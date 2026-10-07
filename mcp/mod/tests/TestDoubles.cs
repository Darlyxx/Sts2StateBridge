// UI doubles test gating, not Godot integration.
namespace Godot
{
    public class CanvasItem
    {
        public bool Visible { get; set; } = true;
        public bool Processing { get; set; } = true;
        public bool IsVisibleInTree() => Visible;
        public bool CanProcess() => Processing;
    }
}
namespace MegaCrit.Sts2.Core.Logging
{
    internal static class Log
    {
        public static void Error(string message) { }
        public static void Info(string message) { }
    }
}
namespace MegaCrit.Sts2.Core.Nodes
{
    internal sealed class NRun
    {
        public static NRun Instance { get; } = new();
        public GlobalUi GlobalUi { get; } = new();
    }
    internal sealed class GlobalUi
    {
        public TopBar TopBar { get; } = new();
        public NMapScreen MapScreen { get; } = new();
    }
    internal sealed class TopBar { public TestButton Map { get; } = new(); }
}
internal sealed class TestButton : Godot.CanvasItem
{
    public bool IsEnabled { get; set; } = true;
    public int Clicks { get; private set; }
    private void OnRelease() => Clicks++;
}
internal sealed class NMapScreen
{
    public bool IsOpen { get; set; }
    public bool IsTraveling { get; set; }
    public bool _isInputDisabled { get; set; }
    public TestButton _backButton { get; } = new();
    public int BackClicks { get; private set; }
    private void OnBackButtonPressed(TestButton button) => BackClicks++;
}
internal sealed class NRewardsScreen { }
internal sealed class NDeckEnchantSelectScreen { }
internal sealed class UnknownModal { }
namespace Sts2StateBridge
{
    internal static class ReflectionRead
    {
        private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        public static object? Value(object? value, string name) => value?.GetType().GetProperty(name, Flags)?.GetValue(value);
        public static bool? Bool(object? value, string name) => Value(value, name) as bool?;
        public static object? Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    }
    internal sealed class InteractionActionSnapshotPayload
    {
        public required string ActionId { get; init; }
        public required string Type { get; init; }
        public string? Label { get; init; }
    }
    internal sealed class ActionRequestException(System.Net.HttpStatusCode status, string code, string message)
        : Exception($"{status}: {code}: {message}");
}
