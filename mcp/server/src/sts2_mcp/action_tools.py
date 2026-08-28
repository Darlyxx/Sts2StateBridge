from __future__ import annotations

from typing import Any

from mcp.server import MCPServer
from mcp.server.mcpserver.exceptions import ToolError
from mcp.types import ToolAnnotations

from .bridge import BridgeClient, BridgeError


WRITE_ANNOTATIONS = ToolAnnotations(
    readOnlyHint=False,
    destructiveHint=True,
    idempotentHint=False,
    openWorldHint=False,
)


def register_action_tools(server: MCPServer, bridge: BridgeClient) -> None:
    @server.tool(annotations=WRITE_ANNOTATIONS, structured_output=True)
    def execute_action(state_id: str, action_id: str) -> dict[str, Any]:
        """执行当前快照列出的一个动作；必须使用同一最新快照中的 state_id 与 action_id。"""
        try:
            return bridge.execute_action(state_id, action_id)
        except BridgeError as exc:
            raise ToolError(str(exc)) from exc
