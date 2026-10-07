from copy import deepcopy
import ast
import json
from pathlib import Path
import subprocess
import sys

import pytest

from sts2_agent.config import DEFAULT_SKILL_PATH, Settings
from sts2_agent.local_tools import LocalSession, create_local_tools, load_helper


analysis = load_helper(DEFAULT_SKILL_PATH / "scripts/analyze_snapshot.py")
journal = load_helper(DEFAULT_SKILL_PATH / "scripts/journal.py")


def snapshot(state="s1"):
    return {"schema_version": 2, "state_id": state, "phase": "combat", "in_run": True,
            "run": {"character_id": "IRONCLAD", "ascension": 10, "floor": 8, "act_number": 1, "gold": 100, "deck": []},
            "combat": {"player": {"energy": 2, "block": 5}, "hand": [], "enemies": []}}


class Client:
    def __init__(self, *snapshots):
        self.snapshots = iter(snapshots)
        self.calls = 0

    def snapshot(self, *, full_state=False):
        assert full_state is True
        self.calls += 1
        return next(self.snapshots)


def test_unknowns_not_zero_and_no_text_type_guess():
    result = analysis.analyze({})
    assert result["deck"]["count"] is None
    s = snapshot()
    s["run"]["deck"] = [{"name": "防御抽牌力量", "rules_text": "gain block draw attack", "energy_cost": 1}]
    result = analysis.analyze(s)["deck"]
    assert result["unknown_types"] == 1 and result["types"] == {}
    assert result["cost_distribution"]["unknown"] == 1  # X flag not known


def test_counts_dynamic_x_chinese_and_enchantment():
    s = snapshot()
    s["run"]["deck"] = [
        {"card_id":"STRIKE", "name":"打击", "upgraded":True, "enchantment":{"id":"E"}, "energy_cost":0, "base_energy_cost":1, "costs_x":False},
        {"card_id":"STRIKE", "upgraded":False, "enchantment":None, "energy_cost":-1, "costs_x":True}, {}]
    result = analysis.analyze(s)
    assert result["state_id"] == "s1"
    assert result["deck"]["by_card_id"] == {"STRIKE": 2}
    assert result["deck"]["upgraded_known"] == result["deck"]["enchanted_known"] == 1
    assert result["deck"]["cost_distribution"] ["known"] == {"0":1}
    assert result["deck"]["cost_distribution"]["x"] == 1


@pytest.mark.parametrize("damage,hits,total,expected", [(4,3,12,12), (4,3,None,12), (None,3,None,None), (4,3,99,None), (0,3,0,0)])
def test_attack_arithmetic(damage, hits, total, expected):
    s = snapshot()
    s["combat"]["enemies"] = [{"is_alive":True,"instance_id":"e1","intents":[{"intent_type":"Attack","damage":damage,"hits":hits,"total_damage":total}]}]
    result = analysis.analyze(s)["combat"]
    assert result["total_visible_attack"] == expected
    assert result["block_subtracted_arithmetic"] == (None if expected is None else max(0, expected-5))
    assert result["is_exact_hp_loss"] is False


def test_selection_and_cost_fields_preserved_without_combo_damage():
    s = snapshot()
    s["combat"]["selection"] = {"min_select":2,"max_select":2,"selected_instance_ids":["c1"],"confirm_enabled":False}
    s["combat"]["hand"] = [{"energy_cost":0,"base_energy_cost":2,"costs_x":False,"star_cost":3,"effective_damage":20}]
    result = analysis.analyze(s)["combat"]
    assert result["selection"]["selected_count"] == 1
    assert result["hand"][0]["star_cost"] == 3
    assert "hand_total_damage" not in result


def test_shop_combinations_unknown_and_sold_out():
    s = snapshot()
    s["interaction"] = {"type":"shop", "options":[
        {"option_id":"a","enabled":True,"price":40}, {"option_id":"b","enabled":True,"price":60},
        {"option_id":"c","enabled":False,"price":0}, {"option_id":"d","enabled":True,"price":None}]}
    result = analysis.analyze(s)["shop"]
    assert any(c["option_ids"] == ["a","b"] and c["remaining_gold"] == 0 for c in result["budget_combinations"])
    assert result["excluded_unknown"] == ["d"]
    s["run"]["gold"] = None
    assert analysis.analyze(s)["shop"]["budget_combinations"] == []


def test_map_graph_forks_unknown_edges_cycles_and_limits():
    s = snapshot()
    s["interaction"] = {"map":{"reachable_node_ids":["a"],"nodes":[
        {"node_id":"a","node_type":"Monster","children":["b","c"]},
        {"node_id":"b","node_type":"RestSite","children":[]},
        {"node_id":"c","node_type":"Shop","children":["a","missing"]}]}}
    result = analysis.analyze(s)["map"]
    assert result["reachable_rest_sites"] == ["b"] and result["reachable_shops"] == ["c"]
    assert result["missing_nodes"] == ["missing"] and result["cycle_detected"]
    assert any(r["node_ids"] == ["a","b"] for r in result["routes"])


def test_analysis_pure_and_stdin_cli_utf8(monkeypatch, tmp_path):
    import socket
    def forbidden(*args, **kwargs):
        raise AssertionError("unexpected I/O")
    with monkeypatch.context() as m:
        m.setattr(socket, "socket", forbidden)
        m.setattr(Path, "open", forbidden)
        s = snapshot(); original = deepcopy(s)
        analysis.analyze(s)
        assert s == original
    tree = ast.parse((DEFAULT_SKILL_PATH / "scripts/analyze_snapshot.py").read_text(encoding="utf-8"))
    imports = {n.names[0].name.split(".")[0] for n in ast.walk(tree) if isinstance(n, ast.Import)}
    assert not imports & {"requests","httpx","socket","urllib","openai","subprocess"}
    result = subprocess.run([sys.executable, str(DEFAULT_SKILL_PATH / "scripts/analyze_snapshot.py")],
        input=json.dumps(snapshot(),ensure_ascii=False),capture_output=True,text=True,encoding="utf-8",cwd=tmp_path,check=True)
    assert json.loads(result.stdout)["state_id"] == "s1"
    assert list(tmp_path.iterdir()) == []


def test_analysis_reads_exactly_one_snapshot(tmp_path):
    client = Client(snapshot("new"))
    session = LocalSession(Settings(api_key="fake",journal_directory=tmp_path),client)
    result = session.analyze()
    assert client.calls == 1
    assert result["state_id"] == result["snapshot"]["state_id"] == result["analysis"]["state_id"] == "new"


def test_custom_skill_without_scripts_is_document_mode(tmp_path):
    session = LocalSession(Settings(api_key="fake",skill_path=tmp_path,journal_directory=tmp_path / "logs"), Client(snapshot()))
    assert session.analyze()["supported"] is False
    assert not (tmp_path / "logs").exists()


def test_journal_unique_append_preserves_summary_and_checks_paths(tmp_path):
    store = journal.JournalStore(tmp_path)
    first, second = store.create(snapshot()), store.create(snapshot())
    assert first != second
    for i in range(22):
        store.record(first,source_state_id="s1",category="summary",summary=f"计划{i}",snapshot=snapshot())
    assert len(store.read(first)) == 23
    view = store.view(first)
    assert view["record_count"] == 22 and view["older_records_retained"] == 2
    assert view["latest_summary"]["summary"] == "计划21"
    with pytest.raises(journal.JournalError): store.path("../secret")
    with pytest.raises(journal.JournalError): store.record(first,source_state_id="s1",category="observation",summary="完成",status="observed")
    with pytest.raises(journal.JournalError): store.record(first,source_state_id="s1",category="plan",summary="api_key=private")


def test_journal_session_facts_plans_resume_and_clear(tmp_path):
    client = Client(snapshot("s2"),snapshot("s3"),snapshot("s4"))
    session = LocalSession(Settings(api_key="actual-test-secret",journal_directory=tmp_path),client)
    session.observe(snapshot("s1"))
    result = session.record("s1","plan","下一层走篝火",status="proposed")
    run_id = result["run_id"]
    result = session.record("s1","observation","选牌已选中但尚未确认",status="observed",observed_state_id="s3")
    assert result["record"]["category"] == "observation"
    session.clear()
    assert session.view()["run_id"] is None
    resumed = session.resume(run_id)
    assert resumed["identity_proven"] is False and resumed["journal"]["record_count"] == 2


def test_resume_rejects_mismatch_and_record_errors_are_safe(tmp_path):
    store = journal.JournalStore(tmp_path); run_id = store.create(snapshot())
    s = snapshot(); s["run"]["floor"] = 1
    session = LocalSession(Settings(api_key="actual-test-secret",journal_directory=tmp_path), Client(s))
    with pytest.raises(session.journal.JournalError): session.resume(run_id)
    assert session.run_id is None
    tool = {t.name:t for t in create_local_tools(session)}["record_run_decision"]
    result = tool.invoke({"source_state_id":"fake","category":"plan","summary":"actual-test-secret"})
    assert "error" in result and "actual-test-secret" not in json.dumps(result)


def test_unobserved_result_cannot_be_completed(tmp_path):
    session = LocalSession(Settings(api_key="fake",journal_directory=tmp_path), Client(snapshot("s2")))
    session.observe(snapshot())
    with pytest.raises(session.journal.JournalError):
        session.record("s1","observation","升级完成",status="observed",observed_state_id="not-read")
    assert session.run_id is None


@pytest.mark.parametrize("kind", ["Hidden", "Unknown", "DeathBlow", "CustomFutureIntent"])
def test_special_intents_cannot_prove_zero_incoming(kind):
    s = snapshot()
    s["combat"]["enemies"] = [{"is_alive": True, "intents": [{"intent_type": kind}]}]
    assert analysis.analyze(s)["combat"]["total_visible_attack"] is None


def test_missing_collections_keep_coverage_unknown():
    s = snapshot()
    s["combat"].pop("hand")
    s["combat"]["enemies"] = [{"is_alive": True, "intents": []}]
    s["interaction"] = {"map": {"reachable_node_ids": ["n"], "nodes": [{"node_id":"n"}]}}
    result = analysis.analyze(s)
    assert result["combat"]["hand_costs"] is None
    assert result["combat"]["total_visible_attack"] is None
    assert not result["map"]["routes"][0]["complete_revealed_path"]
    assert result["map"]["coverage"]["unknown_children"] == ["n"]


def test_journal_cli_new_record_show_and_invalid_json(tmp_path):
    script = str(DEFAULT_SKILL_PATH / "scripts/journal.py")
    def run(command, data=None):
        return subprocess.run([sys.executable, script, "--directory", str(tmp_path), *command],
            input=json.dumps(data, ensure_ascii=False) if data is not None else None,
            capture_output=True, text=True, encoding="utf-8")
    created = run(["new"], snapshot())
    assert created.returncode == 0
    run_id = json.loads(created.stdout)["run_id"]
    recorded = run(["record",run_id], {"source_state_id":"s1","category":"plan","summary":"计划去篝火","status":"proposed"})
    assert recorded.returncode == 0
    assert json.loads(run(["show",run_id]).stdout)["recent_records"][0]["summary"] == "计划去篝火"
    assert run(["show","../escape"]).returncode == 2


def test_local_tool_failures_do_not_expose_exceptions(tmp_path):
    class BrokenClient:
        def snapshot(self, **kwargs):
            raise RuntimeError("sk-private-error-123456")
    session = LocalSession(Settings(api_key="key", journal_directory=tmp_path), BrokenClient())
    tools = {t.name:t for t in create_local_tools(session)}
    value = tools["analyze_current_state"].invoke({})
    assert "error" in value and "sk-private" not in json.dumps(value)


def test_repl_journal_commands_do_not_call_model(monkeypatch, capsys, tmp_path):
    from sts2_agent.cli import run_repl
    calls = []
    class Agent:
        def get_journal(self):
            calls.append("view")
            return {"run_id": None}
        def resume_run(self, run_id):
            calls.append(run_id)
            raise ValueError("private-details")
    inputs = iter(["/journal", "/resume-run abc", "/quit"])
    monkeypatch.setattr("builtins.input", lambda *args: next(inputs))
    assert run_repl(Agent(), False, False) == 0
    assert calls == ["view", "abc"]
    assert "private-details" not in capsys.readouterr().err
