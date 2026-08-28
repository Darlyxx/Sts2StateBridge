using System.Net;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2StateBridge;

internal static class CombatActionService
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

        CombatSnapshotPayload? combat = snapshot.Combat;
        CombatActionSnapshotPayload? candidate = combat?.Actions.FirstOrDefault(action =>
            string.Equals(action.ActionId, request.ActionId, StringComparison.Ordinal));
        if (candidate is null)
        {
            throw new ActionRequestException(
                HttpStatusCode.UnprocessableEntity,
                "invalid_action",
                "action_id is not an available action in the current snapshot");
        }

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
                throw new ActionRequestException(
                    HttpStatusCode.UnprocessableEntity,
                    "unsupported_action_type",
                    $"action type '{candidate.Type}' is not supported");
        }

        _lastAcceptedStateId = request.StateId;
        return new ActionResponsePayload
        {
            Accepted = true,
            StateId = request.StateId!,
            ActionId = request.ActionId!,
            ActionType = candidate.Type
        };
    }

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
