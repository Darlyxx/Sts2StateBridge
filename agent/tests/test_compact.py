from sts2_agent.compact import compact_snapshot


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


def test_full_state_is_unchanged():
    snapshot = {"state_id": "x", "custom": {"value": None}}
    assert compact_snapshot(snapshot, full_state=True) is snapshot
