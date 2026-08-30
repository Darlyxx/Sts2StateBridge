using System.Net;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2StateBridge;

internal static class BridgeServer
{
    private const string Prefix = "http://127.0.0.1:38281/";
    private const int MaxActionBodyCharacters = 8192;

    private static readonly object Sync = new();
    private static HttpListener? _listener;

    public static void Start()
    {
        lock (Sync)
        {
            if (_listener is not null)
            {
                return;
            }

            try
            {
                HttpListener listener = new();
                listener.Prefixes.Add(Prefix);
                listener.Start();
                _listener = listener;

                AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
                _ = Task.Run(() => ListenAsync(listener));

                Log.Info($"[Sts2StateBridge] health endpoint listening at {Prefix}health");
            }
            catch (Exception exception)
            {
                _listener = null;
                Log.Error($"[Sts2StateBridge] failed to start health endpoint: {exception}");
            }
        }
    }

    private static async Task ListenAsync(HttpListener listener)
    {
        try
        {
            while (listener.IsListening)
            {
                HttpListenerContext context = await listener.GetContextAsync().ConfigureAwait(false);
                await RespondAsync(context).ConfigureAwait(false);
            }
        }
        catch (HttpListenerException) when (!listener.IsListening)
        {
            // Expected when the game process is shutting down.
        }
        catch (ObjectDisposedException)
        {
            // Expected when the game process is shutting down.
        }
        catch (Exception exception)
        {
            Log.Error($"[Sts2StateBridge] health endpoint stopped unexpectedly: {exception}");
            Stop();
        }
    }

    private static async Task RespondAsync(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers[HttpResponseHeader.CacheControl] = "no-store";

        string path = context.Request.Url?.AbsolutePath ?? string.Empty;
        string body;
        if (string.Equals(path, "/health", StringComparison.Ordinal))
        {
            if (!RequireMethod(context, "GET", out body))
            {
                return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            body = JsonSerializer.Serialize(new
            {
                ok = true,
                bridge = "Sts2StateBridge",
                bridge_version = "0.11.0",
                game_version_target = "v0.111.0",
                write_enabled = BridgeConfiguration.WriteEnabled
            });
        }
        else if (string.Equals(path, "/snapshot", StringComparison.Ordinal))
        {
            if (!RequireMethod(context, "GET", out body))
            {
                return;
            }

            (response.StatusCode, body) = await BuildSnapshotResponseAsync().ConfigureAwait(false);
        }
        else if (string.Equals(path, "/action", StringComparison.Ordinal))
        {
            if (!RequireMethod(context, "POST", out body))
            {
                return;
            }

            (response.StatusCode, body) = await BuildActionResponseAsync(context.Request)
                .ConfigureAwait(false);
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            body = "{\"error\":\"not_found\"}";
        }

        byte[] payload = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = payload.Length;

        try
        {
            await response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }

    private static bool RequireMethod(
        HttpListenerContext context,
        string requiredMethod,
        out string body)
    {
        if (string.Equals(
                context.Request.HttpMethod,
                requiredMethod,
                StringComparison.OrdinalIgnoreCase))
        {
            body = string.Empty;
            return true;
        }

        context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
        context.Response.Headers[HttpResponseHeader.Allow] = requiredMethod;
        body = "{\"error\":\"method_not_allowed\"}";
        WriteResponseAndClose(context.Response, body).GetAwaiter().GetResult();
        return false;
    }

    private static async Task<(int StatusCode, string Body)> BuildActionResponseAsync(
        HttpListenerRequest request)
    {
        if (!BridgeConfiguration.WriteEnabled)
        {
            return ((int)HttpStatusCode.Forbidden,
                "{\"error\":\"write_disabled\",\"message\":\"write actions are disabled by local configuration\"}");
        }

        try
        {
            string requestBody = await ReadBoundedBodyAsync(request).ConfigureAwait(false);
            ActionRequestPayload? actionRequest = JsonSerializer.Deserialize<ActionRequestPayload>(requestBody);
            if (actionRequest is null
                || string.IsNullOrWhiteSpace(actionRequest.StateId)
                || string.IsNullOrWhiteSpace(actionRequest.ActionId))
            {
                return ((int)HttpStatusCode.BadRequest,
                    "{\"error\":\"invalid_request\",\"message\":\"state_id and action_id are required\"}");
            }

            Task<ActionResponsePayload> actionTask = GameThread.InvokeAsync(
                () => GameActionService.Execute(actionRequest));
            Task completedTask = await Task.WhenAny(
                    actionTask,
                    Task.Delay(TimeSpan.FromSeconds(2)))
                .ConfigureAwait(false);
            if (completedTask != actionTask)
            {
                _ = actionTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                return ((int)HttpStatusCode.ServiceUnavailable,
                    "{\"error\":\"action_unavailable\",\"message\":\"game thread timeout\"}");
            }

            ActionResponsePayload result = await actionTask.ConfigureAwait(false);
            Log.Info($"[Sts2StateBridge] accepted action {result.ActionType} for state {result.StateId}");
            return ((int)HttpStatusCode.Accepted, JsonSerializer.Serialize(result));
        }
        catch (ActionRequestException exception)
        {
            return ((int)exception.StatusCode, JsonSerializer.Serialize(new
            {
                error = exception.ErrorCode,
                message = exception.Message
            }));
        }
        catch (JsonException)
        {
            return ((int)HttpStatusCode.BadRequest,
                "{\"error\":\"invalid_json\",\"message\":\"request body must be valid JSON\"}");
        }
        catch (InvalidDataException exception)
        {
            return ((int)HttpStatusCode.RequestEntityTooLarge, JsonSerializer.Serialize(new
            {
                error = "request_too_large",
                message = exception.Message
            }));
        }
        catch (Exception exception)
        {
            Log.Error($"[Sts2StateBridge] failed to execute action: {exception}");
            return ((int)HttpStatusCode.ServiceUnavailable,
                "{\"error\":\"action_unavailable\",\"message\":\"action execution failed\"}");
        }
    }

    private static async Task<string> ReadBoundedBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxActionBodyCharacters)
        {
            throw new InvalidDataException("request body exceeds the 8192 character limit");
        }

        using StreamReader reader = new(request.InputStream, request.ContentEncoding, false, 1024, false);
        char[] buffer = new char[MaxActionBodyCharacters + 1];
        int count = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        if (count > MaxActionBodyCharacters)
        {
            throw new InvalidDataException("request body exceeds the 8192 character limit");
        }

        return new string(buffer, 0, count);
    }

    private static async Task WriteResponseAndClose(HttpListenerResponse response, string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = payload.Length;
        try
        {
            await response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }

    private static async Task<(int StatusCode, string Body)> BuildSnapshotResponseAsync()
    {
        try
        {
            Task<SnapshotPayload> snapshotTask = GameThread.InvokeAsync(SnapshotService.Capture);
            Task completedTask = await Task.WhenAny(
                    snapshotTask,
                    Task.Delay(TimeSpan.FromSeconds(2)))
                .ConfigureAwait(false);

            if (completedTask != snapshotTask)
            {
                _ = snapshotTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

                Log.Error("[Sts2StateBridge] snapshot request timed out waiting for the game thread");
                return ((int)HttpStatusCode.ServiceUnavailable,
                    "{\"error\":\"snapshot_unavailable\",\"reason\":\"game_thread_timeout\"}");
            }

            SnapshotPayload snapshot = await snapshotTask.ConfigureAwait(false);
            return ((int)HttpStatusCode.OK, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception exception)
        {
            Log.Error($"[Sts2StateBridge] failed to capture snapshot: {exception}");
            return ((int)HttpStatusCode.ServiceUnavailable,
                "{\"error\":\"snapshot_unavailable\",\"reason\":\"capture_failed\"}");
        }
    }

    private static void Stop()
    {
        lock (Sync)
        {
            HttpListener? listener = _listener;
            _listener = null;

            if (listener is null)
            {
                return;
            }

            listener.Close();
        }
    }
}
