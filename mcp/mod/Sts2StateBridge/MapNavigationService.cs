using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace Sts2StateBridge;

// Only expose native, visible UI controls; never enable map travel ourselves.
internal static class MapNavigationService
{
    internal static object? FindControl(object? screen, string action)
    {
        string? type = screen?.GetType().Name;
        object? control;
        if (action == "close_map")
        {
            if (type != "NMapScreen"
                || ReflectionRead.Bool(screen, "IsOpen") != true
                || ReflectionRead.Bool(screen, "IsTraveling") != false
                || ReflectionRead.Bool(screen, "_isInputDisabled") != false)
                return null;
            control = ReflectionRead.Value(screen, "_backButton");
        }
        else if (action == "open_map")
        {
            // Mandatory selectors and unknown/modal screens fail closed.
            if (type is not ("NRewardsScreen" or "NEventRoom" or "NMerchantRoom"
                or "NMerchantInventory" or "NRestSiteRoom" or "NTreasureRoom"
                or "NMapRoom" or "NCombatRoom")) return null;
            object? globalUi = ReflectionRead.Value(NRun.Instance, "GlobalUi");
            object? map = ReflectionRead.Value(globalUi, "MapScreen");
            if (ReflectionRead.Bool(map, "IsOpen") != false
                || ReflectionRead.Bool(map, "IsTraveling") != false) return null;
            control = ReflectionRead.Value(ReflectionRead.Value(globalUi, "TopBar"), "Map");
        }
        else return null;

        return control is CanvasItem canvas && canvas.IsVisibleInTree() && canvas.CanProcess()
            && ReflectionRead.Bool(control, "IsEnabled") == true ? control : null;
    }

    internal static InteractionActionSnapshotPayload? Candidate(object? screen)
    {
        string type = screen?.GetType().Name == "NMapScreen" ? "close_map" : "open_map";
        return FindControl(screen, type) is null ? null : new InteractionActionSnapshotPayload
        {
            ActionId = $"navigation:{type}", Type = type,
            Label = type == "open_map" ? "打开地图（能否移动由游戏决定）" : "返回当前房间"
        };
    }

    internal static void Execute(object? screen, string action)
    {
        object? control = FindControl(screen, action);
        if (control is null)
            throw new ActionRequestException(System.Net.HttpStatusCode.Conflict,
                "navigation_not_ready", "map control is no longer visible or enabled");
        if (action == "close_map") ReflectionRead.Invoke(screen!, "OnBackButtonPressed", control);
        else ReflectionRead.Invoke(control, "OnRelease");
    }
}
