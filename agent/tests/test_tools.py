import json

from sts2_agent.tools import ToolState, build_read_tools


class CountingBridge:
    def __init__(self):
        self.calls = 0

    def get_snapshot(self):
        self.calls += 1
        return {
            "schema_version": 1, "state_id": f"state-{self.calls}", "phase": "combat",
            "in_run": True, "in_combat": True,
            "run": {
                "character_id": "IRONCLAD", "current_hp": 40, "max_hp": 80, "gold": 99,
                "deck": [{"card_id": "STRIKE", "upgraded": False}, {"card_id": "STRIKE", "upgraded": False}],
                "relics": [{"relic_id": "BURNING_BLOOD"}], "potions": [], "hidden": "drop",
            },
            "combat": {"round": 2, "hand": [{"name": "痛击"}], "enemies": [{"name": "虱虫"}], "hidden": "drop"},
            "interaction": None, "raw_only": "visible only in full",
        }


def tool_map(bridge):
    state = ToolState()
    return {item.name: item for item in build_read_tools(bridge, state)}, state


def test_tools_are_zero_argument_and_refresh_each_call():
    bridge = CountingBridge()
    tools, state = tool_map(bridge)
    first = json.loads(tools["get_game_overview"].invoke({}))
    second = json.loads(tools["get_combat_state"].invoke({}))
    assert bridge.calls == 2
    assert first["state_id"] == "state-1"
    assert second["state_id"] == "state-2"
    assert state.state_id == "state-2"
    assert all(not item.args for item in tools.values())


def test_overview_summarizes_deck_and_combat_keeps_decision_fields():
    tools, _ = tool_map(CountingBridge())
    overview = json.loads(tools["get_game_overview"].invoke({}))
    assert overview["run"]["deck_summary"][0]["count"] == 2
    assert "hidden" not in overview["run"]
    combat = json.loads(tools["get_combat_state"].invoke({}))
    assert combat["combat"]["hand"][0]["name"] == "痛击"
    assert "hidden" not in combat["combat"]


def test_full_snapshot_is_explicit_escape_hatch():
    tools, _ = tool_map(CountingBridge())
    full = json.loads(tools["get_full_snapshot"].invoke({}))
    assert full["raw_only"] == "visible only in full"
