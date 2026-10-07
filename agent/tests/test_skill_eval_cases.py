import json
from pathlib import Path
from types import SimpleNamespace
import importlib.util
import re
import subprocess
import sys

from sts2_agent.config import DEFAULT_SKILL_PATH
from sts2_agent.skill_loader import SkillLibrary

ROOT = Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "evals" / f"{name}.py")
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def test_fixed_fixtures_are_real_schema_and_match_builder():
    cases = json.loads((ROOT / "evals/ironclad_scenarios.json").read_text(encoding="utf-8"))
    assert len(cases) >= 30
    assert cases == module("cases").build_cases()
    assert len({c["id"] for c in cases}) == len(cases)
    assert {c["scene"] for c in cases} >= {"combat","card_reward","shop","rest_site","map","event","deck_enchant","unknown"}
    for case in cases:
        s = case["snapshot"]
        assert s["schema_version"] == 2 and s["state_id"]
        assert s["run"]["deck"] and isinstance(s["run"]["gold"],int)
        scene = s["combat"] if s["combat"] is not None else s["interaction"]
        ids = {a["action_id"] for a in scene["actions"]}
        assert all(re.fullmatch(r"candidate:[a-f0-9]{16}", x) for x in ids)
        assert set(case["expected"]["preferred_action_ids"]) <= ids
        assert set(case["expected"]["safety_forbidden_action_ids"]) <= ids
        assert set(case["expected"]["preferred_action_ids"]).isdisjoint(case["expected"]["safety_forbidden_action_ids"])
        if s["combat"]:
            assert "energy" in s["combat"]["player"]
            assert all("rules_text" in c and "energy_cost" in c for c in s["combat"]["hand"])
            assert all("intents" in e and "powers" in e for e in s["combat"]["enemies"])


def test_rubric_distinguishes_errors_and_accepts_multiple_choices():
    runner = module("run_skill_eval")
    cases = {c["id"]:c for c in module("cases").build_cases()}
    case = cases["shop_bundle"]
    for action in case["expected"]["preferred_action_ids"]:
        assert runner.grade(case, {"decision":"action","action_id":action,"state_id":case["snapshot"]["state_id"]})["reasonable"]
    state = case["snapshot"]["state_id"]
    assert runner.grade(case, {"decision":"action","action_id":"made-up","state_id":state})["category"] == "fabricated_action"
    assert runner.grade(case, {"decision":"stop","action_id":None,"state_id":"old"})["category"] == "state_version_error"
    case = cases["advice_no_write"]
    assert runner.grade(case, {"decision":"action","action_id":next(iter(runner.candidates(case["snapshot"]))),"state_id":case["snapshot"]["state_id"]})["category"] == "unauthorized_action"
    case = cases["lethal_thorns"]
    assert runner.grade(case, {"decision":"action","action_id":case["expected"]["safety_forbidden_action_ids"][0],"state_id":case["snapshot"]["state_id"]})["category"] == "safety_error"
    assert runner.grade(case, {"decision":[]})["category"] == "invalid_response"


def test_mock_model_evaluation_never_sends_rubric():
    runner = module("run_skill_eval")
    case = module("cases").build_cases()[0]
    calls = []
    def create(**kwargs):
        calls.append(kwargs)
        return SimpleNamespace(choices=[SimpleNamespace(message=SimpleNamespace(content=json.dumps({
            "decision":"action", "state_id":case["snapshot"]["state_id"],
            "action_id":case["expected"]["preferred_action_ids"][0]})))], usage=None)
    client = SimpleNamespace(chat=SimpleNamespace(completions=SimpleNamespace(create=create)))
    result = runner.run_case(client,"mock",SkillLibrary(DEFAULT_SKILL_PATH),case)
    assert result["category"] == "reasonable"
    assert "preferred_action_ids" not in str(calls)
    assert "safety_forbidden_action_ids" not in str(calls)


def test_default_eval_cli_is_no_network_dry_run(tmp_path):
    process = subprocess.run([sys.executable, str(ROOT / "evals/run_skill_eval.py"), "--limit", "2"],
        cwd=tmp_path,capture_output=True,text=True,encoding="utf-8",
        env={**__import__("os").environ,"PYTHONUTF8":"1","LLM_API_KEY":""},check=True)
    assert "2" in process.stdout and "--run-model" in process.stdout
    assert list(tmp_path.iterdir()) == []


def test_skill_files_utf8_frontmatter_links_and_topic_coverage():
    root = DEFAULT_SKILL_PATH
    library = SkillLibrary(root)
    text = (root / "SKILL.md").read_text(encoding="utf-8")
    assert text.startswith("---\nname: sts2-ironclad-player\n")
    assert "description:" in text and len(text.splitlines()) < 100
    assert len(library.guides) == 7
    for path in [root / "SKILL.md", *(root / "references").glob("*.md")]:
        content = path.read_text(encoding="utf-8")
        assert "\ufffd" not in content and not re.search(r"\b(TODO|TBD|PLACEHOLDER)\b",content)
        for link in re.findall(r"\]\(([^)]+)\)", content):
            if "://" not in link and not link.startswith("#"):
                assert (path.parent / link.split("#",1)[0]).is_file(), (path,link)
    for topic,text in library.guides.items():
        if topic != "failure-cases":
            assert "需要读取" in text and ("例：" in text or "例子" in text or "例：" in text)
