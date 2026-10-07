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


def test_adapter_wrapped_metadata_and_text_blocks():
    agent, _ = make_agent()
    for kwargs in [
        {"content": "result", "artifact": {"structured_content": {"state_id": "new", "phase": "run"}}},
        {"content": [{"type": "text", "text": '{"state_id":"new","phase":"run"}'}]},
    ]:
        agent._update_metadata([ToolMessage(**kwargs, tool_call_id="read", name="get_interaction")])
        assert (agent.state_id, agent.phase) == ("new", "run")


def test_tool_error_cannot_replace_metadata():
    agent, _ = make_agent()
    agent._update_metadata([ToolMessage(content='{"state_id":"bad","phase":"run"}',
        status="error", tool_call_id="failed")])
    assert (agent.state_id, agent.phase) == (None, "unknown")


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
    assert {bound_tool.name for bound_tool in model.bound_tools} == {
        "get_combat_state",
        "get_current_strategy_guide",
        "analyze_current_state",
        "get_run_journal",
        "record_run_decision",
    }


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

    class SnapshotOnlyMcpClient:
        def snapshot(self, *, full_state=False):
            assert full_state is True
            calls.append("guide")
            return {"state_id": "combat-1", "phase": "combat", "combat": {}, "run": {"character_id": "IRONCLAD"}}

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
            "name": "get_current_strategy_guide",
            "args": {},
            "id": "guide-1",
        }]),
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
        mcp_client=SnapshotOnlyMcpClient(),
        model=model,
        tools=[get_combat_state, execute_action],
    )
    answer = agent.ask("继续完成这场战斗")
    assert calls == ["read", "guide", ("combat-1", "play-card-1"), "read"]
    assert answer.state_id == "combat-2"


def test_real_graph_analysis_then_journal_keeps_observed_versions(tmp_path):
    class Client:
        calls = 0

        def snapshot(self, *, full_state=False):
            assert full_state
            self.calls += 1
            return {"schema_version": 2, "state_id": "s1", "phase": "combat", "in_run": True,
                    "run": {"character_id": "IRONCLAD", "ascension": 10, "floor": 8},
                    "combat": {"player": {"energy": 3, "block": 0}, "enemies": [], "hand": [], "actions": []}}

    @tool
    def get_game_overview() -> dict:
        """Read a test overview; no game access."""
        return {"state_id": "s1", "phase": "combat"}

    model = ToolCallingFakeModel(messages=iter([
        AIMessage(content="", tool_calls=[{"name": "get_game_overview", "args": {}, "id": "read"}]),
        AIMessage(content="", tool_calls=[{"name": "record_run_decision", "args": {
            "source_state_id": "s1", "category": "plan", "summary": "先核对手牌与意图", "status": "proposed"}, "id": "log"}]),
        AIMessage(content="", tool_calls=[{"name": "analyze_current_state", "args": {}, "id": "analyze"}]),
        AIMessage(content="只分析，没有操作游戏。"),
    ]))
    client = Client()
    agent = Sts2Agent(Settings(api_key="not-real", journal_directory=tmp_path), model=model,
                      tools=[get_game_overview], mcp_client=client)
    answer = agent.ask("分析并记录当前计划，不执行")
    assert answer.state_id == "s1" and client.calls == 2
    journal = agent.get_journal()
    assert journal["record_count"] == 1
    assert journal["recent_records"][0]["status"] == "proposed"
    run_id = journal["run_id"]
    agent.clear_history()
    assert agent.get_journal()["run_id"] is None and agent.history == []
    resumed = agent.resume_run(run_id)
    assert resumed["identity_proven"] is False
    assert len(agent.history) == 1
    assert agent.local_session.seen_states == {"s1"}
