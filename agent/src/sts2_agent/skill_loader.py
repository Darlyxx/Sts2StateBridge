from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any
import re

from .config import ConfigurationError


GUIDE_PATHS = {
    "fundamentals": Path("references") / "fundamentals.md",
    "combat": Path("references") / "combat.md",
    "drafting": Path("references") / "drafting.md",
    "routing": Path("references") / "routing.md",
    "economy": Path("references") / "economy.md",
    "encounters": Path("references") / "encounters.md",
    "failure-cases": Path("references") / "failure-cases.md",
}


@dataclass(frozen=True, slots=True)
class StrategyGuide:
    state_id: str | None
    phase: str
    interaction_type: str
    topics: tuple[str, ...]
    content: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "state_id": self.state_id,
            "phase": self.phase,
            "interaction_type": self.interaction_type,
            "topics": list(self.topics),
            "guide": self.content,
        }


class SkillLibrary:
    """Validated, progressively disclosed strategy content for the bundled agent."""

    def __init__(self, skill_path: Path) -> None:
        self.root = skill_path.expanduser().resolve()
        entrypoint = self.root / "SKILL.md"
        required = [entrypoint]
        missing = [path for path in required if not path.is_file()]
        if missing:
            names = ", ".join(str(path) for path in missing)
            raise ConfigurationError(f"战士 Skill 缺少必要文件：{names}")
        try:
            self.core_prompt = _without_frontmatter(entrypoint.read_text(encoding="utf-8"))
            # Custom document-only skills are valid. Explicit local references must resolve.
            for relative in re.findall(r"\]\(([^)]+)\)", self.core_prompt):
                if "://" in relative or relative.startswith("#"):
                    continue
                target = (self.root / relative.split("#", 1)[0]).resolve()
                if not target.is_relative_to(self.root) or not target.is_file():
                    raise ConfigurationError(f"Skill 缺少必要文件或引用越界：{relative}")
            self.guides = {
                topic: (self.root / path).read_text(encoding="utf-8").strip()
                for topic, path in GUIDE_PATHS.items()
                if (self.root / path).is_file()
            }
        except UnicodeError as exc:
            raise ConfigurationError("战士 Skill 必须是有效的 UTF-8 文本。") from exc
        except OSError as exc:
            raise ConfigurationError("无法读取 Skill 文件，请检查路径与权限。") from exc
        if not self.core_prompt or any(not text for text in self.guides.values()):
            raise ConfigurationError("战士 Skill 内容为空，无法启动自主 Agent。")

    def guide_for(self, snapshot: dict[str, Any], topic: str = "auto") -> StrategyGuide:
        phase = str(snapshot.get("phase") or "unknown")
        interaction = snapshot.get("interaction")
        interaction_type = (
            str(interaction.get("type") or "none") if isinstance(interaction, dict) else "none"
        )
        if topic != "auto" and topic not in GUIDE_PATHS:
            raise ValueError("未知策略主题。")
        character = (snapshot.get("run") or {}).get("character_id")
        if character != "IRONCLAD":
            return StrategyGuide(snapshot.get("state_id"), phase, interaction_type, (),
                "角色尚未确认是 IRONCLAD：不应用战士专属策略；只解释已读取事实，提示专属支持范围。")
        requested = _topics_for(phase, interaction_type, snapshot) if topic == "auto" else (topic,)
        topics = tuple(t for t in requested if t in self.guides)
        content = "\n\n".join(self.guides[topic] for topic in topics)
        if not content:
            content = "自定义 Skill 未提供本场景的可选参考；使用已加载入口文档，不虚构缺失策略。"
        return StrategyGuide(
            state_id=snapshot.get("state_id"),
            phase=phase,
            interaction_type=interaction_type,
            topics=topics,
            content=content,
        )


def load_skill_prompt(skill_path: Path) -> str:
    """Load only the always-on core; scene guides are exposed dynamically."""
    skill = SkillLibrary(skill_path)
    return f"<sts2_ironclad_skill>\n{skill.core_prompt}\n</sts2_ironclad_skill>"


def _topics_for(
    phase: str,
    interaction_type: str,
    snapshot: dict[str, Any],
) -> tuple[str, ...]:
    if phase == "combat" or isinstance(snapshot.get("combat"), dict):
        return ("combat", "encounters", "failure-cases")
    if interaction_type in {"card_reward", "deck_enchant", "card_selection"}:
        return ("drafting", "failure-cases")
    if interaction_type == "map":
        return ("routing", "failure-cases")
    if interaction_type in {
        "combat_reward",
        "shop",
        "rest_site",
        "event",
        "treasure",
        "ancient",
        "boss_reward",
    }:
        return ("economy", "drafting", "failure-cases")
    return ("routing", "economy", "failure-cases")


def _without_frontmatter(text: str) -> str:
    value = text.lstrip("\ufeff")
    if not value.startswith("---\n"):
        return value.strip()
    end = value.find("\n---\n", 4)
    return value[end + 5 :].strip() if end >= 0 else value.strip()
