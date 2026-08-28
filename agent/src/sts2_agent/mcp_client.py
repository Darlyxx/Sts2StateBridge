from __future__ import annotations

import asyncio
import json
from pathlib import Path
from typing import Any
from uuid import uuid4

from langchain_mcp_adapters.client import MultiServerMCPClient
from langchain_core.messages import ToolMessage


class McpClientError(RuntimeError):
    pass


def adapter_config(mcp_directory: Path, bridge_url: str) -> dict[str, dict[str, Any]]:
    return {
        "sts2": {
            "transport": "stdio",
            "command": "uv",
            "args": ["--directory", str(mcp_directory), "run", "sts2-mcp"],
            "env": {"STS2_BRIDGE_URL": bridge_url},
        }
    }


class Sts2McpClient:
    def __init__(self, mcp_directory: Path, bridge_url: str) -> None:
        self.mcp_directory = mcp_directory
        self.bridge_url = bridge_url
        self.adapter = MultiServerMCPClient(adapter_config(mcp_directory, bridge_url))

    async def get_langchain_tools(self):
        try:
            return await self.adapter.get_tools()
        except Exception as exc:
            raise McpClientError(f"无法启动或连接 STS2 MCP Server：{exc}") from exc

    async def call(self, tool_name: str, arguments: dict | None = None) -> dict:
        try:
            tools = await self.get_langchain_tools()
            tool = next((item for item in tools if item.name == tool_name), None)
            if tool is None:
                raise McpClientError(f"MCP Server 未提供工具 {tool_name}。")
            result = await tool.ainvoke(
                {
                    "name": tool_name,
                    "args": arguments or {},
                    "id": f"sts2-agent-{uuid4().hex}",
                    "type": "tool_call",
                }
            )
        except Exception as exc:
            if isinstance(exc, McpClientError):
                raise
            raise McpClientError(f"调用 MCP 工具 {tool_name} 失败：{exc}") from exc
        return _structured_result(result, tool_name)

    def snapshot(self, *, full_state: bool = False) -> dict:
        async def read() -> dict:
            if full_state:
                return await self.call("get_full_snapshot")
            overview = await self.call("get_game_overview")
            if overview.get("phase") == "combat":
                return await self.call("get_combat_state")
            if overview.get("in_run"):
                return await self.call("get_interaction")
            return overview

        return asyncio.run(read())


def _structured_result(result: Any, tool_name: str) -> dict:
    value = result.artifact if isinstance(result, ToolMessage) else result
    if isinstance(value, dict) and "structured_content" in value:
        value = value["structured_content"]
    if isinstance(value, str):
        try:
            value = json.loads(value)
        except json.JSONDecodeError as exc:
            raise McpClientError(f"MCP 工具 {tool_name} 没有返回结构化 JSON。") from exc
    if isinstance(value, list):
        text = "".join(
            str(block.get("text", ""))
            for block in value
            if isinstance(block, dict) and block.get("type") == "text"
        )
        try:
            value = json.loads(text)
        except json.JSONDecodeError as exc:
            raise McpClientError(f"MCP 工具 {tool_name} 没有返回结构化 JSON。") from exc
    if not isinstance(value, dict):
        raise McpClientError(f"MCP 工具 {tool_name} 返回了无法识别的结果。")
    return value
