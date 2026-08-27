using System.Net;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2StateBridge;

internal static class BridgeServer
{
    private const string Prefix = "http://127.0.0.1:38281/";
    private const string HealthResponse =
        "{\"ok\":true,\"bridge\":\"Sts2StateBridge\",\"bridge_version\":\"0.8.0\",\"game_version_target\":\"v0.111.0\",\"write_enabled\":false}";

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

        string body;
        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.Headers[HttpResponseHeader.Allow] = "GET";
            body = "{\"error\":\"method_not_allowed\"}";
        }
        else if (string.Equals(context.Request.Url?.AbsolutePath, "/health", StringComparison.Ordinal))
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            body = HealthResponse;
        }
        else if (string.Equals(context.Request.Url?.AbsolutePath, "/snapshot", StringComparison.Ordinal))
        {
            (response.StatusCode, body) = await BuildSnapshotResponseAsync().ConfigureAwait(false);
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
