using System.Net;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2StateBridge;

internal static class GameActionService
{
    private static string? _lastAcceptedStateId;

    public static ActionResponsePayload Execute(ActionRequestPayload request)
    {
        SnapshotPayload snapshot = SnapshotService.Capture();
        if (!string.Equals(request.StateId, snapshot.StateId, StringComparison.Ordinal))
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "stale_state",
                "state_id does not match the current game state");
        }

        if (string.Equals(_lastAcceptedStateId, request.StateId, StringComparison.Ordinal))
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "state_already_used",
                "an action was already accepted for this state_id");
        }

        CombatActionSnapshotPayload? combatCandidate = snapshot.Combat?.Actions.FirstOrDefault(action =>
            string.Equals(action.ActionId, request.ActionId, StringComparison.Ordinal));
        InteractionActionSnapshotPayload? interactionCandidate = snapshot.Interaction?.Actions.FirstOrDefault(action =>
            string.Equals(action.ActionId, request.ActionId, StringComparison.Ordinal));
        if (combatCandidate is null && interactionCandidate is null)
        {
            throw new ActionRequestException(
                HttpStatusCode.UnprocessableEntity,
                "invalid_action",
                "action_id is not an available action in the current snapshot");
        }

        string actionType;
        if (combatCandidate is not null)
        {
            ExecuteCombat(combatCandidate);
            actionType = combatCandidate.Type;
        }
        else
        {
            ExecuteInteraction(interactionCandidate!);
            actionType = interactionCandidate!.Type;
        }

        _lastAcceptedStateId = request.StateId;
        return new ActionResponsePayload
        {
            Accepted = true,
            StateId = request.StateId!,
            ActionId = request.ActionId!,
            ActionType = actionType
        };
    }

    private static void ExecuteCombat(CombatActionSnapshotPayload candidate)
    {
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        Player? player = combatState is null ? null : LocalContext.GetMe((ICombatState)combatState);
        if (combatState is null || player?.PlayerCombatState is null || !IsReady(player))
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "combat_not_ready",
                "combat is not ready to accept a player action");
        }

        switch (candidate.Type)
        {
            case "play_card":
                EnqueueCard(combatState, player, candidate);
                break;
            case "use_potion":
                EnqueuePotion(combatState, player, candidate);
                break;
            case "end_turn":
                PlayerCmd.EndTurn(player, false);
                break;
            default:
                throw Unsupported(candidate.Type);
        }
    }

    private static void ExecuteInteraction(InteractionActionSnapshotPayload candidate)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        object? scene = InteractionSnapshotService.FindRelevantNode(
            ActiveScreenContext.Instance.GetCurrentScreen());
        if (runState is null || scene is null)
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "interaction_not_ready",
                "interaction is no longer available");
        }

        switch (candidate.Type)
        {
            case "claim_reward":
                ClaimReward(scene, candidate);
                break;
            case "select_card":
                SelectCardReward(scene, candidate);
                break;
            case "select_alternative":
                SelectCardAlternative(scene, candidate);
                break;
            case "discard_potion":
                DiscardPotion(runState, candidate);
                break;
            case "select_rest_option":
                SelectRestOption(scene, candidate);
                break;
            case "upgrade_card":
                UpgradeCard(scene, candidate);
                break;
            case "open_chest":
                OpenChest(scene);
                break;
            case "claim_relic":
                ClaimTreasureRelic(scene);
                break;
            case "proceed":
                Proceed(scene);
                break;
            default:
                throw Unsupported(candidate.Type);
        }
    }

    private static void ClaimReward(object scene, InteractionActionSnapshotPayload candidate)
    {
        object[] buttons = ReflectionRead.Items(ReflectionRead.Value(scene, "_rewardButtons", "RewardButtons")).ToArray();
        object? button = buttons.Select((value, index) => new
            {
                Button = value,
                Reward = ReflectionRead.Value(value, "Reward") ?? value,
                Index = index
            })
            .FirstOrDefault(value => string.Equals(
                InteractionSnapshotService.RewardOptionId(
                    value.Reward,
                    value.Index,
                    ReflectionRead.Text(value.Reward, "RewardType")?.ToLowerInvariant()
                        ?? value.Reward.GetType().Name),
                candidate.OptionId,
                StringComparison.Ordinal))?.Button;
        if (button is null || !InteractionSnapshotService.ControlEnabled(button))
            throw InteractionChanged("reward is no longer available");
        ReflectionRead.Invoke(button, "OnRelease");
    }

    private static void SelectCardReward(object scene, InteractionActionSnapshotPayload candidate)
    {
        object? card = ReflectionRead.Items(ReflectionRead.Value(scene, "_options", "Options"))
            .Select((option, index) => new
            {
                Card = InteractionSnapshotService.ExtractModel(option, "CardModel", "Card", "Model") ?? option,
                Index = index
            })
            .FirstOrDefault(value => string.Equals(
                $"card_reward:{RunInventoryService.InstanceId(value.Card, value.Index.ToString())}",
                candidate.OptionId,
                StringComparison.Ordinal))?.Card;
        if (card is null) throw InteractionChanged("card reward is no longer available");
        object? holder = ReflectionRead.Invoke(scene, "GetCardHolder", card);
        if (holder is null) throw InteractionChanged("card holder is no longer available");
        ReflectionRead.Invoke(scene, "SelectCard", holder);
    }

    private static void SelectCardAlternative(object scene, InteractionActionSnapshotPayload candidate)
    {
        object[] options = ReflectionRead.Items(ReflectionRead.Value(scene, "_extraOptions", "ExtraOptions")).ToArray();
        int index = Array.FindIndex(options, option => string.Equals(
            $"card_reward:extra:{ReflectionRead.Text(option, "OptionId")}",
            candidate.OptionId,
            StringComparison.Ordinal));
        if (index < 0) throw InteractionChanged("card reward alternative is no longer available");
        ReflectionRead.Invoke(scene, "OnAlternateRewardSelected", index);
    }

    private static void DiscardPotion(RunState runState, InteractionActionSnapshotPayload candidate)
    {
        Player? player = LocalContext.GetMe((IPlayerCollection)runState);
        PotionModel? potion = candidate.PotionSlot is null
            ? null
            : player?.PotionSlots.ElementAtOrDefault(candidate.PotionSlot.Value);
        if (player is null || !player.CanUseOrRemovePotions || potion is null || !string.Equals(
                RunInventoryService.InstanceId(potion, string.Empty),
                candidate.PotionInstanceId,
                StringComparison.Ordinal))
        {
            throw InteractionChanged("potion is no longer discardable");
        }
        _ = PotionCmd.Discard(potion);
    }

    private static void SelectRestOption(object scene, InteractionActionSnapshotPayload candidate)
    {
        object? option = ReflectionRead.Items(ReflectionRead.Value(scene, "Options"))
            .Select(entry => ReflectionRead.Value(entry, "Option") ?? entry)
            .FirstOrDefault(value => string.Equals(
                $"rest:{ReflectionRead.Text(value, "OptionId")}",
                candidate.OptionId,
                StringComparison.Ordinal));
        if (option is null || !(ReflectionRead.Bool(option, "IsEnabled") ?? false))
            throw InteractionChanged("rest site option is no longer enabled");
        object? button = ReflectionRead.Invoke(scene, "GetButtonForOption", option);
        if (button is null || !InteractionSnapshotService.ControlEnabled(button))
            throw InteractionChanged("rest site button is no longer available");
        _ = ReflectionRead.Invoke(button, "SelectOption", option);
    }

    private static void UpgradeCard(object scene, InteractionActionSnapshotPayload candidate)
    {
        CardModel? card = ReflectionRead.Items(ReflectionRead.Value(scene, "_cards", "Cards"))
            .OfType<CardModel>()
            .FirstOrDefault(value => string.Equals(
                $"rest:smith:{RunInventoryService.InstanceId(value, string.Empty)}",
                candidate.OptionId,
                StringComparison.Ordinal));
        if (card is null || card.IsUpgraded) throw InteractionChanged("card is no longer upgradeable");
        ReflectionRead.Invoke(scene, "OnCardClicked", card);
        object? confirm = ReflectionRead.Value(scene, "_singlePreviewConfirmButton");
        if (confirm is null || !InteractionSnapshotService.ControlEnabled(confirm))
            throw InteractionChanged("upgrade confirmation is unavailable");
        ReflectionRead.Invoke(scene, "ConfirmSelection", confirm);
    }

    private static void OpenChest(object scene)
    {
        if (scene.GetType().Name != "NTreasureRoom") throw InteractionChanged("treasure room is unavailable");
        object? button = ReflectionRead.Value(scene, "_chestButton");
        if (button is null || !InteractionSnapshotService.ControlEnabled(button))
            throw InteractionChanged("chest button is unavailable");
        ReflectionRead.Invoke(scene, "OnChestButtonReleased", button);
    }

    private static void ClaimTreasureRelic(object scene)
    {
        object collection = scene.GetType().Name == "NTreasureRoom"
            ? ReflectionRead.Value(scene, "_relicCollection") ?? throw InteractionChanged("relic collection is unavailable")
            : scene;
        object? holder = ReflectionRead.Value(collection, "SingleplayerRelicHolder");
        if (holder is null || !InteractionSnapshotService.ControlEnabled(holder))
            throw InteractionChanged("relic is no longer available");
        ReflectionRead.Invoke(collection, "PickRelic", holder);
    }

    private static void Proceed(object scene)
    {
        object? button = ReflectionRead.Value(scene, "_proceedButton", "ProceedButton");
        if (button is null || !InteractionSnapshotService.ControlEnabled(button))
            throw InteractionChanged("proceed button is unavailable");
        switch (scene.GetType().Name)
        {
            case "NRewardsScreen":
                ReflectionRead.Invoke(scene, "OnProceedButtonPressed", button);
                break;
            case "NRestSiteRoom":
            case "NTreasureRoom":
                ReflectionRead.Invoke(scene, "OnProceedButtonReleased", button);
                break;
            default:
                throw InteractionChanged("proceed action is unavailable on this screen");
        }
    }

    private static ActionRequestException InteractionChanged(string message) => new(
        HttpStatusCode.Conflict,
        "interaction_changed",
        message);

    private static ActionRequestException Unsupported(string type) => new(
        HttpStatusCode.UnprocessableEntity,
        "unsupported_action_type",
        $"action type '{type}' is not supported");

    private static bool IsReady(Player player)
    {
        CombatManager manager = CombatManager.Instance;
        return manager.IsInProgress
            && !manager.IsPaused
            && !manager.PlayerActionsDisabled
            && !manager.IsOverOrEnding
            && manager.IsPartOfPlayerTurn(player)
            && !manager.IsExecutingCardOrPotionEffect(player);
    }

    private static void EnqueueCard(
        CombatState combatState,
        Player player,
        CombatActionSnapshotPayload candidate)
    {
        CardModel? card = player.PlayerCombatState?.Hand.Cards.FirstOrDefault(value =>
            string.Equals(
                CombatSnapshotService.GetInstanceId(value, string.Empty),
                candidate.CardInstanceId,
                StringComparison.Ordinal));
        if (card is null || !card.CanPlay(out _, out _))
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "card_not_playable",
                "the selected card is no longer playable");
        }

        Creature? target = ResolveTarget(combatState, candidate.TargetInstanceId);
        if (candidate.TargetInstanceId is not null && target is null)
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "target_unavailable",
                "the selected target is no longer available");
        }

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, target));
    }

    private static void EnqueuePotion(
        CombatState combatState,
        Player player,
        CombatActionSnapshotPayload candidate)
    {
        PotionModel? potion = candidate.PotionSlot is null
            ? null
            : player.PotionSlots.ElementAtOrDefault(candidate.PotionSlot.Value);
        if (potion is null || !string.Equals(
                CombatSnapshotService.GetInstanceId(potion, string.Empty),
                candidate.PotionInstanceId,
                StringComparison.Ordinal))
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "potion_unavailable",
                "the selected potion is no longer available");
        }

        Creature? target = ResolveTarget(combatState, candidate.TargetInstanceId);
        if (candidate.TargetInstanceId is not null && target is null)
        {
            throw new ActionRequestException(
                HttpStatusCode.Conflict,
                "target_unavailable",
                "the selected target is no longer available");
        }

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new UsePotionAction(potion, target, true));
    }

    private static Creature? ResolveTarget(CombatState combatState, string? instanceId)
    {
        if (instanceId is null)
        {
            return null;
        }

        IEnumerable<Creature> creatures = combatState.Enemies
            .Concat(combatState.Players.Select(player => player.Creature))
            .Concat(combatState.Players.SelectMany(player => player.PlayerCombatState?.Pets ?? []));
        return creatures.FirstOrDefault(creature => string.Equals(
            CombatSnapshotService.GetInstanceId(creature, string.Empty),
            instanceId,
            StringComparison.Ordinal));
    }
}

internal sealed class ActionRequestPayload
{
    [JsonPropertyName("state_id")]
    public string? StateId { get; init; }

    [JsonPropertyName("action_id")]
    public string? ActionId { get; init; }
}

internal sealed class ActionResponsePayload
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("state_id")]
    public required string StateId { get; init; }

    [JsonPropertyName("action_id")]
    public required string ActionId { get; init; }

    [JsonPropertyName("action_type")]
    public required string ActionType { get; init; }
}

internal sealed class ActionRequestException(
    HttpStatusCode statusCode,
    string errorCode,
    string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}
