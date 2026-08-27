from types import SimpleNamespace

from sts2_agent import Settings, Sts2Agent


class FakeBridge:
    def get_snapshot(self):
        return {"state_id": "state-7", "phase": "combat", "in_combat": True, "combat": {"hand": [{"name": "痛击"}]}}


class FakeCompletions:
    def __init__(self):
        self.calls = []

    def create(self, **kwargs):
        self.calls.append(kwargs)
        if kwargs["stream"]:
            return iter([
                SimpleNamespace(choices=[SimpleNamespace(delta=SimpleNamespace(content="先打"))]),
                SimpleNamespace(choices=[SimpleNamespace(delta=SimpleNamespace(content="痛击"))]),
            ])
        return SimpleNamespace(choices=[SimpleNamespace(message=SimpleNamespace(content="先打痛击"))])


def make_agent():
    completions = FakeCompletions()
    client = SimpleNamespace(chat=SimpleNamespace(completions=completions))
    settings = Settings(api_key="not-a-real-key", model="deepseek-v4-flash")
    return Sts2Agent(settings, bridge=FakeBridge(), client=client), completions


def test_ask_returns_state_metadata_and_records_history():
    agent, completions = make_agent()
    answer = agent.ask("怎么打？")
    assert (answer.text, answer.state_id, answer.phase) == ("先打痛击", "state-7", "combat")
    assert completions.calls[0]["model"] == "deepseek-v4-flash"
    assert "state-7" in completions.calls[0]["messages"][-1]["content"]
    assert len(agent.history) == 2


def test_stream_collects_answer_for_memory():
    agent, _ = make_agent()
    state, chunks = agent.ask_stream("怎么打？")
    assert state["state_id"] == "state-7"
    assert "".join(chunks) == "先打痛击"
    assert agent.history[-1]["content"] == "先打痛击"


def test_clear_history():
    agent, _ = make_agent()
    agent.ask("问题")
    agent.clear_history()
    assert agent.history == []
