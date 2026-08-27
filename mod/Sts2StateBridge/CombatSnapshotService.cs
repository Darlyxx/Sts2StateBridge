using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace Sts2StateBridge;

internal static class CombatSnapshotService
{
    public static CombatSnapshotPayload? Build(CombatState? combatState)
    {
        if (combatState is null)
        {
            return null;
        }

        Player? player = LocalContext.GetMe((ICombatState)combatState);
        if (player?.PlayerCombatState is null)
        {
            return null;
        }

        CombatSnapshotPayload payload = new()
        {
            Round = combatState.RoundNumber,
            CurrentSide = combatState.CurrentSide.ToString(),
            IsPlayerTurn = combatState.CurrentSide == CombatSide.Player
                && CombatManager.Instance.IsPartOfPlayerTurn(player),
            Player = new CombatPlayerSnapshotPayload
            {
                CurrentHp = player.Creature.CurrentHp,
                MaxHp = player.Creature.MaxHp,
                Block = player.Creature.Block,
                Energy = player.PlayerCombatState.Energy,
                MaxEnergy = player.MaxEnergy,
                Stars = player.PlayerCombatState.Stars,
                Powers = BuildPowers(player.Creature)
            },
            Hand = player.PlayerCombatState.Hand.Cards
                .Select((card, index) => BuildHandCard(combatState, card, index))
                .ToArray(),
            Enemies = combatState.Enemies
                .Select((enemy, index) => BuildEnemy(enemy, index))
                .ToArray(),
            Piles = BuildPiles(player),
            Potions = BuildPotions(player, combatState),
            Relics = BuildRelics(player)
        };

        payload.Actions = BuildActions(payload);
        return payload;
    }

    private static CombatHandCardSnapshotPayload BuildHandCard(
        CombatState combatState,
        CardModel card,
        int index)
    {
        bool playable = false;
        string? unplayableReason = null;

        try
        {
            playable = card.CanPlay(out UnplayableReason reason, out _)
                && IsSupportedTargetType(card.TargetType);
            unplayableReason = playable || reason == UnplayableReason.None
                ? null
                : NormalizeUnplayableReason(reason);

            if (!IsSupportedTargetType(card.TargetType))
            {
                unplayableReason = "unsupported_target_type";
            }
        }
        catch (Exception exception)
        {
            unplayableReason = $"read_failed:{exception.GetType().Name}";
        }

        return new CombatHandCardSnapshotPayload
        {
            Index = index,
            InstanceId = GetInstanceId(card, $"hand:{index}"),
            CardId = SafeRead(() => card.Id.Entry),
            Name = SafeRead(() => card.Title),
            Upgraded = SafeReadNullable(() => card.IsUpgraded),
            EnergyCost = SafeReadNullable(() => card.EnergyCost.GetWithModifiers(CostModifiers.All)),
            CostsX = SafeReadNullable(() => card.EnergyCost.CostsX),
            Enchantment = RunInventoryService.BuildEnchantment(card),
            TargetType = SafeRead(() => card.TargetType.ToString()),
            RequiresTarget = SafeReadNullable(() => RequiresTarget(card.TargetType)),
            ValidTargetIndices = SafeRead(
                () => GetValidTargetIndices(combatState, card),
                Array.Empty<int>()),
            Playable = playable,
            UnplayableReason = unplayableReason,
            RulesText = SafeRead(() => GetRulesText(card))
        };
    }

    private static CombatEnemySnapshotPayload BuildEnemy(Creature enemy, int index)
    {
        return new CombatEnemySnapshotPayload
        {
            Index = index,
            InstanceId = GetInstanceId(enemy, $"enemy:{index}"),
            EnemyId = SafeRead(() => enemy.ModelId.Entry),
            Name = SafeRead(() => enemy.Name),
            CurrentHp = SafeReadNullable(() => enemy.CurrentHp),
            MaxHp = SafeReadNullable(() => enemy.MaxHp),
            Block = SafeReadNullable(() => enemy.Block),
            IsAlive = SafeReadNullable(() => enemy.IsAlive),
            IsHittable = SafeReadNullable(() => enemy.IsHittable),
            Powers = BuildPowers(enemy),
            Intents = BuildIntents(enemy)
        };
    }

    private static CombatPileSnapshotPayload[] BuildPiles(Player player)
    {
        try
        {
            bool orderKnown = player.Relics.Any(relic =>
                string.Equals(SafeRead(() => relic.Id.Entry), "FROZEN_EYE", StringComparison.Ordinal));

            return player.Piles
                .Where(pile => !string.Equals(pile.Type.ToString(), "Hand", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pile.Type.ToString(), "Deck", StringComparison.OrdinalIgnoreCase))
                .Select(pile =>
                {
                    IEnumerable<CardModel> cards = pile.Cards;
                    if (!orderKnown && string.Equals(pile.Type.ToString(), "Draw", StringComparison.OrdinalIgnoreCase))
                    {
                        cards = cards.OrderBy(card => SafeRead(() => card.Id.Entry))
                            .ThenBy(card => card.IsUpgraded)
                            .ThenBy(card => GetInstanceId(card, string.Empty));
                    }

                    return new CombatPileSnapshotPayload
                    {
                        PileType = pile.Type.ToString(),
                        Count = pile.Cards.Count,
                        OrderKnown = orderKnown || !string.Equals(
                            pile.Type.ToString(), "Draw", StringComparison.OrdinalIgnoreCase),
                        Cards = cards.Select((card, index) => BuildPileCard(card, index)).ToArray()
                    };
                })
                .ToArray();
        }
        catch
        {
            return Array.Empty<CombatPileSnapshotPayload>();
        }
    }

    private static CombatPileCardSnapshotPayload BuildPileCard(CardModel card, int index)
    {
        return new CombatPileCardSnapshotPayload
        {
            Index = index,
            InstanceId = GetInstanceId(card, $"pile:{index}"),
            CardId = SafeRead(() => card.Id.Entry),
            Name = SafeRead(() => card.Title),
            Upgraded = SafeReadNullable(() => card.IsUpgraded),
            EnergyCost = SafeReadNullable(() => card.EnergyCost.GetWithModifiers(CostModifiers.All)),
            CostsX = SafeReadNullable(() => card.EnergyCost.CostsX),
            Enchantment = RunInventoryService.BuildEnchantment(card),
            RulesText = SafeRead(() => GetRulesText(card))
        };
    }

    private static CombatPotionSnapshotPayload[] BuildPotions(Player player, CombatState combatState)
    {
        try
        {
            return player.PotionSlots.Select((potion, slot) =>
            {
                if (potion is null)
                {
                    return new CombatPotionSnapshotPayload { Slot = slot, IsEmpty = true };
                }

                string? targetType = ReadPropertyText(potion, "TargetType");
                bool requiresTarget = string.Equals(targetType, "AnyEnemy", StringComparison.Ordinal)
                    || string.Equals(targetType, "AnyAlly", StringComparison.Ordinal);
                int[] targets = requiresTarget && string.Equals(targetType, "AnyEnemy", StringComparison.Ordinal)
                    ? combatState.Enemies.Select((enemy, index) => new { enemy, index })
                        .Where(entry => entry.enemy.IsAlive && entry.enemy.IsHittable)
                        .Select(entry => entry.index).ToArray()
                    : Array.Empty<int>();

                return new CombatPotionSnapshotPayload
                {
                    Slot = slot,
                    IsEmpty = false,
                    InstanceId = GetInstanceId(potion, $"potion:{slot}"),
                    PotionId = ReadNestedEntry(potion, "Id"),
                    Name = ReadLocalizedProperty(potion, "Title"),
                    RulesText = ReadModelRulesText(potion),
                    TargetType = targetType,
                    RequiresTarget = requiresTarget,
                    ValidTargetIndices = targets,
                    Usable = player.PlayerCombatState is not null
                        && combatState.CurrentSide == CombatSide.Player
                };
            }).ToArray();
        }
        catch
        {
            return Array.Empty<CombatPotionSnapshotPayload>();
        }
    }

    private static CombatRelicSnapshotPayload[] BuildRelics(Player player)
    {
        try
        {
            return player.Relics.Select((relic, index) => new CombatRelicSnapshotPayload
            {
                Index = index,
                InstanceId = GetInstanceId(relic, $"relic:{index}"),
                RelicId = SafeRead(() => relic.Id.Entry),
                Name = ReadLocalizedProperty(relic, "Title"),
                RulesText = ReadModelRulesText(relic),
                Counter = SafeRead(() => relic.ShowCounter ? (int?)relic.DisplayAmount : null)
            }).ToArray();
        }
        catch
        {
            return Array.Empty<CombatRelicSnapshotPayload>();
        }
    }

    private static CombatActionSnapshotPayload[] BuildActions(CombatSnapshotPayload snapshot)
    {
        if (!snapshot.IsPlayerTurn)
        {
            return Array.Empty<CombatActionSnapshotPayload>();
        }

        List<CombatActionSnapshotPayload> actions = new();
        foreach (CombatHandCardSnapshotPayload card in snapshot.Hand.Where(card => card.Playable))
        {
            int?[] targets = card.RequiresTarget == true
                ? card.ValidTargetIndices.Cast<int?>().ToArray()
                : [null];
            foreach (int? targetIndex in targets)
            {
                CombatEnemySnapshotPayload? target = targetIndex is null
                    ? null
                    : snapshot.Enemies.ElementAtOrDefault(targetIndex.Value);
                actions.Add(new CombatActionSnapshotPayload
                {
                    ActionId = $"play_card:{card.InstanceId}:{target?.InstanceId ?? "none"}",
                    Type = "play_card",
                    CardInstanceId = card.InstanceId,
                    CardIndex = card.Index,
                    TargetInstanceId = target?.InstanceId,
                    TargetIndex = targetIndex
                });
            }
        }

        foreach (CombatPotionSnapshotPayload potion in snapshot.Potions.Where(p => !p.IsEmpty && p.Usable))
        {
            int?[] targets = potion.RequiresTarget
                ? potion.ValidTargetIndices.Cast<int?>().ToArray()
                : [null];
            foreach (int? targetIndex in targets)
            {
                CombatEnemySnapshotPayload? target = targetIndex is null
                    ? null
                    : snapshot.Enemies.ElementAtOrDefault(targetIndex.Value);
                actions.Add(new CombatActionSnapshotPayload
                {
                    ActionId = $"use_potion:{potion.InstanceId}:{target?.InstanceId ?? "none"}",
                    Type = "use_potion",
                    PotionInstanceId = potion.InstanceId,
                    PotionSlot = potion.Slot,
                    TargetInstanceId = target?.InstanceId,
                    TargetIndex = targetIndex
                });
            }
        }

        actions.Add(new CombatActionSnapshotPayload
        {
            ActionId = "end_turn",
            Type = "end_turn"
        });
        return actions.ToArray();
    }

    private static string GetInstanceId(object value, string fallback)
    {
        object? netId = value.GetType().GetProperty("NetId")?.GetValue(value);
        if (netId is not null)
        {
            return netId.ToString() ?? fallback;
        }

        string modelId = ReadNestedEntry(value, "Id")
            ?? ReadNestedEntry(value, "ModelId")
            ?? value.GetType().Name;
        return $"{modelId}:{RuntimeHelpers.GetHashCode(value):x8}";
    }

    private static string? ReadNestedEntry(object value, string propertyName)
    {
        object? nested = value.GetType().GetProperty(propertyName)?.GetValue(value);
        return nested?.GetType().GetProperty("Entry")?.GetValue(nested)?.ToString();
    }

    private static string? ReadPropertyText(object value, string propertyName)
    {
        return value.GetType().GetProperty(propertyName)?.GetValue(value)?.ToString();
    }

    private static string? ReadLocalizedProperty(object value, string propertyName)
    {
        object? localized = value.GetType().GetProperty(propertyName)?.GetValue(value);
        if (localized is null)
        {
            return null;
        }

        return localized.GetType().GetMethod("GetFormattedText", Type.EmptyTypes)
            ?.Invoke(localized, null)?.ToString() ?? localized.ToString();
    }

    private static string? ReadModelRulesText(object value)
    {
        foreach (string propertyName in new[]
                 {
                     "Description", "DescriptionLocString", "RulesText", "Tooltip"
                 })
        {
            string? text = ReadLocalizedProperty(value, propertyName);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        foreach (string methodName in new[] { "GetDescription", "GetRulesText" })
        {
            try
            {
                object? result = value.GetType().GetMethod(methodName, Type.EmptyTypes)
                    ?.Invoke(value, null);
                if (result is not null)
                {
                    string? text = result.GetType().GetMethod("GetFormattedText", Type.EmptyTypes)
                        ?.Invoke(result, null)?.ToString() ?? result.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            catch
            {
                // Try the next compatible member name.
            }
        }

        return null;
    }


    private static CombatIntentSnapshotPayload[] BuildIntents(Creature enemy)
    {
        try
        {
            var nextMove = enemy.Monster?.NextMove;
            if (nextMove is null)
            {
                return Array.Empty<CombatIntentSnapshotPayload>();
            }

            Creature[] targets = enemy.CombatState?.Players
                .Select(player => player.Creature)
                .ToArray() ?? Array.Empty<Creature>();

            return nextMove.Intents
                .Select((intent, index) => BuildIntent(intent, enemy, targets, index))
                .ToArray();
        }
        catch
        {
            return Array.Empty<CombatIntentSnapshotPayload>();
        }
    }

    private static CombatIntentSnapshotPayload BuildIntent(
        AbstractIntent intent,
        Creature owner,
        Creature[] targets,
        int index)
    {
        int? damage = null;
        int? hits = null;
        int? totalDamage = null;
        int? statusCardCount = null;

        if (intent is AttackIntent attackIntent)
        {
            damage = SafeReadNullable(() => attackIntent.GetSingleDamage(targets, owner));
            hits = SafeReadNullable(() => Math.Max(1, attackIntent.Repeats));
            totalDamage = SafeReadNullable(() => attackIntent.GetTotalDamage(targets, owner));
        }

        if (intent is StatusIntent statusIntent)
        {
            statusCardCount = SafeReadNullable(() => statusIntent.CardCount);
        }

        return new CombatIntentSnapshotPayload
        {
            Index = index,
            IntentType = SafeRead(() => intent.IntentType.ToString()),
            Label = SafeRead(() => intent.GetIntentLabel(targets, owner).GetFormattedText()),
            Damage = damage,
            Hits = hits,
            TotalDamage = totalDamage,
            StatusCardCount = statusCardCount
        };
    }

    private static CombatPowerSnapshotPayload[] BuildPowers(Creature creature)
    {
        try
        {
            object? powersValue = creature.GetType().GetProperty("Powers")?.GetValue(creature);
            if (powersValue is not IEnumerable powers)
            {
                return Array.Empty<CombatPowerSnapshotPayload>();
            }

            List<CombatPowerSnapshotPayload> result = new();
            int index = 0;

            foreach (object? power in powers)
            {
                if (power is null)
                {
                    continue;
                }

                Type type = power.GetType();
                object? id = type.GetProperty("Id")?.GetValue(power);
                object? title = type.GetProperty("Title")?.GetValue(power);
                object? amount = type.GetProperty("Amount")?.GetValue(power);
                object? powerType = type.GetProperty("TypeForCurrentAmount")?.GetValue(power)
                    ?? type.GetProperty("Type")?.GetValue(power);

                result.Add(new CombatPowerSnapshotPayload
                {
                    Index = index,
                    PowerId = id?.GetType().GetProperty("Entry")?.GetValue(id)?.ToString(),
                    Name = title?.GetType().GetMethod("GetFormattedText")?.Invoke(title, null)?.ToString(),
                    Amount = TryConvertInt(amount),
                    IsDebuff = string.Equals(powerType?.ToString(), "Debuff", StringComparison.Ordinal)
                });
                index++;
            }

            return result.ToArray();
        }
        catch
        {
            return Array.Empty<CombatPowerSnapshotPayload>();
        }
    }

    private static int[] GetValidTargetIndices(CombatState combatState, CardModel card)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => combatState.Enemies
                .Select((enemy, index) => new { enemy, index })
                .Where(entry => entry.enemy.IsAlive && entry.enemy.IsHittable)
                .Select(entry => entry.index)
                .ToArray(),
            TargetType.AnyAlly => combatState.Players
                .Select((player, index) => new { player, index })
                .Where(entry => entry.player.Creature.IsAlive && entry.player.NetId != card.Owner.NetId)
                .Select(entry => entry.index)
                .ToArray(),
            _ => Array.Empty<int>()
        };
    }

    private static bool RequiresTarget(TargetType targetType)
    {
        return targetType is TargetType.AnyEnemy or TargetType.AnyAlly;
    }

    private static bool IsSupportedTargetType(TargetType targetType)
    {
        return targetType is TargetType.None
            or TargetType.Self
            or TargetType.AnyEnemy
            or TargetType.AllEnemies
            or TargetType.RandomEnemy
            or TargetType.AnyAlly
            or TargetType.AllAllies;
    }

    private static string NormalizeUnplayableReason(UnplayableReason reason)
    {
        if (reason.HasFlag(UnplayableReason.EnergyCostTooHigh))
        {
            return "not_enough_energy";
        }

        if (reason.HasFlag(UnplayableReason.StarCostTooHigh))
        {
            return "not_enough_stars";
        }

        if (reason.HasFlag(UnplayableReason.NoLivingAllies))
        {
            return "no_living_allies";
        }

        if (reason.HasFlag(UnplayableReason.BlockedByHook))
        {
            return "blocked_by_hook";
        }

        if (reason.HasFlag(UnplayableReason.HasUnplayableKeyword)
            || reason.HasFlag(UnplayableReason.BlockedByCardLogic))
        {
            return "unplayable";
        }

        return reason.ToString();
    }

    private static string GetRulesText(CardModel card)
    {
        try
        {
            PileType pileType = card.Pile?.Type ?? PileType.None;
            string resolved = card.GetDescriptionForPile(pileType, card.CurrentTarget);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }
        catch
        {
            // Fall back to the raw localized description below.
        }

        return card.Description?.GetRawText() ?? string.Empty;
    }

    private static T? SafeRead<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default;
        }
    }

    private static T SafeRead<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private static T? SafeReadNullable<T>(Func<T> getter) where T : struct
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static int? TryConvertInt(object? value)
    {
        try
        {
            return value is null ? null : Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class CombatSnapshotPayload
{
    [JsonPropertyName("round")]
    public int Round { get; init; }

    [JsonPropertyName("current_side")]
    public required string CurrentSide { get; init; }

    [JsonPropertyName("is_player_turn")]
    public bool IsPlayerTurn { get; init; }

    [JsonPropertyName("player")]
    public required CombatPlayerSnapshotPayload Player { get; init; }

    [JsonPropertyName("hand")]
    public CombatHandCardSnapshotPayload[] Hand { get; init; } = [];

    [JsonPropertyName("enemies")]
    public CombatEnemySnapshotPayload[] Enemies { get; init; } = [];

    [JsonPropertyName("piles")]
    public CombatPileSnapshotPayload[] Piles { get; init; } = [];

    [JsonPropertyName("potions")]
    public CombatPotionSnapshotPayload[] Potions { get; init; } = [];

    [JsonPropertyName("relics")]
    public CombatRelicSnapshotPayload[] Relics { get; init; } = [];

    [JsonPropertyName("actions")]
    public CombatActionSnapshotPayload[] Actions { get; set; } = [];
}

internal sealed class CombatPlayerSnapshotPayload
{
    [JsonPropertyName("current_hp")]
    public int CurrentHp { get; init; }

    [JsonPropertyName("max_hp")]
    public int MaxHp { get; init; }

    [JsonPropertyName("block")]
    public int Block { get; init; }

    [JsonPropertyName("energy")]
    public int Energy { get; init; }

    [JsonPropertyName("max_energy")]
    public int MaxEnergy { get; init; }

    [JsonPropertyName("stars")]
    public int Stars { get; init; }

    [JsonPropertyName("powers")]
    public CombatPowerSnapshotPayload[] Powers { get; init; } = [];
}

internal sealed class CombatHandCardSnapshotPayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("card_id")]
    public string? CardId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("upgraded")]
    public bool? Upgraded { get; init; }

    [JsonPropertyName("energy_cost")]
    public int? EnergyCost { get; init; }

    [JsonPropertyName("costs_x")]
    public bool? CostsX { get; init; }

    [JsonPropertyName("enchantment")]
    public CardEnchantmentSnapshotPayload? Enchantment { get; init; }

    [JsonPropertyName("target_type")]
    public string? TargetType { get; init; }

    [JsonPropertyName("requires_target")]
    public bool? RequiresTarget { get; init; }

    [JsonPropertyName("valid_target_indices")]
    public int[] ValidTargetIndices { get; init; } = [];

    [JsonPropertyName("playable")]
    public bool Playable { get; init; }

    [JsonPropertyName("unplayable_reason")]
    public string? UnplayableReason { get; init; }

    [JsonPropertyName("rules_text")]
    public string? RulesText { get; init; }
}

internal sealed class CombatEnemySnapshotPayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("enemy_id")]
    public string? EnemyId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("current_hp")]
    public int? CurrentHp { get; init; }

    [JsonPropertyName("max_hp")]
    public int? MaxHp { get; init; }

    [JsonPropertyName("block")]
    public int? Block { get; init; }

    [JsonPropertyName("is_alive")]
    public bool? IsAlive { get; init; }

    [JsonPropertyName("is_hittable")]
    public bool? IsHittable { get; init; }

    [JsonPropertyName("powers")]
    public CombatPowerSnapshotPayload[] Powers { get; init; } = [];

    [JsonPropertyName("intents")]
    public CombatIntentSnapshotPayload[] Intents { get; init; } = [];
}

internal sealed class CombatPowerSnapshotPayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("power_id")]
    public string? PowerId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("amount")]
    public int? Amount { get; init; }

    [JsonPropertyName("is_debuff")]
    public bool IsDebuff { get; init; }
}

internal sealed class CombatIntentSnapshotPayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("intent_type")]
    public string? IntentType { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("damage")]
    public int? Damage { get; init; }

    [JsonPropertyName("hits")]
    public int? Hits { get; init; }

    [JsonPropertyName("total_damage")]
    public int? TotalDamage { get; init; }

    [JsonPropertyName("status_card_count")]
    public int? StatusCardCount { get; init; }
}

internal sealed class CombatPileSnapshotPayload
{
    [JsonPropertyName("pile_type")]
    public required string PileType { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("order_known")]
    public bool OrderKnown { get; init; }

    [JsonPropertyName("cards")]
    public CombatPileCardSnapshotPayload[] Cards { get; init; } = [];
}

internal sealed class CombatPileCardSnapshotPayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("card_id")]
    public string? CardId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("upgraded")]
    public bool? Upgraded { get; init; }

    [JsonPropertyName("energy_cost")]
    public int? EnergyCost { get; init; }

    [JsonPropertyName("costs_x")]
    public bool? CostsX { get; init; }

    [JsonPropertyName("enchantment")]
    public CardEnchantmentSnapshotPayload? Enchantment { get; init; }

    [JsonPropertyName("rules_text")]
    public string? RulesText { get; init; }
}

internal sealed class CombatPotionSnapshotPayload
{
    [JsonPropertyName("slot")]
    public int Slot { get; init; }

    [JsonPropertyName("is_empty")]
    public bool IsEmpty { get; init; }

    [JsonPropertyName("instance_id")]
    public string? InstanceId { get; init; }

    [JsonPropertyName("potion_id")]
    public string? PotionId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("rules_text")]
    public string? RulesText { get; init; }

    [JsonPropertyName("target_type")]
    public string? TargetType { get; init; }

    [JsonPropertyName("requires_target")]
    public bool RequiresTarget { get; init; }

    [JsonPropertyName("valid_target_indices")]
    public int[] ValidTargetIndices { get; init; } = [];

    [JsonPropertyName("usable")]
    public bool Usable { get; init; }
}

internal sealed class CombatRelicSnapshotPayload
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("relic_id")]
    public string? RelicId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("rules_text")]
    public string? RulesText { get; init; }

    [JsonPropertyName("counter")]
    public int? Counter { get; init; }
}

internal sealed class CombatActionSnapshotPayload
{
    [JsonPropertyName("action_id")]
    public required string ActionId { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("card_instance_id")]
    public string? CardInstanceId { get; init; }

    [JsonPropertyName("card_index")]
    public int? CardIndex { get; init; }

    [JsonPropertyName("potion_instance_id")]
    public string? PotionInstanceId { get; init; }

    [JsonPropertyName("potion_slot")]
    public int? PotionSlot { get; init; }

    [JsonPropertyName("target_instance_id")]
    public string? TargetInstanceId { get; init; }

    [JsonPropertyName("target_index")]
    public int? TargetIndex { get; init; }
}
