import json

from langchain_core.messages import AIMessage, AIMessageChunk, ToolMessage
from langchain_core.language_models.fake_chat_models import GenericFakeChatModel
from langchain_core.tools import tool

from sts2_agent import Settings, Sts2Agent


class FakeGraph:
    def __init__(self):
        self.invocations = []

    def invoke(self, payload, config):
        self.invocations.append((payload, config))
        return {"messages": [*payload["messages"], AIMessage(content="建议防御。[state_id: state-lc]")]}

    def stream(self, payload, config, stream_mode):
        self.invocations.append((payload, config, stream_mode))
        yield ToolMessage(content="{}", tool_call_id="call-1", name="get_combat_state"), {"langgraph_node": "tools"}
        yield AIMessageChunk(content="建议"), {"langgraph_node": "model"}
        yield AIMessageChunk(content="防御"), {"langgraph_node": "model"}


def make_agent():
    graph = FakeGraph()
    agent = Sts2Agent(Settings(api_key="not-a-real-key"), graph=graph)
    return agent, graph


def test_langchain_ask_uses_bounded_graph_and_returns_metadata():
    agent, graph = make_agent()
    answer = agent.ask("怎么打？")
    assert answer.text == "建议防御。[state_id: state-lc]"
    # This fake graph does not execute a real tool, so stale metadata must not leak.
    assert (answer.state_id, answer.phase) == (None, "unknown")
    assert graph.invocations[0][1]["recursion_limit"] == 40
    assert len(agent.history) == 2


def test_langchain_stream_reports_tool_without_mixing_it_into_answer():
    agent, _ = make_agent()
    called = []
    state, chunks = agent.ask_stream("怎么打？", on_tool_call=called.append)
    assert "".join(chunks) == "建议防御"
    assert called == ["get_combat_state"]
    assert state == {"state_id": None, "phase": "unknown"}
    assert agent.history[-1].content == "建议防御"


def test_langchain_clear_history():
    agent, _ = make_agent()
    agent.ask("问题")
    agent.clear_history()
    assert agent.history == []


def test_langchain_reads_metadata_from_mcp_structured_artifact():
    agent, _ = make_agent()
    message = ToolMessage(
        content="tool result",
        artifact={"state_id": "artifact-state", "phase": "combat"},
        tool_call_id="call-artifact",
        name="get_combat_state",
    )
    agent._update_metadata([message])
    assert (agent.state_id, agent.phase) == ("artifact-state", "combat")


class ToolCallingFakeModel(GenericFakeChatModel):
    def bind_tools(self, tools, **kwargs):
        object.__setattr__(self, "bound_tools", tools)
        return self


def test_real_langchain_graph_executes_read_tool_then_answers():
    @tool
    def get_combat_state() -> str:
        """Read combat state."""
        return json.dumps({"state_id": "state-lc", "phase": "combat", "combat": {"hand": []}})

    model = ToolCallingFakeModel(messages=iter([
        AIMessage(content="", tool_calls=[{"name": "get_combat_state", "args": {}, "id": "call-1"}]),
        AIMessage(content="读取完成。[state_id: state-lc]"),
    ]))
    agent = Sts2Agent(Settings(api_key="not-a-real-key"), model=model, tools=[get_combat_state])
    answer = agent.ask("读取战斗状态")
    assert answer.text == "读取完成。[state_id: state-lc]"
    assert (answer.state_id, answer.phase) == ("state-lc", "combat")
    assert {bound_tool.name for bound_tool in model.bound_tools} == {"get_combat_state"}


def test_status_question_reads_but_does_not_execute():
    calls = []

    @tool
    def get_game_overview() -> str:
        """Read game overview."""
        calls.append("read")
        return json.dumps({"state_id": "overview-1", "phase": "map", "character_id": "IRONCLAD"})

    @tool
    def execute_action(state_id: str, action_id: str) -> str:
        """Execute a candidate game action."""
        calls.append((state_id, action_id))
        return json.dumps({"accepted": True})

    model = ToolCallingFakeModel(messages=iter([
        AIMessage(content="", tool_calls=[{"name": "get_game_overview", "args": {}, "id": "read-1"}]),
        AIMessage(content="当前在地图。[state_id: overview-1]"),
    ]))
    agent = Sts2Agent(
        Settings(api_key="not-a-real-key"),
        model=model,
        tools=[get_game_overview, execute_action],
    )
    agent.ask("我现在在哪？")
    assert calls == ["read"]


def test_authorized_battle_executes_then_refreshes_state():
    calls = []

    @tool
    def get_combat_state() -> str:
        """Read combat state."""
        calls.append("read")
        state_id = "combat-1" if calls.count("read") == 1 else "combat-2"
        return json.dumps({
            "state_id": state_id,
            "phase": "combat",
            "character_id": "IRONCLAD",
            "actions": [{"action_id": "play-card-1"}] if state_id == "combat-1" else [],
        })

    @tool
    def execute_action(state_id: str, action_id: str) -> str:
        """Execute a candidate game action."""
        calls.append((state_id, action_id))
        return json.dumps({"accepted": True, "state_id": state_id})

    model = ToolCallingFakeModel(messages=iter([
        AIMessage(content="", tool_calls=[{"name": "get_combat_state", "args": {}, "id": "read-1"}]),
        AIMessage(content="", tool_calls=[{
            "name": "execute_action",
            "args": {"state_id": "combat-1", "action_id": "play-card-1"},
            "id": "act-1",
        }]),
        AIMessage(content="", tool_calls=[{"name": "get_combat_state", "args": {}, "id": "read-2"}]),
        AIMessage(content="动作后已刷新。[state_id: combat-2]"),
    ]))
    agent = Sts2Agent(
        Settings(api_key="not-a-real-key"),
        model=model,
        tools=[get_combat_state, execute_action],
    )
    answer = agent.ask("继续完成这场战斗")
    assert calls == ["read", ("combat-1", "play-card-1"), "read"]
    assert answer.state_id == "combat-2"
