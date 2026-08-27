using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2StateBridge;

internal static class RunInventoryService
{
    public static int? ReadAscension(RunState runState, Player player)
    {
        return ReflectionRead.Int(runState, "AscensionLevel", "Ascension")
            ?? ReflectionRead.Int(player, "AscensionLevel", "Ascension");
    }

    public static RunCardSnapshotPayload[] BuildDeck(Player player)
    {
        try
        {
            return player.Deck.Cards.Select((card, index) => new RunCardSnapshotPayload
            {
                Index = index,
                InstanceId = InstanceId(card, $"deck:{index}"),
                CardId = card.Id.Entry,
                Name = card.Title,
                Upgraded = card.IsUpgraded,
                EnergyCost = card.EnergyCost.GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.All),
                CostsX = card.EnergyCost.CostsX,
                Enchantment = BuildEnchantment(card),
                RulesText = Safe(() => card.GetDescriptionForPile(
                    card.Pile?.Type ?? MegaCrit.Sts2.Core.Entities.Cards.PileType.None,
                    card.CurrentTarget))
            }).ToArray();
        }
        catch { return []; }
    }

    internal static CardEnchantmentSnapshotPayload? BuildEnchantment(CardModel card)
    {
        object? enchantment = ReflectionRead.Value(card, "Enchantment");
        if (enchantment is null) return null;
        return new CardEnchantmentSnapshotPayload
        {
            EnchantmentId = ReflectionRead.Entry(enchantment, "Id", "ModelId"),
            Name = ReflectionRead.Localized(enchantment, "Title"),
            RulesText = ReflectionRead.ModelText(enchantment)
        };
    }

    public static RunRelicSnapshotPayload[] BuildRelics(Player player)
    {
        try
        {
            return player.Relics.Select((relic, index) => new RunRelicSnapshotPayload
            {
                Index = index,
                InstanceId = InstanceId(relic, $"relic:{index}"),
                RelicId = relic.Id.Entry,
                Name = ReflectionRead.Localized(relic, "Title"),
                RulesText = ReflectionRead.ModelText(relic),
                Counter = Safe(() => relic.ShowCounter ? (int?)relic.DisplayAmount : null),
                Status = ReflectionRead.Text(relic, "Status")
            }).ToArray();
        }
        catch { return []; }
    }

    public static RunPotionSnapshotPayload[] BuildPotions(Player player)
    {
        try
        {
            return player.PotionSlots.Select((potion, slot) => potion is null
                ? new RunPotionSnapshotPayload { Slot = slot, IsEmpty = true }
                : new RunPotionSnapshotPayload
                {
                    Slot = slot,
                    IsEmpty = false,
                    InstanceId = InstanceId(potion, $"potion:{slot}"),
                    PotionId = potion.Id.Entry,
                    Name = ReflectionRead.Localized(potion, "Title"),
                    RulesText = ReflectionRead.ModelText(potion),
                    TargetType = ReflectionRead.Text(potion, "TargetType")
                }).ToArray();
        }
        catch { return []; }
    }

    internal static string InstanceId(object value, string fallback)
    {
        object? netId = ReflectionRead.Value(value, "NetId");
        if (netId is not null) return netId.ToString() ?? fallback;
        string model = ReflectionRead.Entry(value, "Id", "ModelId") ?? value.GetType().Name;
        return $"{model}:{RuntimeHelpers.GetHashCode(value):x8}";
    }

    private static T? Safe<T>(Func<T> getter)
    {
        try { return getter(); } catch { return default; }
    }
}

internal sealed class RunCardSnapshotPayload
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("instance_id")] public required string InstanceId { get; init; }
    [JsonPropertyName("card_id")] public string? CardId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("upgraded")] public bool? Upgraded { get; init; }
    [JsonPropertyName("energy_cost")] public int? EnergyCost { get; init; }
    [JsonPropertyName("costs_x")] public bool? CostsX { get; init; }
    [JsonPropertyName("enchantment")] public CardEnchantmentSnapshotPayload? Enchantment { get; init; }
    [JsonPropertyName("rules_text")] public string? RulesText { get; init; }
}

internal sealed class CardEnchantmentSnapshotPayload
{
    [JsonPropertyName("enchantment_id")] public string? EnchantmentId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("rules_text")] public string? RulesText { get; init; }
}

internal sealed class RunRelicSnapshotPayload
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("instance_id")] public required string InstanceId { get; init; }
    [JsonPropertyName("relic_id")] public string? RelicId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("rules_text")] public string? RulesText { get; init; }
    [JsonPropertyName("counter")] public int? Counter { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
}

internal sealed class RunPotionSnapshotPayload
{
    [JsonPropertyName("slot")] public int Slot { get; init; }
    [JsonPropertyName("is_empty")] public bool IsEmpty { get; init; }
    [JsonPropertyName("instance_id")] public string? InstanceId { get; init; }
    [JsonPropertyName("potion_id")] public string? PotionId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("rules_text")] public string? RulesText { get; init; }
    [JsonPropertyName("target_type")] public string? TargetType { get; init; }
}

internal static class ReflectionRead
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static object? Value(object? value, params string[] names)
    {
        if (value is null) return null;
        foreach (string name in names)
        {
            try
            {
                PropertyInfo? property = value.GetType().GetProperties(Flags)
                    .FirstOrDefault(candidate => candidate.Name == name
                        && candidate.GetIndexParameters().Length == 0);
                if (property is not null) return property.GetValue(value);
                FieldInfo? field = value.GetType().GetField(name, Flags)
                    ?? value.GetType().GetField("_" + char.ToLowerInvariant(name[0]) + name[1..], Flags);
                if (field is not null) return field.GetValue(value);
            }
            catch { }
        }
        return null;
    }

    public static IEnumerable<object> Items(object? value)
    {
        if (value is System.Collections.IEnumerable items && value is not string)
            foreach (object? item in items) if (item is not null) yield return item;
    }

    public static string? Text(object? value, params string[] names) => Value(value, names)?.ToString();

    public static int? Int(object? value, params string[] names)
    {
        try { object? result = Value(value, names); return result is null ? null : Convert.ToInt32(result); }
        catch { return null; }
    }

    public static bool? Bool(object? value, params string[] names)
    {
        try { object? result = Value(value, names); return result is null ? null : Convert.ToBoolean(result); }
        catch { return null; }
    }

    public static string? Entry(object? value, params string[] names)
    {
        object? nested = Value(value, names);
        return Value(nested, "Entry")?.ToString();
    }

    public static string? Localized(object? value, params string[] names)
    {
        object? localized = Value(value, names);
        if (localized is null) return null;
        try
        {
            MethodInfo? formatter = localized.GetType().GetMethods(Flags)
                .FirstOrDefault(method => method.Name == "GetFormattedText"
                    && method.GetParameters().Length == 0);
            return formatter?.Invoke(localized, null)?.ToString() ?? localized.ToString();
        }
        catch { return localized.ToString(); }
    }

    public static string? ModelText(object value)
    {
        foreach (string name in new[]
                 {
                     "DynamicDescription", "Description", "DescriptionLocString",
                     "EventDescription", "DynamicEventDescription", "RulesText", "Tooltip"
                 })
        {
            string? result = Localized(value, name);
            if (!string.IsNullOrWhiteSpace(result)) return result;
        }
        return null;
    }

    public static string? InvokeText(object? value, params string[] methodNames)
    {
        if (value is null) return null;
        foreach (string methodName in methodNames)
        {
            try
            {
                MethodInfo? method = value.GetType().GetMethods(Flags)
                    .FirstOrDefault(candidate => candidate.Name == methodName
                        && candidate.GetParameters().Length == 0);
                string? result = method?.Invoke(value, null)?.ToString();
                if (!string.IsNullOrWhiteSpace(result)) return result;
            }
            catch { }
        }
        return null;
    }
}
