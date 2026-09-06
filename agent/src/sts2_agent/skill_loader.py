from __future__ import annotations

from pathlib import Path

from .config import ConfigurationError


PLAYBOOK_PATH = Path("references") / "ironclad-playbook.md"


def load_skill_prompt(skill_path: Path) -> str:
    """Load the strategy entrypoint and compact playbook for model injection."""
    root = skill_path.expanduser().resolve()
    entrypoint = root / "SKILL.md"
    playbook = root / PLAYBOOK_PATH
    missing = [path for path in (entrypoint, playbook) if not path.is_file()]
    if missing:
        names = ", ".join(str(path) for path in missing)
        raise ConfigurationError(f"战士 Skill 缺少必要文件：{names}")
    try:
        skill_text = _without_frontmatter(entrypoint.read_text(encoding="utf-8"))
        playbook_text = playbook.read_text(encoding="utf-8").strip()
    except UnicodeError as exc:
        raise ConfigurationError("战士 Skill 必须是有效的 UTF-8 文本。") from exc
    if not skill_text or not playbook_text:
        raise ConfigurationError("战士 Skill 内容为空，无法启动自主 Agent。")
    return (
        "<sts2_ironclad_skill>\n"
        f"{skill_text}\n\n{playbook_text}\n"
        "</sts2_ironclad_skill>"
    )


def _without_frontmatter(text: str) -> str:
    value = text.lstrip("\ufeff")
    if not value.startswith("---\n"):
        return value.strip()
    end = value.find("\n---\n", 4)
    return value[end + 5 :].strip() if end >= 0 else value.strip()
