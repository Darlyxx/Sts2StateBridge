from langchain_core.messages import AIMessage, AIMessageChunk, ToolMessage
from langchain_core.language_models.fake_chat_models import GenericFakeChatModel

from sts2_agent import Settings, Sts2Agent


class FakeBridge:
    def get_snapshot(self):
        return {"state_id": "state-lc", "phase": "combat", "in_combat": True, "combat": {"hand": []}}


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
    agent = Sts2Agent(Settings(api_key="not-a-real-key"), bridge=FakeBridge(), graph=graph)
    agent.tool_state.state_id = "state-lc"
    agent.tool_state.phase = "combat"
    return agent, graph


def test_langchain_ask_uses_bounded_graph_and_returns_metadata():
    agent, graph = make_agent()
    answer = agent.ask("怎么打？")
    assert answer.text == "建议防御。[state_id: state-lc]"
    # This fake graph does not execute a real tool, so stale metadata must not leak.
    assert (answer.state_id, answer.phase) == (None, "unknown")
    assert graph.invocations[0][1]["recursion_limit"] == 10
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


class ToolCallingFakeModel(GenericFakeChatModel):
    def bind_tools(self, tools, **kwargs):
        object.__setattr__(self, "bound_tools", tools)
        return self


def test_real_langchain_graph_executes_read_tool_then_answers():
    model = ToolCallingFakeModel(messages=iter([
        AIMessage(content="", tool_calls=[{"name": "get_combat_state", "args": {}, "id": "call-1"}]),
        AIMessage(content="读取完成。[state_id: state-lc]"),
    ]))
    agent = Sts2Agent(Settings(api_key="not-a-real-key"), bridge=FakeBridge(), model=model)
    answer = agent.ask("读取战斗状态")
    assert answer.text == "读取完成。[state_id: state-lc]"
    assert (answer.state_id, answer.phase) == ("state-lc", "combat")
    assert {tool.name for tool in model.bound_tools} == {
        "get_game_overview", "get_combat_state", "get_interaction", "get_full_snapshot"
    }
