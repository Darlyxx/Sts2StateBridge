from pathlib import Path

import pytest

from sts2_agent.config import ConfigurationError, DEFAULT_SKILL_PATH
from sts2_agent.skill_loader import load_skill_prompt


def _write_skill(root: Path, skill: str = "策略入口", playbook: str = "精炼手册") -> None:
    (root / "references").mkdir(parents=True)
    (root / "SKILL.md").write_text(
        f"---\nname: custom\ndescription: test\n---\n\n{skill}\n",
        encoding="utf-8",
    )
    (root / "references" / "ironclad-playbook.md").write_text(playbook, encoding="utf-8")


def test_bundled_skill_merges_entrypoint_and_playbook_only():
    prompt = load_skill_prompt(DEFAULT_SKILL_PATH)
    assert "STS2 Ironclad Player" in prompt
    assert "协同包" in prompt
    assert "name: sts2-ironclad-player" not in prompt
    assert "steamcommunity.com" not in prompt


def test_custom_skill_path_is_loaded(tmp_path: Path):
    _write_skill(tmp_path, "自定义策略", "自定义手册")
    prompt = load_skill_prompt(tmp_path)
    assert "自定义策略" in prompt
    assert "自定义手册" in prompt


@pytest.mark.parametrize("missing", ["SKILL.md", "references/ironclad-playbook.md"])
def test_missing_required_skill_file_has_clear_error(tmp_path: Path, missing: str):
    _write_skill(tmp_path)
    (tmp_path / missing).unlink()
    with pytest.raises(ConfigurationError, match="缺少必要文件"):
        load_skill_prompt(tmp_path)


def test_invalid_utf8_has_clear_error(tmp_path: Path):
    _write_skill(tmp_path)
    (tmp_path / "SKILL.md").write_bytes(b"\xff\xfe\x00")
    with pytest.raises(ConfigurationError, match="UTF-8"):
        load_skill_prompt(tmp_path)
