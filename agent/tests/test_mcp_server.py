from pathlib import Path

import pytest
from mcp import Client, StdioServerParameters

from sts2_agent.bridge import BridgeUnavailableError
from sts2_agent.mcp_server import create_mcp_server


@pytest.fixture
def anyio_backend():
    return "asyncio"


class CountingBridge:
    def __init__(self):
        self.calls = 0

    def get_snapshot(self):
        self.calls += 1
        return {
            "schema_version": 1,
            "bridge_version": "0.7.0",
            "state_id": f"mcp-{self.calls}",
            "phase": "combat",
            "in_run": True,
            "in_combat": True,
            "run": {"character_id": "IRONCLAD", "current_hp": 40, "max_hp": 80, "deck": []},
            "combat": {"round": 1, "hand": [{"name": "痛击"}], "enemies": []},
            "interaction": None,
        }


class RecoveringBridge(CountingBridge):
    def get_snapshot(self):
        if self.calls == 0:
            self.calls += 1
            raise BridgeUnavailableError("无法连接本地游戏桥接器。")
        return super().get_snapshot()


@pytest.mark.anyio
async def test_mcp_lists_only_read_only_zero_argument_tools():
    async with Client(create_mcp_server(CountingBridge())) as client:
        listed = await client.list_tools()
    assert {tool.name for tool in listed.tools} == {
        "get_game_overview", "get_combat_state", "get_interaction", "get_full_snapshot"
    }
    for item in listed.tools:
        assert item.input_schema.get("properties") == {}
        assert item.annotations.read_only_hint is True
        assert item.annotations.destructive_hint is False
        assert item.annotations.idempotent_hint is True
        assert item.annotations.open_world_hint is False


@pytest.mark.anyio
async def test_mcp_tools_return_structured_fresh_snapshots():
    bridge = CountingBridge()
    async with Client(create_mcp_server(bridge)) as client:
        first = await client.call_tool("get_game_overview", {})
        second = await client.call_tool("get_combat_state", {})
        full = await client.call_tool("get_full_snapshot", {})
    assert first.is_error is False
    assert first.structured_content["state_id"] == "mcp-1"
    assert second.structured_content["state_id"] == "mcp-2"
    assert second.structured_content["combat"]["hand"][0]["name"] == "痛击"
    assert full.structured_content["state_id"] == "mcp-3"
    assert bridge.calls == 3


@pytest.mark.anyio
async def test_mcp_tool_error_does_not_end_session():
    bridge = RecoveringBridge()
    async with Client(create_mcp_server(bridge)) as client:
        failed = await client.call_tool("get_game_overview", {})
        recovered = await client.call_tool("get_game_overview", {})
    assert failed.is_error is True
    assert "无法连接本地游戏桥接器" in failed.content[0].text
    assert recovered.is_error is False
    assert recovered.structured_content["state_id"] == "mcp-2"


@pytest.mark.anyio
async def test_stdio_server_initializes_without_stdout_noise():
    agent_dir = Path(__file__).resolve().parents[1]
    params = StdioServerParameters(
        command="uv",
        args=["--directory", str(agent_dir), "run", "sts2-mcp"],
    )
    async with Client(params, read_timeout_seconds=20) as client:
        listed = await client.list_tools()
    assert len(listed.tools) == 4
