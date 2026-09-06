using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;

namespace Sts2StateBridge;

internal static class CombatSelectionSnapshotService
{
    private static readonly HashSet<string> SupportedScreens =
    [
        "NChooseACardSelectionScreen",
        "NChooseABundleSelectionScreen",
        "NCombatPileCardSelectScreen",
        "NSimpleCardSelectScreen",
        "NPlayerHand"
    ];

    public static CombatSelectionSnapshotPayload? Build(object? root)
    {
        object? selector = null;
        try
        {
            selector = CardSelectCmd.LocalSelector;
        }
        catch
        {
            // Older compatible builds may not expose a local selector.
        }

        object? screen = selector is not null && SupportedScreens.Contains(selector.GetType().Name)
            ? selector
            : Find(root);
        if (screen is null)
        {
            screen = Find((Engine.GetMainLoop() as SceneTree)?.Root);
        }
        if (screen is null) return null;

        try
        {
            return screen.GetType().Name switch
            {
                "NChooseACardSelectionScreen" => BuildChooseCard(screen),
                "NChooseABundleSelectionScreen" => BuildChooseBundle(screen),
                "NCombatPileCardSelectScreen" => BuildGrid(screen, "combat_pile"),
                "NSimpleCardSelectScreen" => BuildGrid(screen, "simple_card"),
                "NPlayerHand" => BuildHandSelection(screen),
                _ => null
            };
        }
        catch
        {
            return new CombatSelectionSnapshotPayload
            {
                SelectionType = "unknown",
                ScreenType = screen.GetType().Name,
                Ready = false,
                Reason = "selection_read_failed"
            };
        }
    }

    private static CombatSelectionSnapshotPayload BuildChooseCard(object screen)
    {
        CardModel[] cards = ReflectionRead.Items(ReflectionRead.Value(screen, "_cards", "Cards"))
            .OfType<CardModel>().ToArray();
        bool complete = ReflectionRead.Bool(screen, "_screenComplete") ?? false;
        bool selected = ReflectionRead.Bool(screen, "_cardSelected") ?? false;
        return new CombatSelectionSnapshotPayload
        {
            SelectionType = "choose_card",
            ScreenType = screen.GetType().Name,
            Ready = !complete && !selected && cards.Length > 0,
            MinSelect = 1,
            MaxSelect = 1,
            Cancelable = ReflectionRead.Bool(screen, "_canSkip") ?? false,
            RequiresConfirmation = false,
            Reason = complete || selected ? "selection_resolving" : cards.Length == 0 ? "no_candidates" : null,
            Candidates = cards.Select((card, index) => Candidate(card, index, false)).ToArray()
        };
    }

    private static CombatSelectionSnapshotPayload BuildChooseBundle(object screen)
    {
        object[] bundles = ReflectionRead.Items(ReflectionRead.Value(screen, "_bundles", "Bundles")).ToArray();
        object? selectedBundle = ReflectionRead.Value(screen, "_selectedBundle");
        return new CombatSelectionSnapshotPayload
        {
            SelectionType = "choose_bundle",
            ScreenType = screen.GetType().Name,
            Ready = bundles.Length > 0,
            MinSelect = 1,
            MaxSelect = 1,
            Cancelable = false,
            RequiresConfirmation = true,
            SelectedInstanceIds = selectedBundle is null
                ? []
                : [CombatSnapshotService.GetInstanceId(selectedBundle, "bundle:selected")],
            Candidates = bundles.Select((bundle, index) =>
            {
                CardModel[] cards = ReflectionRead.Items(ReflectionRead.Value(bundle, "Bundle"))
                    .OfType<CardModel>().ToArray();
                string id = CombatSnapshotService.GetInstanceId(bundle, $"bundle:{index}");
                return new CombatSelectionCandidateSnapshotPayload
                {
                    Index = index,
                    InstanceId = id,
                    Selected = ReferenceEquals(bundle, selectedBundle),
                    Enabled = true,
                    Cards = cards.Select(CombatSnapshotService.BuildPileCard).ToArray()
                };
            }).ToArray()
        };
    }

    private static CombatSelectionSnapshotPayload BuildGrid(object screen, string selectionType)
    {
        object? prefs = ReflectionRead.Value(screen, "_prefs", "Prefs");
        HashSet<CardModel> selected = ReflectionRead.Items(ReflectionRead.Value(screen, "_selectedCards"))
            .OfType<CardModel>().ToHashSet();
        CardModel[] cards = ReflectionRead.Items(ReflectionRead.Value(screen, "_cardResults"))
            .Select(item => item as CardModel ?? ReflectionRead.Value(item, "Card") as CardModel)
            .Where(card => card is not null).Cast<CardModel>().ToArray();
        if (cards.Length == 0)
        {
            cards = ReflectionRead.Items(ReflectionRead.Value(ReflectionRead.Value(screen, "_pile"), "Cards"))
                .OfType<CardModel>().ToArray();
        }

        int min = ReflectionRead.Int(prefs, "MinSelect") ?? 0;
        int max = ReflectionRead.Int(prefs, "MaxSelect") ?? cards.Length;
        bool needsConfirmation = ReflectionRead.Bool(prefs, "RequireManualConfirmation") ?? true;
        return new CombatSelectionSnapshotPayload
        {
            SelectionType = selectionType,
            ScreenType = screen.GetType().Name,
            SourcePile = ReflectionRead.Text(ReflectionRead.Value(screen, "_pile"), "Type"),
            Prompt = ReflectionRead.Localized(prefs, "Prompt"),
            Ready = cards.Length > 0 && selected.Count <= max,
            MinSelect = min,
            MaxSelect = max,
            Cancelable = ReflectionRead.Bool(prefs, "Cancelable") ?? false,
            RequiresConfirmation = needsConfirmation,
            SelectedInstanceIds = selected.Select((card, index) => CombatSnapshotService.GetInstanceId(card, $"selected:{index}"))
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Reason = cards.Length == 0 ? "no_candidates" : null,
            Candidates = cards.Select((card, index) => Candidate(card, index, selected.Contains(card))).ToArray()
        };
    }

    private static CombatSelectionSnapshotPayload BuildHandSelection(object hand)
    {
        object? prefs = ReflectionRead.Value(hand, "_prefs", "Prefs");
        HashSet<CardModel> selected = ReflectionRead.Items(ReflectionRead.Value(hand, "_selectedCards"))
            .OfType<CardModel>().ToHashSet();
        CardModel[] activeCards = ReflectionRead.Items(ReflectionRead.Value(hand, "ActiveHolders", "Holders"))
            .Select(holder => ReflectionRead.Value(holder, "CardModel", "Model") as CardModel)
            .Where(card => card is not null).Cast<CardModel>().ToArray();
        HashSet<CardModel> visibleCards = activeCards.Concat(selected).ToHashSet();
        CardModel? sample = activeCards.FirstOrDefault() ?? selected.FirstOrDefault();
        CardModel[] cards = sample?.Pile?.Cards.Where(visibleCards.Contains).ToArray()
            ?? activeCards.Concat(selected.Where(card => !activeCards.Contains(card))).ToArray();
        object? filterValue = ReflectionRead.Value(hand, "_currentSelectionFilter");
        Func<CardModel, bool>? filter = filterValue as Func<CardModel, bool>;
        int min = ReflectionRead.Int(prefs, "MinSelect") ?? 0;
        int max = ReflectionRead.Int(prefs, "MaxSelect") ?? cards.Length;
        return new CombatSelectionSnapshotPayload
        {
            SelectionType = "hand",
            ScreenType = hand.GetType().Name,
            SourcePile = "Hand",
            Prompt = ReflectionRead.Localized(prefs, "Prompt")
                ?? ReflectionRead.Text(ReflectionRead.Value(hand, "_selectionHeader"), "Text"),
            Ready = cards.Length > 0 && selected.Count <= max,
            MinSelect = min,
            MaxSelect = max,
            Cancelable = ReflectionRead.Bool(prefs, "Cancelable") ?? false,
            RequiresConfirmation = ReflectionRead.Bool(prefs, "RequireManualConfirmation") ?? true,
            SelectedInstanceIds = selected.Select((card, index) => CombatSnapshotService.GetInstanceId(card, $"selected:{index}"))
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Reason = cards.Length == 0 ? "no_candidates" : null,
            Candidates = cards.Select((card, index) =>
            {
                bool enabled = SafeFilter(filter, card);
                return Candidate(card, index, selected.Contains(card), enabled);
            }).ToArray()
        };
    }

    private static CombatSelectionCandidateSnapshotPayload Candidate(
        CardModel card,
        int index,
        bool selected,
        bool enabled = true)
    {
        return new CombatSelectionCandidateSnapshotPayload
        {
            Index = index,
            InstanceId = CombatSnapshotService.GetInstanceId(card, $"selection:{index}"),
            Selected = selected,
            Enabled = enabled,
            DisabledReason = enabled ? null : "filtered_out",
            Card = CombatSnapshotService.BuildPileCard(card, index)
        };
    }

    private static bool SafeFilter(Func<CardModel, bool>? filter, CardModel card)
    {
        try { return filter?.Invoke(card) ?? true; }
        catch { return false; }
    }

    private static object? Find(object? root)
    {
        if (root is null) return null;
        if (root.GetType().Name == "NPlayerHand")
        {
            if ((ReflectionRead.Bool(root, "IsInCardSelection") ?? false) && IsVisible(root)) return root;
        }
        else if (SupportedScreens.Contains(root.GetType().Name) && IsVisible(root))
        {
            return root;
        }
        if (root is not Node node) return null;
        foreach (Node child in node.GetChildren())
        {
            object? found = Find(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool IsVisible(object value)
    {
        try
        {
            return value is not CanvasItem canvasItem || canvasItem.IsVisibleInTree();
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class CombatSelectionSnapshotPayload
{
    [JsonPropertyName("selection_type")] public required string SelectionType { get; init; }
    [JsonPropertyName("screen_type")] public required string ScreenType { get; init; }
    [JsonPropertyName("ready")] public bool Ready { get; init; }
    [JsonPropertyName("prompt")] public string? Prompt { get; init; }
    [JsonPropertyName("source_pile")] public string? SourcePile { get; init; }
    [JsonPropertyName("min_select")] public int MinSelect { get; init; }
    [JsonPropertyName("max_select")] public int MaxSelect { get; init; }
    [JsonPropertyName("cancelable")] public bool Cancelable { get; init; }
    [JsonPropertyName("requires_confirmation")] public bool RequiresConfirmation { get; init; }
    [JsonPropertyName("selected_instance_ids")] public string[] SelectedInstanceIds { get; init; } = [];
    [JsonPropertyName("candidates")] public CombatSelectionCandidateSnapshotPayload[] Candidates { get; init; } = [];
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

internal sealed class CombatSelectionCandidateSnapshotPayload
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("instance_id")] public required string InstanceId { get; init; }
    [JsonPropertyName("selected")] public bool Selected { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("disabled_reason")] public string? DisabledReason { get; init; }
    [JsonPropertyName("card")] public CombatPileCardSnapshotPayload? Card { get; init; }
    [JsonPropertyName("cards")] public CombatPileCardSnapshotPayload[] Cards { get; init; } = [];
}
