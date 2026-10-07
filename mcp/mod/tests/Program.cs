using System.Collections.Concurrent;
using Sts2StateBridge;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}
QueuedContext context = new();
SynchronizationContext.SetSynchronizationContext(context);
GameThread.Initialize();
int executed = 0;
using (CancellationTokenSource deadline = new())
{
    Task<int> request = Task.Run(() => GameThread.InvokeAsync(() => ++executed, deadline.Token));
    context.WaitForPost();
    deadline.Cancel();
    context.Drain();
    try { request.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
    Check(request.IsCanceled && executed == 0, "expired queued action never executes");
}
Task<int> next = Task.Run(() => GameThread.InvokeAsync(() => ++executed));
context.WaitForPost();
context.Drain();
Check(next.GetAwaiter().GetResult() == 1, "dispatcher recovers after cancellation");
Task<int> failing = Task.Run(() => GameThread.InvokeAsync<int>(() => throw new InvalidOperationException("test")));
context.WaitForPost();
context.Drain();
try { failing.GetAwaiter().GetResult(); } catch (InvalidOperationException) { }
Check(failing.IsFaulted, "dispatcher reports exceptions");

var ui = MegaCrit.Sts2.Core.Nodes.NRun.Instance.GlobalUi;
Check(MapNavigationService.Candidate(new NRewardsScreen())?.Type == "open_map", "rewards expose enabled map button");
Check(MapNavigationService.Candidate(new NDeckEnchantSelectScreen()) is null, "mandatory selection cannot open map");
Check(MapNavigationService.Candidate(new UnknownModal()) is null, "unknown modal fails closed");
ui.TopBar.Map.IsEnabled = false;
Check(MapNavigationService.Candidate(new NRewardsScreen()) is null, "disabled topbar cannot open map");
ui.TopBar.Map.IsEnabled = true;
ui.TopBar.Map.Visible = false;
Check(MapNavigationService.Candidate(new NRewardsScreen()) is null, "hidden topbar cannot open map");
ui.TopBar.Map.Visible = true;
MapNavigationService.Execute(new NRewardsScreen(), "open_map");
Check(ui.TopBar.Map.Clicks == 1, "open invokes native handler once");
NMapScreen map = new() { IsOpen = true };
Check(MapNavigationService.Candidate(map)?.Type == "close_map", "inspection map exposes back without enabling travel");
map.IsTraveling = true;
Check(MapNavigationService.Candidate(map) is null, "cannot close during travel");
map.IsTraveling = false;
map._isInputDisabled = true;
Check(MapNavigationService.Candidate(map) is null, "cannot close during input lock");
map._isInputDisabled = false;
map._backButton.IsEnabled = false;
Check(MapNavigationService.Candidate(map) is null, "disabled back button fails closed");
try { MapNavigationService.Execute(map, "close_map"); throw new Exception("expected rejection"); }
catch (ActionRequestException) { checks++; }
map._backButton.IsEnabled = true;
MapNavigationService.Execute(map, "close_map");
Check(map.BackClicks == 1, "close invokes native back handler once");
Console.WriteLine($"{checks} regression checks passed (UI doubles, not in-game validation).");

sealed class QueuedContext : SynchronizationContext
{
    private readonly ConcurrentQueue<Action> queue = new();
    private readonly AutoResetEvent posted = new(false);
    public override void Post(SendOrPostCallback callback, object? state)
    {
        queue.Enqueue(() => callback(state));
        posted.Set();
    }
    public void WaitForPost()
    {
        if (!posted.WaitOne(TimeSpan.FromSeconds(5))) throw new TimeoutException("No dispatched callback");
    }
    public void Drain() { while (queue.TryDequeue(out Action? action)) action(); }
}
