from sts2_mcp.compact import compact_snapshot


def test_combat_keeps_decision_data_and_drops_unknown_fields():
    snapshot = {
        "schema_version": 1, "state_id": "abc", "phase": "combat", "in_run": True, "in_combat": True,
        "secret": "must not pass", "run": {"gold": 99, "deck": [{"card_id": "BASH"}], "unknown": 1},
        "combat": {"round": 2, "hand": [{"name": "痛击"}], "actions": [{"action_id": "play:1"}], "unknown": 2},
    }
    compact = compact_snapshot(snapshot)
    assert compact["combat"]["hand"][0]["name"] == "痛击"
    assert "secret" not in compact
    assert "unknown" not in compact["run"]
    assert "unknown" not in compact["combat"]


def test_map_only_keeps_current_and_reachable_nodes():
    snapshot = {
        "state_id": "map1", "phase": "run", "in_run": True, "in_combat": False, "run": {"gold": 10},
        "interaction": {"type": "map", "ready": True, "map": {
            "current_node_id": "map:1:1", "reachable_node_ids": ["map:2:1"],
            "nodes": [{"node_id": "map:1:1"}, {"node_id": "map:2:1"}, {"node_id": "map:9:9"}],
        }},
    }
    nodes = compact_snapshot(snapshot)["interaction"]["map"]["relevant_nodes"]
    assert [node["node_id"] for node in nodes] == ["map:1:1", "map:2:1"]


def test_interaction_keeps_only_current_action_candidates():
    snapshot = {
        "state_id": "reward-1", "phase": "run", "in_run": True, "in_combat": False,
        "interaction": {
            "type": "combat_reward", "ready": True,
            "options": [{"option_id": "reward:0:gold", "enabled": True}],
            "actions": [{
                "action_id": "interaction:claim_reward:reward:0:gold",
                "type": "claim_reward",
                "option_id": "reward:0:gold",
            }],
            "internal": "must not pass",
        },
    }
    interaction = compact_snapshot(snapshot)["interaction"]
    assert interaction["actions"][0]["type"] == "claim_reward"
    assert "internal" not in interaction


def test_map_event_and_shop_actions_survive_compaction():
    for interaction_type, action_type in [
        ("map", "travel_map"),
        ("event", "select_event_option"),
        ("shop", "buy_shop_item"),
    ]:
        snapshot = {
            "state_id": f"{interaction_type}-1", "phase": "run", "in_run": True,
            "interaction": {
                "type": interaction_type, "ready": True,
                "actions": [{"action_id": f"interaction:{action_type}:x", "type": action_type}],
            },
        }
        assert compact_snapshot(snapshot)["interaction"]["actions"][0]["type"] == action_type


def test_full_state_is_unchanged():
    snapshot = {"state_id": "x", "custom": {"value": None}}
    assert compact_snapshot(snapshot, full_state=True) is snapshot


def test_combat_keeps_composable_mechanics_and_star_costs():
    snapshot = {
        "state_id": "mechanics-1", "phase": "combat", "in_run": True, "in_combat": True,
        "combat": {
            "player": {"mechanics": [
                {"type": "stars", "current": 2},
                {"type": "osty", "current_hp": 9, "max_hp": 20},
                {"type": "orbs", "capacity": 3, "orbs": [{"orb_id": "LIGHTNING"}]},
            ]},
            "hand": [{"card_id": "STAR_CARD", "star_cost": 2, "costs_star_x": False}],
        },
    }
    combat = compact_snapshot(snapshot)["combat"]
    assert [item["type"] for item in combat["player"]["mechanics"]] == ["stars", "osty", "orbs"]
    assert combat["hand"][0]["star_cost"] == 2


def test_legacy_stars_are_normalized_to_mechanics_without_mutating_input():
    snapshot = {
        "phase": "combat", "in_combat": True,
        "combat": {"player": {"stars": 4}, "hand": []},
    }
    combat = compact_snapshot(snapshot)["combat"]
    assert combat["player"]["mechanics"] == [{"type": "stars", "current": 4}]
    assert "stars" not in combat["player"]
    assert snapshot["combat"]["player"]["stars"] == 4


def test_combat_keeps_readiness_selection_dynamic_values_and_derived_summary():
    snapshot = {
        "schema_version": 2,
        "bridge_version": "0.13.0",
        "state_id": "combat-read-1",
        "phase": "combat",
        "in_run": True,
        "in_combat": True,
        "combat": {
            "readiness": {
                "ready": False,
                "player_turn_phase": "Play",
                "input_locked": True,
                "selection_pending": True,
                "reason": "selection_pending",
            },
            "selection": {
                "selection_type": "choose_card",
                "screen_type": "NChooseACardSelectionScreen",
                "ready": True,
                "min_select": 1,
                "max_select": 1,
                "candidates": [{
                    "instance_id": "choice-1",
                    "enabled": True,
                    "card": {
                        "card_id": "BASH",
                        "card_type": "Attack",
                        "rarity": "Basic",
                        "keywords": ["Exhaust"],
                        "base_energy_cost": 2,
                        "energy_cost": 1,
                        "effective_damage": 10,
                        "dynamic_values": [{"name": "Damage", "value": 10}],
                    },
                }],
            },
            "derived": {
                "derived": True,
                "visible_incoming_attack_damage": 12,
                "estimated_unblocked_damage": 7,
            },
            "enemies": [{
                "instance_id": "enemy-1",
                "is_stunned": False,
                "side": "Enemy",
                "intents": [{
                    "intent_type": "Attack",
                    "total_damage": 12,
                    "target_instance_ids": ["player-1"],
                    "effects": [{"effect_type": "attack", "total": 12, "derived": True}],
                }],
            }],
            "actions": [{
                "action_id": "selection:select:choice-1",
                "type": "selection_select",
                "candidate_instance_id": "choice-1",
            }],
        },
    }

    combat = compact_snapshot(snapshot)["combat"]
    assert combat["selection"]["candidates"][0]["card"]["effective_damage"] == 10
    assert combat["readiness"]["selection_pending"] is True
    assert combat["derived"]["estimated_unblocked_damage"] == 7
    assert combat["enemies"][0]["intents"][0]["effects"][0]["effect_type"] == "attack"
    assert combat["actions"][0]["type"] == "selection_select"
    assert combat["actions"][0]["candidate_instance_id"] == "choice-1"


def test_interaction_keeps_deck_enchant_selection_and_actions():
    snapshot = {
        "schema_version": 2,
        "state_id": "enchant-1",
        "phase": "run",
        "in_run": True,
        "in_combat": False,
        "interaction": {
            "type": "card_selection",
            "ready": True,
            "selection": {
                "selection_type": "deck_enchant",
                "selected_count": 1,
                "confirmation_stage": "preview",
                "confirm_enabled": True,
                "candidates": [{"instance_id": "card-1", "selected": True}],
            },
            "actions": [{"action_id": "selection:confirm", "type": "selection_confirm"}],
        },
    }

    interaction = compact_snapshot(snapshot)["interaction"]
    assert interaction["selection"]["confirmation_stage"] == "preview"
    assert interaction["selection"]["selected_count"] == 1
    assert interaction["actions"][0]["action_id"] == "selection:confirm"
