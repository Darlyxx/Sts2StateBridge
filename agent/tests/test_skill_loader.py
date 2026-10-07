from pathlib import Path

import pytest

from sts2_agent.config import ConfigurationError, DEFAULT_SKILL_PATH
from sts2_agent.skill_loader import GUIDE_PATHS, SkillLibrary, load_skill_prompt


def _write_skill(root: Path, skill: str = "策略入口") -> None:
    (root / "references").mkdir(parents=True)
    (root / "SKILL.md").write_text(
        f"---\nname: custom\ndescription: test\n---\n\n{skill}\n",
        encoding="utf-8",
    )
    for topic, path in GUIDE_PATHS.items():
        (root / path).write_text(f"{topic}策略", encoding="utf-8")


def test_bundled_skill_injects_core_without_scene_guides_or_sources():
    prompt = load_skill_prompt(DEFAULT_SKILL_PATH)
    assert "STS2 Ironclad Player" in prompt
    assert "不可破坏的动作循环" in prompt
    assert "回合工作表" not in prompt
    assert "steamcommunity.com" not in prompt
    assert "name: sts2-ironclad-player" not in prompt


def test_custom_skill_path_loads_core_and_all_guides(tmp_path: Path):
    _write_skill(tmp_path, "自定义策略")
    library = SkillLibrary(tmp_path)
    assert library.core_prompt == "自定义策略"
    assert set(library.guides) == set(GUIDE_PATHS)


@pytest.mark.parametrize(
    ("snapshot", "expected"),
    [
        ({"phase": "combat", "combat": {}}, ("combat", "encounters", "failure-cases")),
        ({"phase": "run", "interaction": {"type": "card_reward"}}, ("drafting", "failure-cases")),
        ({"phase": "run", "interaction": {"type": "map"}}, ("routing", "failure-cases")),
        (
            {"phase": "run", "interaction": {"type": "shop"}},
            ("economy", "drafting", "failure-cases"),
        ),
    ],
)
def test_scene_routes_only_relevant_guides(tmp_path: Path, snapshot: dict, expected: tuple[str, ...]):
    _write_skill(tmp_path)
    guide = SkillLibrary(tmp_path).guide_for({"state_id": "s1", "run": {"character_id": "IRONCLAD"}, **snapshot})
    assert guide.topics == expected
    assert guide.state_id == "s1"
    for topic in expected:
        assert f"{topic}策略" in guide.content


@pytest.mark.parametrize("missing", ["SKILL.md", "references/combat.md"])
def test_missing_required_skill_file_has_clear_error(tmp_path: Path, missing: str):
    _write_skill(tmp_path)
    if missing != "SKILL.md":
        with (tmp_path / "SKILL.md").open("a", encoding="utf-8") as f:
            f.write("\n[combat](references/combat.md)\n")
    (tmp_path / missing).unlink()
    with pytest.raises(ConfigurationError, match="缺少必要文件"):
        SkillLibrary(tmp_path)


def test_document_only_custom_skill_and_role_guard(tmp_path):
    (tmp_path / "SKILL.md").write_text("# 文档模式", encoding="utf-8")
    library = SkillLibrary(tmp_path)
    assert library.guides == {}
    assert "自定义" in library.guide_for({"run": {"character_id": "IRONCLAD"}}).content
    assert library.guide_for({"run": {"character_id": "DEFECT"}}).topics == ()
    assert library.guide_for({}).topics == ()


def test_basic_topic_only_and_invalid_topic():
    library = SkillLibrary(DEFAULT_SKILL_PATH)
    snapshot = {"run": {"character_id": "IRONCLAD"}, "phase": "combat"}
    assert library.guide_for(snapshot, "fundamentals").topics == ("fundamentals",)
    with pytest.raises(ValueError):
        library.guide_for(snapshot, "../../secret")


def test_invalid_utf8_has_clear_error(tmp_path: Path):
    _write_skill(tmp_path)
    (tmp_path / "SKILL.md").write_bytes(b"\xff\xfe\x00")
    with pytest.raises(ConfigurationError, match="UTF-8"):
        SkillLibrary(tmp_path)
