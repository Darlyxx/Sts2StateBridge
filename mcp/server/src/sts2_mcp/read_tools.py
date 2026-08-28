from __future__ import annotations

from typing import Any

from mcp.server import MCPServer
from mcp.server.mcpserver.exceptions import ToolError
from mcp.types import ToolAnnotations

from .bridge import BridgeClient, BridgeError
from .views import combat_state_view, full_snapshot_view, game_overview_view, interaction_view


READ_ONLY_ANNOTATIONS = ToolAnnotations(
    readOnlyHint=True,
    destructiveHint=False,
    idempotentHint=True,
    openWorldHint=False,
)


def _read_view(bridge: BridgeClient, view) -> dict[str, Any]:
    try:
        return view(bridge.get_snapshot())
    except BridgeError as exc:
        raise ToolError(str(exc)) from exc


def register_read_tools(server: MCPServer, bridge: BridgeClient) -> None:
    @server.tool(annotations=READ_ONLY_ANNOTATIONS, structured_output=True)
    def get_game_overview() -> dict[str, Any]:
        """读取最新游戏阶段、state_id、角色资源和整局构筑摘要。"""
        return _read_view(bridge, game_overview_view)

    @server.tool(annotations=READ_ONLY_ANNOTATIONS, structured_output=True)
    def get_combat_state() -> dict[str, Any]:
        """读取最新战斗状态，包括手牌、牌堆、敌人、意图、药水、遗物和合法候选。"""
        return _read_view(bridge, combat_state_view)

    @server.tool(annotations=READ_ONLY_ANNOTATIONS, structured_output=True)
    def get_interaction() -> dict[str, Any]:
        """读取最新地图、奖励、事件、商店、休息点或宝箱等非战斗交互。"""
        return _read_view(bridge, interaction_view)

    @server.tool(annotations=READ_ONLY_ANNOTATIONS, structured_output=True)
    def get_full_snapshot() -> dict[str, Any]:
        """读取完整原始游戏快照；仅在其他只读工具缺少必要字段时使用。"""
        return _read_view(bridge, full_snapshot_view)
