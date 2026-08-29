using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2StateBridge;

internal static class InteractionSnapshotService
{
    public static InteractionSnapshotPayload Build(object? currentScreen, RunState runState)
    {
        string screenType = currentScreen?.GetType().Name ?? "unknown";
        try
        {
            object? scene = FindRelevantNode(currentScreen);
            string typeName = scene?.GetType().Name ?? screenType;

            InteractionSnapshotPayload result = typeName switch
            {
                "NMapScreen" => BuildMap(scene!, screenType),
                "NRewardsScreen" => BuildRewards(scene!, screenType),
                "NCardRewardSelectionScreen" => BuildCardReward(scene!, screenType),
                "NEventRoom" => BuildEvent(scene!, screenType),
                "NMerchantInventory" => BuildShop(scene!, screenType, runState),
                "NMerchantRoom" => BuildMerchantRoom(scene!, screenType),
                "NRestSiteRoom" => BuildRestSite(scene!, screenType),
                "NDeckUpgradeSelectScreen" => BuildDeckUpgrade(scene!, screenType),
                "NTreasureRoom" or "NTreasureRoomRelicCollection" => BuildTreasure(scene!, screenType),
                _ => new InteractionSnapshotPayload
                {
                    Type = LooksTransitional(screenType) ? "transition" : "none",
                    Ready = false,
                    ScreenType = screenType
                }
            };
            result.Actions = BuildActions(result, runState);
            return result;
        }
        catch
        {
            return new InteractionSnapshotPayload
            {
                Type = "unknown",
                Ready = false,
                ScreenType = screenType
            };
        }
    }

    private static InteractionSnapshotPayload BuildMap(object screen, string screenType)
    {
        List<MapNodeSnapshotPayload> nodes = new();
        object? dictionary = ReflectionRead.Value(screen, "_mapPointDictionary", "MapPointDictionary");
        foreach (object entry in ReflectionRead.Items(dictionary))
        {
            object? node = ReflectionRead.Value(entry, "Value") ?? entry;
            object? point = ReflectionRead.Value(node, "Point");
            if (point is null) continue;
            object? coord = ReflectionRead.Value(point, "coord", "Coord");
            int? col = ReflectionRead.Int(coord, "col", "Col", "X");
            int? row = ReflectionRead.Int(coord, "row", "Row", "Y");
            string id = $"map:{row?.ToString() ?? "?"}:{col?.ToString() ?? "?"}";
            nodes.Add(new MapNodeSnapshotPayload
            {
                NodeId = id,
                Row = row,
                Column = col,
                NodeType = ReflectionRead.Text(point, "PointType"),
                State = ReflectionRead.Text(node, "State"),
                Reachable = ReflectionRead.Bool(node, "IsTravelable") ?? false,
                Children = ReflectionRead.Items(ReflectionRead.Value(point, "Children"))
                    .Select(child =>
                    {
                        object? childCoord = ReflectionRead.Value(child, "coord", "Coord");
                        return $"map:{ReflectionRead.Int(childCoord, "row", "Row", "Y")?.ToString() ?? "?"}:{ReflectionRead.Int(childCoord, "col", "Col", "X")?.ToString() ?? "?"}";
                    }).ToArray()
            });
        }

        MapNodeSnapshotPayload[] ordered = nodes.OrderBy(node => node.Row).ThenBy(node => node.Column).ToArray();
        return new InteractionSnapshotPayload
        {
            Type = "map",
            Ready = (ReflectionRead.Bool(screen, "IsOpen") ?? true)
                && (ReflectionRead.Bool(screen, "IsTravelEnabled") ?? false)
                && !(ReflectionRead.Bool(screen, "IsTraveling") ?? false),
            ScreenType = screenType,
            Map = new MapSnapshotPayload
            {
                Nodes = ordered,
                ReachableNodeIds = ordered.Where(node => node.Reachable).Select(node => node.NodeId).ToArray(),
                CurrentNodeId = ordered.FirstOrDefault(node => string.Equals(node.State, "Current", StringComparison.OrdinalIgnoreCase))?.NodeId
                    ?? ordered.Where(node => string.Equals(node.State, "Traveled", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(node => node.Row).FirstOrDefault()?.NodeId
            },
            Options = ordered.Where(node => node.Reachable).Select((node, index) => new InteractionOptionSnapshotPayload
            {
                OptionId = $"travel:{node.NodeId}",
                Index = index,
                Kind = "map_node",
                Label = node.NodeType,
                Enabled = true,
                TargetId = node.NodeId
            }).ToArray()
        };
    }

    private static InteractionSnapshotPayload BuildRewards(object screen, string screenType)
    {
        object? set = ReflectionRead.Value(screen, "_rewardsSet", "RewardsSet");
        object[] buttons = ReflectionRead.Items(ReflectionRead.Value(screen, "_rewardButtons", "RewardButtons"))
            .Where(button => button is not CanvasItem canvas || canvas.IsVisibleInTree())
            .ToArray();
        List<InteractionOptionSnapshotPayload> options = buttons
            .Select((button, index) => BuildRewardOption(
                ReflectionRead.Value(button, "Reward") ?? button,
                index,
                ControlEnabled(button)))
            .ToList();
        object? proceed = ReflectionRead.Value(screen, "_proceedButton", "ProceedButton");
        if (ControlEnabled(proceed))
        {
            options.Add(new InteractionOptionSnapshotPayload
            {
                OptionId = "rewards:proceed", Index = options.Count, Kind = "proceed",
                Label = (ReflectionRead.Bool(proceed, "IsSkip") ?? false) ? "skip" : "proceed",
                Enabled = true
            });
        }
        return new InteractionSnapshotPayload
        {
            Type = "combat_reward",
            Ready = set is not null && !(ReflectionRead.Bool(screen, "IsComplete") ?? false),
            ScreenType = screenType,
            Options = options.ToArray()
        };
    }

    private static InteractionOptionSnapshotPayload BuildRewardOption(object reward, int index, bool controlEnabled)
    {
        string kind = ReflectionRead.Text(reward, "RewardType")?.ToLowerInvariant() ?? reward.GetType().Name;
        object? model = ReflectionRead.Value(reward, "Relic", "Potion", "Card");
        return new InteractionOptionSnapshotPayload
        {
            OptionId = RewardOptionId(reward, index, kind),
            Index = index,
            Kind = kind,
            Label = ReflectionRead.Localized(reward, "Description"),
            Enabled = controlEnabled && !(ReflectionRead.Bool(reward, "SuccessfullySelected") ?? false),
            Amount = ReflectionRead.Int(reward, "Amount"),
            Item = BuildItem(model)
        };
    }

    internal static string RewardOptionId(object reward, int index, string? kind = null)
    {
        kind ??= ReflectionRead.Text(reward, "RewardType")?.ToLowerInvariant()
            ?? reward.GetType().Name.ToLowerInvariant();
        return $"reward:{ReflectionRead.Int(reward, "RewardsSetIndex") ?? index}:{kind}";
    }

    private static InteractionSnapshotPayload BuildCardReward(object screen, string screenType)
    {
        List<InteractionOptionSnapshotPayload> options = new();
        foreach (object option in ReflectionRead.Items(ReflectionRead.Value(screen, "_options", "Options")))
        {
            object? card = ExtractModel(option, "CardModel", "Card", "Model");
            options.Add(new InteractionOptionSnapshotPayload
            {
                OptionId = $"card_reward:{RunInventoryService.InstanceId(card ?? option, options.Count.ToString())}",
                Index = options.Count,
                Kind = "card",
                Label = ReflectionRead.Localized(card, "Title") ?? ReflectionRead.Text(card, "Title"),
                Enabled = true,
                Item = BuildItem(card)
            });
        }
        foreach (object option in ReflectionRead.Items(ReflectionRead.Value(screen, "_extraOptions", "ExtraOptions")))
        {
            string? optionId = ReflectionRead.Text(option, "OptionId");
            options.Add(new InteractionOptionSnapshotPayload
            {
                OptionId = $"card_reward:extra:{optionId ?? options.Count.ToString()}",
                Index = options.Count,
                Kind = "alternative",
                Label = ReflectionRead.Localized(option, "Title") ?? optionId,
                Enabled = true
            });
        }
        return new InteractionSnapshotPayload { Type = "card_reward", Ready = options.Count > 0, ScreenType = screenType, Options = options.ToArray() };
    }

    private static InteractionSnapshotPayload BuildEvent(object room, string screenType)
    {
        object? layout = ReflectionRead.Value(room, "Layout");
        InteractionOptionSnapshotPayload[] options = ReflectionRead.Items(ReflectionRead.Value(layout, "OptionButtons"))
            .Select((button, index) =>
            {
                object? option = ReflectionRead.Value(button, "Option");
                int actualIndex = ReflectionRead.Int(button, "Index") ?? index;
                return new InteractionOptionSnapshotPayload
                {
                    OptionId = $"event:{actualIndex}:{ReflectionRead.Text(option, "TextKey") ?? "option"}",
                    Index = actualIndex,
                    Kind = (ReflectionRead.Bool(option, "IsProceed") ?? false) ? "proceed" : "event_option",
                    Label = ReflectionRead.Localized(option, "Title"),
                    Description = ReflectionRead.Localized(option, "Description"),
                    Enabled = !(ReflectionRead.Bool(option, "IsLocked") ?? true),
                    Item = BuildItem(ReflectionRead.Value(option, "Relic"))
                };
            }).ToArray();
        object? eventModel = ReflectionRead.Value(layout, "_event", "Event") ?? ReflectionRead.Value(room, "_event", "Event");
        object? titleLabel = ReflectionRead.Value(layout, "_title", "TitleLabel");
        object? descriptionLabel = ReflectionRead.Value(layout, "_description", "DescriptionLabel");
        string? renderedTitle = ReflectionRead.InvokeText(titleLabel, "GetParsedText")
            ?? ReflectionRead.Text(titleLabel, "Text");
        string? renderedDescription = ReflectionRead.InvokeText(descriptionLabel, "GetParsedText")
            ?? ReflectionRead.Text(descriptionLabel, "Text");
        return new InteractionSnapshotPayload
        {
            Type = "event", Ready = options.Length > 0, ScreenType = screenType,
            Title = renderedTitle ?? ReflectionRead.Localized(eventModel, "Title"),
            Description = renderedDescription ?? ReflectionRead.Localized(eventModel, "Description"), Options = options
        };
    }

    private static InteractionSnapshotPayload BuildShop(object node, string screenType, RunState runState)
    {
        object? inventory = ReflectionRead.Value(node, "Inventory");
        InteractionOptionSnapshotPayload[] options = ReflectionRead.Items(ReflectionRead.Value(inventory, "AllEntries"))
            .Select((entry, index) =>
            {
                object? model = ExtractModel(entry, "Model", "CreationResult", "Card", "Potion", "Relic");
                string kind = entry.GetType().Name.Replace("Merchant", "").Replace("Entry", "").ToLowerInvariant();
                int? cost = ReflectionRead.Int(entry, "Cost");
                bool stocked = ReflectionRead.Bool(entry, "IsStocked") ?? true;
                InteractionItemSnapshotPayload? item = BuildItem(model);
                object? removalNode = kind == "cardremoval"
                    ? ReflectionRead.Value(node, "_cardRemovalNode", "CardRemovalNode")
                    : null;
                return new InteractionOptionSnapshotPayload
                {
                    OptionId = $"shop:{kind}:{RunInventoryService.InstanceId(model ?? entry, index.ToString())}",
                    Index = index, Kind = kind,
                    Label = ReflectionRead.Localized(model, "Title")
                        ?? ReflectionRead.Localized(removalNode, "Title")
                        ?? (kind == "cardremoval" ? "remove_card" : null),
                    Description = item?.Description
                        ?? ReflectionRead.Localized(removalNode, "Description")
                        ?? ReflectionRead.ModelText(model ?? entry),
                    Enabled = stocked, Price = cost,
                    Affordable = stocked && (ReflectionRead.Bool(entry, "EnoughGold") ?? false), Item = item
                };
            }).ToArray();
        return new InteractionSnapshotPayload
        {
            Type = "shop", Ready = (ReflectionRead.Bool(node, "IsOpen") ?? false), ScreenType = screenType, Options = options
        };
    }

    private static InteractionSnapshotPayload BuildMerchantRoom(object room, string screenType)
    {
        return new InteractionSnapshotPayload
        {
            Type = "shop", Ready = true, ScreenType = screenType,
            Options = [new InteractionOptionSnapshotPayload
            {
                OptionId = "shop:open", Index = 0, Kind = "open_shop",
                Label = "open_shop", Enabled = true
            }]
        };
    }

    private static InteractionSnapshotPayload BuildRestSite(object room, string screenType)
    {
        List<InteractionOptionSnapshotPayload> options = ReflectionRead.Items(ReflectionRead.Value(room, "Options"))
            .Select((entry, index) =>
            {
                object option = ReflectionRead.Value(entry, "Option") ?? entry;
                return new InteractionOptionSnapshotPayload
                {
                    OptionId = $"rest:{ReflectionRead.Text(option, "OptionId") ?? index.ToString()}",
                    Index = index, Kind = "rest_option", Label = ReflectionRead.Localized(option, "Title"),
                    Description = ReflectionRead.Localized(option, "Description"),
                    Enabled = ReflectionRead.Bool(option, "IsEnabled") ?? false
                };
            }).ToList();
        object? proceed = ReflectionRead.Value(room, "ProceedButton", "_proceedButton");
        if (options.Count == 0 && proceed is CanvasItem proceedCanvas && proceedCanvas.IsVisibleInTree())
        {
            options.Add(new InteractionOptionSnapshotPayload
            {
                OptionId = "rest:proceed", Index = 0, Kind = "proceed",
                Label = "proceed", Enabled = true
            });
        }
        return new InteractionSnapshotPayload { Type = "rest_site", Ready = options.Count > 0, ScreenType = screenType, Options = options.ToArray() };
    }

    private static InteractionSnapshotPayload BuildDeckUpgrade(object screen, string screenType)
    {
        InteractionOptionSnapshotPayload[] options = ReflectionRead.Items(ReflectionRead.Value(screen, "_cards", "Cards"))
            .OfType<CardModel>()
            .Select((card, index) => new InteractionOptionSnapshotPayload
            {
                OptionId = $"rest:smith:{RunInventoryService.InstanceId(card, index.ToString())}",
                Index = index,
                Kind = "upgrade_card",
                Label = card.Title,
                Description = SafeCardText(card),
                Enabled = !card.IsUpgraded,
                Item = BuildItem(card)
            }).ToArray();
        return new InteractionSnapshotPayload
        {
            Type = "rest_site", Ready = options.Length > 0,
            ScreenType = screenType, Title = "smith_card_selection", Options = options
        };
    }

    private static InteractionSnapshotPayload BuildTreasure(object collection, string screenType)
    {
        object? proceedButton = null;
        if (collection.GetType().Name == "NTreasureRoom")
        {
            proceedButton = ReflectionRead.Value(collection, "ProceedButton", "_proceedButton");
            bool opened = ReflectionRead.Bool(collection, "_hasChestBeenOpened") ?? false;
            if (!opened)
            {
                return new InteractionSnapshotPayload
                {
                    Type = "treasure", Ready = true, ScreenType = screenType,
                    Options = [new InteractionOptionSnapshotPayload
                    {
                        OptionId = "treasure:open_chest", Index = 0, Kind = "open_chest",
                        Label = "open_chest",
                        Enabled = ControlEnabled(ReflectionRead.Value(collection, "_chestButton"))
                    }]
                };
            }

            object? nestedCollection = ReflectionRead.Value(collection, "_relicCollection", "RelicCollection");
            if (nestedCollection is null)
            {
                return new InteractionSnapshotPayload { Type = "transition", Ready = false, ScreenType = screenType };
            }
            collection = nestedCollection;
        }

        object? holder = ReflectionRead.Value(collection, "SingleplayerRelicHolder");
        object? relicNode = ReflectionRead.Value(holder, "Relic");
        object? relic = ExtractModel(relicNode, "Model", "RelicModel", "Relic");
        bool empty = ReflectionRead.Bool(collection, "_isEmptyChest") ?? false;
        bool relicVisible = relicNode is not CanvasItem relicCanvas || relicCanvas.IsVisibleInTree();
        bool proceedVisible = ControlEnabled(proceedButton);
        InteractionOptionSnapshotPayload[] options = relic is null || !relicVisible
            ? proceedVisible
                ? [new InteractionOptionSnapshotPayload
                {
                    OptionId = "treasure:proceed", Index = 0, Kind = "proceed",
                    Label = "proceed", Enabled = true
                }]
                : []
            : [new InteractionOptionSnapshotPayload
        {
            OptionId = $"treasure:{RunInventoryService.InstanceId(relic, "relic")}", Index = 0,
            Kind = "relic", Label = ReflectionRead.Localized(relic, "Title"), Description = ReflectionRead.ModelText(relic),
            Enabled = true, Item = BuildItem(relic)
        }];
        return new InteractionSnapshotPayload { Type = "treasure", Ready = empty || options.Length > 0, ScreenType = screenType, Options = options };
    }

    internal static object? FindRelevantNode(object? root)
    {
        if (root is null) return null;
        string[] relevant = ["NMapScreen", "NRewardsScreen", "NCardRewardSelectionScreen", "NEventRoom", "NMerchantInventory", "NMerchantRoom", "NRestSiteRoom", "NDeckUpgradeSelectScreen", "NTreasureRoom", "NTreasureRoomRelicCollection"];
        if (relevant.Contains(root.GetType().Name) && IsActiveCandidate(root)) return root;
        if (root is not Node node) return null;
        foreach (Node child in node.GetChildren())
        {
            object? found = FindRelevantNode(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool IsActiveCandidate(object value)
    {
        string typeName = value.GetType().Name;
        if (typeName == "NMapScreen") return ReflectionRead.Bool(value, "IsOpen") ?? false;
        if (typeName == "NMerchantInventory") return ReflectionRead.Bool(value, "IsOpen") ?? false;
        if (value is CanvasItem canvas) return canvas.IsVisibleInTree();
        if (value is Node node) return node.IsInsideTree() && node.ProcessMode != Node.ProcessModeEnum.Disabled;
        return true;
    }

    internal static object? ExtractModel(object? value, params string[] names)
    {
        if (value is null) return null;
        if (ReflectionRead.Entry(value, "Id") is not null) return value;
        foreach (string name in names)
        {
            object? nested = ReflectionRead.Value(value, name);
            if (nested is null) continue;
            if (ReflectionRead.Entry(nested, "Id") is not null) return nested;
            object? deeper = ReflectionRead.Value(nested, "Card", "Model");
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private static InteractionItemSnapshotPayload? BuildItem(object? model)
    {
        if (model is null) return null;
        string? description = model is CardModel card
            ? SafeCardText(card)
            : ReflectionRead.ModelText(model);
        return new InteractionItemSnapshotPayload
        {
            ItemId = ReflectionRead.Entry(model, "Id", "ModelId"),
            Name = ReflectionRead.Localized(model, "Title") ?? ReflectionRead.Text(model, "Title", "Name"),
            Description = description,
            Upgraded = ReflectionRead.Bool(model, "IsUpgraded")
        };
    }

    private static string? SafeCardText(CardModel card)
    {
        try
        {
            return card.GetDescriptionForPile(card.Pile?.Type ?? PileType.None, card.CurrentTarget);
        }
        catch
        {
            return ReflectionRead.ModelText(card);
        }
    }

    private static bool LooksTransitional(string screenType) => screenType is "NRun" or "NCombatRoom" or "unknown";

    internal static bool ControlEnabled(object? control)
    {
        if (control is null) return false;
        if (control is CanvasItem canvas && !canvas.IsVisibleInTree()) return false;
        return ReflectionRead.Bool(control, "IsEnabled") ?? true;
    }

    private static InteractionActionSnapshotPayload[] BuildActions(
        InteractionSnapshotPayload interaction,
        RunState runState)
    {
        if (!interaction.Ready)
        {
            return [];
        }

        List<InteractionActionSnapshotPayload> actions = new();
        InteractionOptionSnapshotPayload[] enabled = interaction.Options
            .Where(option => option.Enabled)
            .ToArray();

        if (interaction.Type == "combat_reward")
        {
            Player? player = LocalContext.GetMe((IPlayerCollection)runState);
            foreach (InteractionOptionSnapshotPayload option in enabled)
            {
                if (option.Kind == "proceed")
                {
                    actions.Add(ForOption("proceed", option));
                    continue;
                }

                if (option.Kind == "potion" && player is not null && !player.HasOpenPotionSlots)
                {
                    foreach ((var potion, int slot) in player.PotionSlots.Select((potion, slot) => (potion, slot)))
                    {
                        if (potion is null) continue;
                        string instanceId = RunInventoryService.InstanceId(potion, slot.ToString());
                        actions.Add(new InteractionActionSnapshotPayload
                        {
                            ActionId = $"interaction:discard_potion:{instanceId}",
                            Type = "discard_potion",
                            OptionId = option.OptionId,
                            Label = $"discard {ReflectionRead.Localized(potion, "Title") ?? potion.Id.Entry}",
                            PotionSlot = slot,
                            PotionInstanceId = instanceId
                        });
                    }
                    continue;
                }

                actions.Add(ForOption("claim_reward", option));
            }
        }
        else if (interaction.Type == "card_reward")
        {
            actions.AddRange(enabled.Select(option => ForOption(
                option.Kind == "card" ? "select_card" : "select_alternative",
                option)));
        }
        else if (interaction.Type == "rest_site")
        {
            actions.AddRange(enabled.Select(option => ForOption(
                option.Kind switch
                {
                    "upgrade_card" => "upgrade_card",
                    "proceed" => "proceed",
                    _ => "select_rest_option"
                },
                option)));
        }
        else if (interaction.Type == "treasure")
        {
            actions.AddRange(enabled.Select(option => ForOption(
                option.Kind switch
                {
                    "open_chest" => "open_chest",
                    "relic" => "claim_relic",
                    _ => "proceed"
                },
                option)));
        }

        return actions.ToArray();
    }

    private static InteractionActionSnapshotPayload ForOption(
        string type,
        InteractionOptionSnapshotPayload option)
    {
        return new InteractionActionSnapshotPayload
        {
            ActionId = $"interaction:{type}:{option.OptionId}",
            Type = type,
            OptionId = option.OptionId,
            Label = option.Label,
            TargetId = option.TargetId ?? option.Item?.ItemId
        };
    }
}

internal sealed class InteractionSnapshotPayload
{
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("ready")] public bool Ready { get; init; }
    [JsonPropertyName("screen_type")] public required string ScreenType { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("options")] public InteractionOptionSnapshotPayload[] Options { get; init; } = [];
    [JsonPropertyName("actions")] public InteractionActionSnapshotPayload[] Actions { get; set; } = [];
    [JsonPropertyName("map")] public MapSnapshotPayload? Map { get; init; }
}

internal sealed class InteractionActionSnapshotPayload
{
    [JsonPropertyName("action_id")] public required string ActionId { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("option_id")] public string? OptionId { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("target_id")] public string? TargetId { get; init; }
    [JsonPropertyName("potion_slot")] public int? PotionSlot { get; init; }
    [JsonPropertyName("potion_instance_id")] public string? PotionInstanceId { get; init; }
}

internal sealed class InteractionOptionSnapshotPayload
{
    [JsonPropertyName("option_id")] public required string OptionId { get; init; }
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("price")] public int? Price { get; init; }
    [JsonPropertyName("affordable")] public bool? Affordable { get; init; }
    [JsonPropertyName("amount")] public int? Amount { get; init; }
    [JsonPropertyName("target_id")] public string? TargetId { get; init; }
    [JsonPropertyName("item")] public InteractionItemSnapshotPayload? Item { get; init; }
}

internal sealed class InteractionItemSnapshotPayload
{
    [JsonPropertyName("item_id")] public string? ItemId { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("upgraded")] public bool? Upgraded { get; init; }
}

internal sealed class MapSnapshotPayload
{
    [JsonPropertyName("current_node_id")] public string? CurrentNodeId { get; init; }
    [JsonPropertyName("reachable_node_ids")] public string[] ReachableNodeIds { get; init; } = [];
    [JsonPropertyName("nodes")] public MapNodeSnapshotPayload[] Nodes { get; init; } = [];
}

internal sealed class MapNodeSnapshotPayload
{
    [JsonPropertyName("node_id")] public required string NodeId { get; init; }
    [JsonPropertyName("row")] public int? Row { get; init; }
    [JsonPropertyName("column")] public int? Column { get; init; }
    [JsonPropertyName("node_type")] public string? NodeType { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("reachable")] public bool Reachable { get; init; }
    [JsonPropertyName("children")] public string[] Children { get; init; } = [];
}
