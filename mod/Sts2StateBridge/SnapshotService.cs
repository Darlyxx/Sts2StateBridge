using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2StateBridge;

internal static class SnapshotService
{
    public static SnapshotPayload Capture()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        bool combatInProgress = CombatManager.Instance.IsInProgress;

        bool inRun = runState is not null;
        bool hasCombatState = combatState is not null;
        RunSnapshotPayload? run = BuildRunSnapshot(runState);
        CombatSnapshotPayload? combat = combatInProgress
            ? CombatSnapshotService.Build(combatState)
            : null;
        InteractionSnapshotPayload? interaction = combat is null && runState is not null
            ? InteractionSnapshotService.Build(currentScreen, runState)
            : null;

        string phase;
        if (combatInProgress)
        {
            phase = hasCombatState && combat is not null && inRun ? "combat" : "unknown";
        }
        else if (inRun)
        {
            phase = "run";
        }
        else if (currentScreen is NMainMenu)
        {
            phase = "main_menu";
        }
        else if (currentScreen is not null)
        {
            phase = "menu";
        }
        else
        {
            phase = "loading";
        }

        SnapshotPayload payload = new()
        {
            Phase = phase,
            ScreenType = currentScreen?.GetType().Name,
            InRun = inRun,
            InCombat = hasCombatState && combatInProgress,
            Run = run,
            Combat = combat,
            Interaction = interaction
        };
        payload.StateId = SnapshotStateIdService.Compute(payload);
        return payload;
    }

    private static RunSnapshotPayload? BuildRunSnapshot(RunState? runState)
    {
        if (runState is null)
        {
            return null;
        }

        var player = LocalContext.GetMe((IPlayerCollection)runState);
        if (player is null)
        {
            return null;
        }

        return new RunSnapshotPayload
        {
            CharacterId = player.Character.Id.Entry,
            CharacterName = player.Character.Title.GetFormattedText(),
            CurrentHp = player.Creature.CurrentHp,
            MaxHp = player.Creature.MaxHp,
            Gold = player.Gold,
            Floor = runState.TotalFloor,
            ActIndex = runState.CurrentActIndex,
            ActNumber = runState.CurrentActIndex + 1,
            ActFloor = runState.ActFloor,
            ActId = runState.Act?.Id.Entry,
            Ascension = RunInventoryService.ReadAscension(runState, player),
            Deck = RunInventoryService.BuildDeck(player),
            Relics = RunInventoryService.BuildRelics(player),
            Potions = RunInventoryService.BuildPotions(player)
        };
    }
}

internal sealed class SnapshotPayload
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("bridge_version")]
    public string BridgeVersion { get; init; } = "0.8.0";

    [JsonPropertyName("state_id")]
    public string? StateId { get; set; }

    [JsonPropertyName("phase")]
    public required string Phase { get; init; }

    [JsonPropertyName("screen_type")]
    public string? ScreenType { get; init; }

    [JsonPropertyName("in_run")]
    public bool InRun { get; init; }

    [JsonPropertyName("in_combat")]
    public bool InCombat { get; init; }

    [JsonPropertyName("run")]
    public RunSnapshotPayload? Run { get; init; }

    [JsonPropertyName("combat")]
    public CombatSnapshotPayload? Combat { get; init; }

    [JsonPropertyName("interaction")]
    public InteractionSnapshotPayload? Interaction { get; init; }
}

internal sealed class RunSnapshotPayload
{
    [JsonPropertyName("character_id")]
    public required string CharacterId { get; init; }

    [JsonPropertyName("character_name")]
    public required string CharacterName { get; init; }

    [JsonPropertyName("current_hp")]
    public int CurrentHp { get; init; }

    [JsonPropertyName("max_hp")]
    public int MaxHp { get; init; }

    [JsonPropertyName("gold")]
    public int Gold { get; init; }

    [JsonPropertyName("floor")]
    public int Floor { get; init; }

    [JsonPropertyName("act_index")]
    public int ActIndex { get; init; }

    [JsonPropertyName("act_number")]
    public int ActNumber { get; init; }

    [JsonPropertyName("act_floor")]
    public int ActFloor { get; init; }

    [JsonPropertyName("act_id")]
    public string? ActId { get; init; }

    [JsonPropertyName("ascension")]
    public int? Ascension { get; init; }

    [JsonPropertyName("deck")]
    public RunCardSnapshotPayload[] Deck { get; init; } = [];

    [JsonPropertyName("relics")]
    public RunRelicSnapshotPayload[] Relics { get; init; } = [];

    [JsonPropertyName("potions")]
    public RunPotionSnapshotPayload[] Potions { get; init; } = [];
}
