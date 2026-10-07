from pathlib import Path

from sts2_agent.skill_loader import GUIDE_PATHS, SkillLibrary
from sts2_agent.strategy_tool import StrategyGuideSession, create_strategy_guide_tool


class FakeMcpClient:
    def __init__(self, snapshots: list[dict]) -> None:
        self.snapshots = iter(snapshots)

    def snapshot(self, *, full_state=False) -> dict:
        assert full_state is True
        return {"run": {"character_id": "IRONCLAD"}, **next(self.snapshots)}


def _library(tmp_path: Path) -> SkillLibrary:
    (tmp_path / "references").mkdir()
    (tmp_path / "SKILL.md").write_text(
        "---\nname: custom\ndescription: test\n---\n\ncore", encoding="utf-8"
    )
    for topic, path in GUIDE_PATHS.items():
        (tmp_path / path).write_text(f"guide:{topic}", encoding="utf-8")
    return SkillLibrary(tmp_path)


def test_strategy_tool_reads_current_scene_and_returns_state_id(tmp_path: Path):
    client = FakeMcpClient([{"state_id": "combat-1", "phase": "combat", "combat": {}}])
    session = StrategyGuideSession(_library(tmp_path), client)
    result = create_strategy_guide_tool(session).invoke({})
    assert result["state_id"] == "combat-1"
    assert result["phase"] == "combat"
    assert result["topics"] == ["combat", "encounters", "failure-cases"]
    assert "guide:combat" in result["guide"]
    assert result["already_loaded"] is False
    assert result["snapshot"]["state_id"] == result["state_id"]


def test_strategy_tool_suppresses_duplicate_scene_until_reset(tmp_path: Path):
    client = FakeMcpClient([
        {"state_id": "combat-1", "phase": "combat", "combat": {}},
        {"state_id": "combat-2", "phase": "combat", "combat": {}},
        {"state_id": "combat-3", "phase": "combat", "combat": {}},
    ])
    session = StrategyGuideSession(_library(tmp_path), client)
    first = session.read_current()
    duplicate = session.read_current()
    session.reset()
    after_reset = session.read_current()
    assert first["guide"]
    assert duplicate["guide"] == ""
    assert duplicate["already_loaded"] is True
    assert duplicate["snapshot"]["state_id"] == "combat-2"
    assert after_reset["guide"]


def test_strategy_tool_reloads_when_scene_changes(tmp_path: Path):
    client = FakeMcpClient([
        {"state_id": "combat-1", "phase": "combat", "combat": {}},
        {"state_id": "map-1", "phase": "run", "interaction": {"type": "map"}},
    ])
    session = StrategyGuideSession(_library(tmp_path), client)
    assert session.read_current()["topics"][0] == "combat"
    changed = session.read_current()
    assert changed["topics"][0] == "routing"
    assert changed["already_loaded"] is False


def test_new_encounter_same_phase_and_returning_scene_reload(tmp_path):
    client = FakeMcpClient([
        {"state_id":"a", "phase":"combat", "combat":{"enemies":[{"enemy_id":"ONE"}]}},
        {"state_id":"b", "phase":"combat", "combat":{"enemies":[{"enemy_id":"TWO"}]}},
        {"state_id":"c", "phase":"run", "interaction":{"type":"map"}},
        {"state_id":"d", "phase":"combat", "combat":{"enemies":[{"enemy_id":"ONE"}]}},
    ])
    session = StrategyGuideSession(_library(tmp_path), client)
    assert all(session.read_current()["already_loaded"] is False for _ in range(4))
