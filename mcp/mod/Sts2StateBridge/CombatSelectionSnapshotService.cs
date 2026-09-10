using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;

namespace Sts2StateBridge;

internal static class CombatSelectionSnapshotService
{
    private static readonly HashSet<string> CombatScreens =
    [
        "NChooseACardSelectionScreen", "NChooseABundleSelectionScreen",
        "NCombatPileCardSelectScreen", "NSimpleCardSelectScreen", "NPlayerHand"
    ];

    public static CombatSelectionSnapshotPayload? Build(object? root)
    {
        object? screen = FindActive(root);
        if (screen is null) return null;
        try { return BuildForScreen(screen); }
        catch { return Failed(screen, "selection_read_failed"); }
    }

    public static CombatSelectionSnapshotPayload BuildDeckEnchant(object screen)
    {
        try { return BuildEnchant(screen); }
        catch { return Failed(screen, "selection_read_failed"); }
    }

    internal static object? FindActive(object? root, bool includeDeckEnchant = false)
    {
        object? selector = null;
        try { selector = CardSelectCmd.LocalSelector; } catch { }
        if (selector is not null && CombatScreens.Contains(selector.GetType().Name) && IsActive(selector)) return selector;
        return Find(root, includeDeckEnchant) ?? Find((Engine.GetMainLoop() as SceneTree)?.Root, includeDeckEnchant);
    }

    internal static SelectionActionDescriptor[] BuildActions(CombatSelectionSnapshotPayload? selection)
    {
        if (selection is null || !selection.Ready) return [];
        List<SelectionActionDescriptor> actions = [];
        foreach (CombatSelectionCandidateSnapshotPayload candidate in selection.Candidates)
        {
            if (candidate.Selected && selection.ConfirmationStage != "preview")
                actions.Add(new("selection:deselect:" + candidate.InstanceId, "selection_deselect", candidate.InstanceId));
            else if (candidate.Enabled && selection.SelectedCount < selection.MaxSelect)
                actions.Add(new("selection:select:" + candidate.InstanceId, "selection_select", candidate.InstanceId));
        }
        if (selection.ConfirmEnabled) actions.Add(new("selection:confirm", "selection_confirm", null));
        if (selection.CancelEnabled) actions.Add(new("selection:cancel", "selection_cancel", null));
        if (selection.Skippable) actions.Add(new("selection:skip", "selection_skip", null));
        return actions.ToArray();
    }

    internal static void Execute(object screen, string actionType, string? instanceId)
    {
        CombatSelectionSnapshotPayload snapshot = BuildForScreen(screen) ?? throw Changed("selection is no longer available");
        bool allowed = BuildActions(snapshot).Any(action => action.Type == actionType
            && string.Equals(action.CandidateInstanceId, instanceId, StringComparison.Ordinal));
        if (!allowed) throw Changed("selection action is no longer available");

        string type = screen.GetType().Name;
        if (actionType is "selection_select" or "selection_deselect")
        {
            object target = LocateCandidate(screen, instanceId!) ?? throw Changed("selection candidate is no longer available");
            bool select = actionType == "selection_select";
            if (type == "NChooseACardSelectionScreen") ReflectionRead.Invoke(screen, "SelectHolder", target);
            else if (type == "NChooseABundleSelectionScreen") ReflectionRead.Invoke(screen, "OnBundleClicked", target);
            else if (type == "NPlayerHand")
            {
                if (select)
                {
                    ReflectionRead.Invoke(screen, "OnHolderPressed", target);
                    if (!snapshot.RequiresConfirmation) ReflectionRead.Invoke(screen, "CheckIfSelectionComplete");
                }
                else
                {
                    object? cardNode = ReflectionRead.Invoke(screen, "GetCard", CardOf(target)!);
                    if (cardNode is null) throw Changed("selected hand card is unavailable");
                    ReflectionRead.Invoke(screen, "DeselectCard", cardNode);
                }
            }
            else ReflectionRead.Invoke(screen, "OnCardClicked", CardOf(target)!);
            return;
        }

        if (actionType == "selection_confirm") Confirm(screen, snapshot);
        else if (actionType == "selection_cancel") Cancel(screen, snapshot);
        else if (actionType == "selection_skip")
            ReflectionRead.Invoke(screen, "OnSkipButtonReleased", ReflectionRead.Value(screen, "_skipButton"));
        else throw Changed("unsupported selection action");
    }

    private static CombatSelectionSnapshotPayload? BuildForScreen(object screen) => screen.GetType().Name switch
    {
        "NChooseACardSelectionScreen" => BuildChooseCard(screen),
        "NChooseABundleSelectionScreen" => BuildChooseBundle(screen),
        "NCombatPileCardSelectScreen" => BuildGrid(screen, "combat_pile"),
        "NSimpleCardSelectScreen" => BuildGrid(screen, "simple_card"),
        "NPlayerHand" => BuildHand(screen),
        "NDeckEnchantSelectScreen" => BuildEnchant(screen),
        _ => null
    };

    private static CombatSelectionSnapshotPayload BuildChooseCard(object screen)
    {
        CardModel[] cards = Cards(ReflectionRead.Value(screen, "_cards", "Cards"));
        bool resolving = (ReflectionRead.Bool(screen, "_screenComplete") ?? false) || (ReflectionRead.Bool(screen, "_cardSelected") ?? false);
        bool skip = (ReflectionRead.Bool(screen, "_canSkip") ?? false)
            && InteractionSnapshotService.ControlEnabled(ReflectionRead.Value(screen, "_skipButton"));
        return Finish(new()
        {
            SelectionType = "choose_card", ScreenType = screen.GetType().Name,
            Ready = !resolving && cards.Length > 0, MinSelect = 1, MaxSelect = 1,
            Cancelable = skip, Skippable = skip, RequiresConfirmation = false,
            Reason = resolving ? "selection_resolving" : cards.Length == 0 ? "no_candidates" : null,
            Candidates = cards.Select((card, index) => Candidate(card, index, false)).ToArray()
        });
    }

    private static CombatSelectionSnapshotPayload BuildChooseBundle(object screen)
    {
        object? selected = ReflectionRead.Value(screen, "_selectedBundle");
        object[] nodes = Descendants(ReflectionRead.Value(screen, "_bundleRow") as Node)
            .Where(node => node.GetType().Name == "NCardBundle" && ReflectionRead.Value(node, "Bundle") is not null).Cast<object>().ToArray();
        bool preview = selected is not null;
        return Finish(new()
        {
            SelectionType = "choose_bundle", ScreenType = screen.GetType().Name, Ready = nodes.Length > 0,
            MinSelect = 1, MaxSelect = 1, Cancelable = preview,
            CancelEnabled = preview && InteractionSnapshotService.ControlEnabled(ReflectionRead.Value(screen, "_previewCancelButton")),
            RequiresConfirmation = true,
            ConfirmEnabled = preview && InteractionSnapshotService.ControlEnabled(ReflectionRead.Value(screen, "_previewConfirmButton")),
            ConfirmationStage = preview ? "preview" : "selecting",
            Candidates = nodes.Select((node, index) =>
            {
                bool chosen = ReferenceEquals(node, selected);
                return new CombatSelectionCandidateSnapshotPayload
                {
                    Index = index, InstanceId = CombatSnapshotService.GetInstanceId(node, $"bundle:{index}"),
                    Selected = chosen, Enabled = !preview, DisabledReason = preview && !chosen ? "preview_open" : null,
                    Cards = Cards(ReflectionRead.Value(node, "Bundle")).Select(CombatSnapshotService.BuildPileCard).ToArray()
                };
            }).ToArray()
        });
    }

    private static CombatSelectionSnapshotPayload BuildGrid(object screen, string kind)
    {
        object? prefs = ReflectionRead.Value(screen, "_prefs", "Prefs");
        HashSet<CardModel> selected = Cards(ReflectionRead.Value(screen, "_selectedCards")).ToHashSet();
        CardModel[] cards = ReflectionRead.Items(ReflectionRead.Value(screen, "_cardResults")).Select(CardOf)
            .Where(card => card is not null).Cast<CardModel>().ToArray();
        if (cards.Length == 0) cards = Cards(ReflectionRead.Value(ReflectionRead.Value(screen, "_pile"), "Cards"));
        int min = ReflectionRead.Int(prefs, "MinSelect") ?? 0;
        int max = ReflectionRead.Int(prefs, "MaxSelect") ?? cards.Length;
        bool manual = ReflectionRead.Bool(prefs, "RequireManualConfirmation") ?? true;
        bool cancelable = ReflectionRead.Bool(prefs, "Cancelable") ?? false;
        return Finish(new()
        {
            SelectionType = kind, ScreenType = screen.GetType().Name,
            SourcePile = ReflectionRead.Text(ReflectionRead.Value(screen, "_pile"), "Type"),
            Prompt = ReflectionRead.Localized(prefs, "Prompt"), Ready = cards.Length > 0 && selected.Count <= max,
            MinSelect = min, MaxSelect = max, Cancelable = cancelable, CancelEnabled = cancelable && selected.Count == 0,
            RequiresConfirmation = manual,
            ConfirmEnabled = manual && selected.Count >= min && selected.Count <= max
                && InteractionSnapshotService.ControlEnabled(ReflectionRead.Value(screen, "_confirmButton")),
            SelectedInstanceIds = Ids(selected), Reason = cards.Length == 0 ? "no_candidates" : null,
            Candidates = cards.Select((card, index) => Candidate(card, index, selected.Contains(card))).ToArray()
        });
    }

    private static CombatSelectionSnapshotPayload BuildHand(object hand)
    {
        object? prefs = ReflectionRead.Value(hand, "_prefs", "Prefs");
        HashSet<CardModel> selected = Cards(ReflectionRead.Value(hand, "_selectedCards")).ToHashSet();
        CardModel[] cards = ReflectionRead.Items(ReflectionRead.Value(hand, "ActiveHolders", "Holders"))
            .Select(CardOf).Where(card => card is not null).Cast<CardModel>().Concat(selected).Distinct().ToArray();
        Func<CardModel, bool>? filter = ReflectionRead.Value(hand, "_currentSelectionFilter") as Func<CardModel, bool>;
        int min = ReflectionRead.Int(prefs, "MinSelect") ?? 0;
        int max = ReflectionRead.Int(prefs, "MaxSelect") ?? cards.Length;
        bool manual = ReflectionRead.Bool(prefs, "RequireManualConfirmation") ?? true;
        bool cancelable = ReflectionRead.Bool(prefs, "Cancelable") ?? false;
        return Finish(new()
        {
            SelectionType = "hand", ScreenType = hand.GetType().Name, SourcePile = "Hand",
            Prompt = ReflectionRead.Localized(prefs, "Prompt") ?? ReflectionRead.Text(ReflectionRead.Value(hand, "_selectionHeader"), "Text"),
            Ready = cards.Length > 0 && selected.Count <= max, MinSelect = min, MaxSelect = max,
            Cancelable = cancelable, CancelEnabled = cancelable, RequiresConfirmation = manual,
            ConfirmEnabled = manual && selected.Count >= min && selected.Count <= max
                && InteractionSnapshotService.ControlEnabled(ReflectionRead.Value(hand, "_selectModeConfirmButton")),
            SelectedInstanceIds = Ids(selected), Reason = cards.Length == 0 ? "no_candidates" : null,
            Candidates = cards.Select((card, index) => Candidate(card, index, selected.Contains(card), SafeFilter(filter, card))).ToArray()
        });
    }

    private static CombatSelectionSnapshotPayload BuildEnchant(object screen)
    {
        object? prefs = ReflectionRead.Value(screen, "_prefs", "Prefs");
        HashSet<CardModel> selected = Cards(ReflectionRead.Value(screen, "_selectedCards")).ToHashSet();
        CardModel[] cards = Cards(ReflectionRead.Value(screen, "_cards", "Cards"));
        int min = ReflectionRead.Int(prefs, "MinSelect") ?? 1;
        int max = ReflectionRead.Int(prefs, "MaxSelect") ?? ((ReflectionRead.Bool(screen, "UseSingleSelection") ?? true) ? 1 : cards.Length);
        bool singlePreview = InteractionSnapshotService.ControlEnabled(ReflectionRead.Value(screen, "_singlePreviewConfirmButton"));
        bool multiPreview = InteractionSnapshotService.ControlEnabled(ReflectionRead.Value(screen, "_multiPreviewConfirmButton"));
        bool preview = singlePreview || multiPreview;
        object? confirm = preview
            ? ReflectionRead.Value(screen, singlePreview ? "_singlePreviewConfirmButton" : "_multiPreviewConfirmButton")
            : ReflectionRead.Value(screen, "_confirmButton");
        object? cancel = preview
            ? ReflectionRead.Value(screen, singlePreview ? "_singlePreviewCancelButton" : "_multiPreviewCancelButton")
            : ReflectionRead.Value(screen, "_closeButton");
        return Finish(new()
        {
            SelectionType = "deck_enchant", ScreenType = screen.GetType().Name,
            Prompt = ReflectionRead.Localized(prefs, "Prompt"), Ready = cards.Length > 0,
            MinSelect = min, MaxSelect = max, Cancelable = true,
            CancelEnabled = InteractionSnapshotService.ControlEnabled(cancel), RequiresConfirmation = true,
            ConfirmEnabled = InteractionSnapshotService.ControlEnabled(confirm) && (preview || selected.Count >= min),
            ConfirmationStage = preview ? "preview" : "selecting", SelectedInstanceIds = Ids(selected),
            Candidates = cards.Select((card, index) => Candidate(card, index, selected.Contains(card), !preview)).ToArray()
        });
    }

    private static CombatSelectionSnapshotPayload Finish(CombatSelectionSnapshotPayload value)
    {
        value.SelectedCount = value.SelectedInstanceIds.Length > 0
            ? value.SelectedInstanceIds.Length : value.Candidates.Count(candidate => candidate.Selected);
        return value;
    }

    private static CombatSelectionSnapshotPayload Failed(object screen, string reason) => new()
    { SelectionType = "unknown", ScreenType = screen.GetType().Name, Ready = false, Reason = reason };

    private static CombatSelectionCandidateSnapshotPayload Candidate(CardModel card, int index, bool selected, bool enabled = true) => new()
    {
        Index = index, InstanceId = CombatSnapshotService.GetInstanceId(card, $"selection:{index}"),
        Selected = selected, Enabled = enabled, DisabledReason = enabled ? null : "filtered_out",
        Card = CombatSnapshotService.BuildPileCard(card, index)
    };

    private static CardModel[] Cards(object? value) => ReflectionRead.Items(value).OfType<CardModel>().ToArray();
    private static CardModel? CardOf(object? value) => value as CardModel ?? ReflectionRead.Value(value, "CardModel", "Card", "Model") as CardModel;
    private static string[] Ids(IEnumerable<CardModel> cards) => cards.Select((card, index) => CombatSnapshotService.GetInstanceId(card, $"selected:{index}"))
        .OrderBy(id => id, StringComparer.Ordinal).ToArray();

    private static object? LocateCandidate(object screen, string id)
    {
        if (screen.GetType().Name == "NChooseABundleSelectionScreen")
            return Descendants(ReflectionRead.Value(screen, "_bundleRow") as Node).FirstOrDefault(node =>
                node.GetType().Name == "NCardBundle" && CombatSnapshotService.GetInstanceId(node, "bundle") == id);
        if (screen.GetType().Name == "NChooseACardSelectionScreen")
        {
            return Descendants(ReflectionRead.Value(screen, "_cardRow") as Node).FirstOrDefault(node =>
            {
                CardModel? card = CardOf(node);
                return card is not null && CombatSnapshotService.GetInstanceId(card, "candidate") == id;
            });
        }

        IEnumerable<object> values = screen.GetType().Name == "NPlayerHand"
            ? ReflectionRead.Items(ReflectionRead.Value(screen, "ActiveHolders", "Holders"))
            : ReflectionRead.Items(ReflectionRead.Value(screen, "_cardResults", "_cards", "Cards"));
        foreach (object value in values)
        {
            CardModel? card = CardOf(value);
            if (card is not null && CombatSnapshotService.GetInstanceId(card, "candidate") == id)
                return screen.GetType().Name == "NPlayerHand" ? value : card;
        }
        foreach (CardModel card in Cards(ReflectionRead.Value(ReflectionRead.Value(screen, "_pile"), "Cards")))
        {
            if (CombatSnapshotService.GetInstanceId(card, "pile_candidate") == id) return card;
        }
        if (screen.GetType().Name == "NPlayerHand")
        {
            return Cards(ReflectionRead.Value(screen, "_selectedCards")).FirstOrDefault(card =>
                CombatSnapshotService.GetInstanceId(card, "selected") == id);
        }
        return null;
    }

    private static void Confirm(object screen, CombatSelectionSnapshotPayload snapshot)
    {
        string type = screen.GetType().Name;
        if (type == "NChooseABundleSelectionScreen") ReflectionRead.Invoke(screen, "ConfirmSelection", ReflectionRead.Value(screen, "_previewConfirmButton"));
        else if (type == "NPlayerHand") ReflectionRead.Invoke(screen, "OnSelectModeConfirmButtonPressed", ReflectionRead.Value(screen, "_selectModeConfirmButton"));
        else if (type == "NDeckEnchantSelectScreen")
        {
            object? button = snapshot.ConfirmationStage == "preview"
                ? FirstEnabled(screen, "_singlePreviewConfirmButton", "_multiPreviewConfirmButton")
                : ReflectionRead.Value(screen, "_confirmButton");
            ReflectionRead.Invoke(screen, snapshot.ConfirmationStage == "preview" ? "ConfirmSelection" : "PreviewSelection", button);
        }
        else ReflectionRead.Invoke(screen, "CompleteSelection");
    }

    private static void Cancel(object screen, CombatSelectionSnapshotPayload snapshot)
    {
        string type = screen.GetType().Name;
        if (type == "NChooseABundleSelectionScreen") ReflectionRead.Invoke(screen, "CancelSelection", ReflectionRead.Value(screen, "_previewCancelButton"));
        else if (type == "NPlayerHand") ReflectionRead.Invoke(screen, "CancelHandSelectionIfNecessary");
        else if (type == "NDeckEnchantSelectScreen")
        {
            object? button = snapshot.ConfirmationStage == "preview"
                ? FirstEnabled(screen, "_singlePreviewCancelButton", "_multiPreviewCancelButton")
                : ReflectionRead.Value(screen, "_closeButton");
            ReflectionRead.Invoke(screen, snapshot.ConfirmationStage == "preview" ? "CancelSelection" : "CloseSelection", button);
        }
        else ReflectionRead.Invoke(screen, "CompleteSelection");
    }

    private static object? Find(object? root, bool enchant)
    {
        if (root is null) return null;
        string name = root.GetType().Name;
        if ((CombatScreens.Contains(name) || enchant && name == "NDeckEnchantSelectScreen") && IsActive(root)) return root;
        if (root is not Node node) return null;
        foreach (Node child in node.GetChildren()) { object? found = Find(child, enchant); if (found is not null) return found; }
        return null;
    }

    private static bool IsActive(object value)
    {
        if (value.GetType().Name == "NPlayerHand" && !(ReflectionRead.Bool(value, "IsInCardSelection") ?? false)) return false;
        try { return value is not CanvasItem canvas || canvas.IsVisibleInTree(); } catch { return false; }
    }

    private static IEnumerable<Node> Descendants(Node? root)
    {
        if (root is null) yield break;
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
    }

    private static bool SafeFilter(Func<CardModel, bool>? filter, CardModel card) { try { return filter?.Invoke(card) ?? true; } catch { return false; } }
    private static object? FirstEnabled(object value, params string[] names) => names
        .Select(name => ReflectionRead.Value(value, name)).FirstOrDefault(InteractionSnapshotService.ControlEnabled);
    private static ActionRequestException Changed(string message) => new(System.Net.HttpStatusCode.Conflict, "selection_changed", message);
}

internal sealed record SelectionActionDescriptor(string ActionId, string Type, string? CandidateInstanceId);

internal sealed class CombatSelectionSnapshotPayload
{
    [JsonPropertyName("selection_type")] public required string SelectionType { get; init; }
    [JsonPropertyName("screen_type")] public required string ScreenType { get; init; }
    [JsonPropertyName("ready")] public bool Ready { get; init; }
    [JsonPropertyName("prompt")] public string? Prompt { get; init; }
    [JsonPropertyName("source_pile")] public string? SourcePile { get; init; }
    [JsonPropertyName("min_select")] public int MinSelect { get; init; }
    [JsonPropertyName("max_select")] public int MaxSelect { get; init; }
    [JsonPropertyName("selected_count")] public int SelectedCount { get; set; }
    [JsonPropertyName("cancelable")] public bool Cancelable { get; init; }
    [JsonPropertyName("skippable")] public bool Skippable { get; init; }
    [JsonPropertyName("requires_confirmation")] public bool RequiresConfirmation { get; init; }
    [JsonPropertyName("confirm_enabled")] public bool ConfirmEnabled { get; init; }
    [JsonPropertyName("cancel_enabled")] public bool CancelEnabled { get; init; }
    [JsonPropertyName("confirmation_stage")] public string? ConfirmationStage { get; init; }
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
