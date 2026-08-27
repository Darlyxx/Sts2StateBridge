from __future__ import annotations

import json
from dataclasses import dataclass

from langchain_core.tools import BaseTool, tool

from .bridge import BridgeClient, BridgeError
from .views import combat_state_view, full_snapshot_view, game_overview_view, interaction_view


@dataclass(slots=True)
class ToolState:
    state_id: str | None = None
    phase: str = "unknown"


def build_read_tools(bridge: BridgeClient, state: ToolState) -> list[BaseTool]:
    def read() -> dict:
        snapshot = bridge.get_snapshot()
        state.state_id = snapshot.get("state_id")
        state.phase = snapshot.get("phase", "unknown")
        return snapshot

    def result_or_error(builder) -> str:
        try:
            return json.dumps(builder(read()), ensure_ascii=False, separators=(",", ":"))
        except BridgeError as exc:
            return json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False)

    @tool
    def get_game_overview() -> str:
        """读取最新游戏阶段、state_id、角色资源和整局构筑摘要。适合一般局势与构筑问题。"""
        return result_or_error(game_overview_view)

    @tool
    def get_combat_state() -> str:
        """读取最新战斗状态，包括玩家、手牌、牌堆、药水、遗物、敌人、意图与合法候选。只用于战斗问题。"""
        return result_or_error(combat_state_view)

    @tool
    def get_interaction() -> str:
        """读取最新非战斗交互，包括地图、奖励、事件、商店、休息点或宝箱的可见选项。"""
        return result_or_error(interaction_view)

    @tool
    def get_full_snapshot() -> str:
        """读取完整原始游戏快照。仅在其他只读工具确实缺少回答所需字段时使用。"""
        return result_or_error(full_snapshot_view)

    return [get_game_overview, get_combat_state, get_interaction, get_full_snapshot]
