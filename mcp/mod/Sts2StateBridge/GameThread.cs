using MegaCrit.Sts2.Core.Logging;

namespace Sts2StateBridge;

internal static class GameThread
{
    private static readonly object Sync = new();

    private static SynchronizationContext? _context;
    private static int _threadId;

    public static void Initialize()
    {
        lock (Sync)
        {
            _context = SynchronizationContext.Current;
            _threadId = Environment.CurrentManagedThreadId;

            if (_context is null)
            {
                Log.Error("[Sts2StateBridge] failed to capture the game thread synchronization context");
                return;
            }

            Log.Info($"[Sts2StateBridge] captured game thread context on managed thread {_threadId}");
        }
    }

    public static Task<T> InvokeAsync<T>(Func<T> action)
    {
        SynchronizationContext? context;
        int threadId;

        lock (Sync)
        {
            context = _context;
            threadId = _threadId;
        }

        if (context is null)
        {
            throw new InvalidOperationException("Game thread context is not available.");
        }

        if (Environment.CurrentManagedThreadId == threadId)
        {
            return Task.FromResult(action());
        }

        TaskCompletionSource<T> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Post(_ =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, null);

        return completion.Task;
    }
}
