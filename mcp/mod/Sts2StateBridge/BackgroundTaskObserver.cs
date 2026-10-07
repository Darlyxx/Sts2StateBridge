using MegaCrit.Sts2.Core.Logging;

namespace Sts2StateBridge;

internal static class BackgroundTaskObserver
{
    internal static void Observe(Task task, string operation)
    {
        _ = task.ContinueWith(completed =>
        {
            Exception? failure = completed.Exception?.GetBaseException();
            Log.Error($"[Sts2StateBridge] accepted operation failed asynchronously: {operation}: {failure?.GetType().Name}");
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }
}
