from __future__ import annotations

from typing import Any

from langchain_core.tools import tool

from .mcp_client import Sts2McpClient, McpClientError
from .skill_loader import SkillLibrary


class StrategyGuideSession:
    """Serve one relevant guide per scene while avoiding duplicate prompt payloads."""

    def __init__(self, library: SkillLibrary, mcp_client: Sts2McpClient) -> None:
        self.library = library
        self.mcp_client = mcp_client
        self._last_key: tuple | None = None

    def reset(self) -> None:
        self._last_key = None

    def read_current(self, topic: str = "auto") -> dict[str, Any]:
        snapshot = self.mcp_client.snapshot(full_state=True)
        guide = self.library.guide_for(snapshot, topic)
        # Different encounters in the same combat phase need the guide again.
        encounter = tuple(sorted(str(e.get("enemy_id")) for e in (snapshot.get("combat") or {}).get("enemies", []) or []))
        key = (guide.phase, guide.interaction_type, guide.topics, encounter,
               (snapshot.get("run") or {}).get("character_id"),
               (snapshot.get("run") or {}).get("floor"), (snapshot.get("run") or {}).get("act_number"))
        result = guide.as_dict()
        # This read can follow a state transition. Return its own candidates too,
        # so its newer state_id cannot accidentally be paired with an older view.
        result["snapshot"] = snapshot
        if key == self._last_key:
            result["guide"] = ""
            result["already_loaded"] = True
            return result
        self._last_key = key
        result["already_loaded"] = False
        return result


def create_strategy_guide_tool(session: StrategyGuideSession):
    @tool
    def get_current_strategy_guide(topic: str = "auto") -> dict[str, Any]:
        """读取当前场景战士策略；场景/遭遇改变后重读。topic 可选 auto、fundamentals（基础问题）、combat、drafting、routing、economy、encounters、failure-cases。"""
        try:
            return session.read_current(topic)
        except ValueError:
            return {"error": "未知策略主题，请使用工具说明中的主题名称。"}
        except McpClientError:
            return {"error": "无法读取当前游戏；检查 MCP/Bridge 或等待界面稳定后有限重试。"}

    return get_current_strategy_guide
