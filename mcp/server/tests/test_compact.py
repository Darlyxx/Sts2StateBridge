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
