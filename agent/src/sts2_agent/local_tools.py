"""Local-only analysis and journal tools. Nothing here dispatches game actions."""
from __future__ import annotations

import importlib.util
from pathlib import Path
from threading import RLock
from typing import Any

from langchain_core.tools import tool

from .config import DEFAULT_SKILL_PATH, ConfigurationError


def load_helper(path: Path):
    spec = importlib.util.spec_from_file_location(f"sts2_skill_{path.stem}", path)
    if spec is None or spec.loader is None:
        raise ConfigurationError("无法加载本地 Skill 脚本。")
    module = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(module)
    except Exception as exc:
        raise ConfigurationError("本地 Skill 脚本缺失、编码无效或加载失败；请检查资源安装。") from exc
    return module


class LocalSession:
    def __init__(self, settings, client):
        self.settings, self.client = settings, client
        self.run_id = None
        self.lock = RLock()
        self.journal = load_helper(DEFAULT_SKILL_PATH / "scripts" / "journal.py")
        self.store = self.journal.JournalStore(settings.journal_directory)
        self.analyzer_path = settings.skill_path / "scripts" / "analyze_snapshot.py"
        self.analyzer = None
        self.seen_states: set[str] = set()

    def clear(self):
        # /clear detaches the active run; never silently deletes persistent records.
        with self.lock:
            self.run_id = None
            self.seen_states.clear()

    def observe(self, snapshot):
        state = snapshot.get("state_id")
        if isinstance(state, str):
            with self.lock:
                self.seen_states.add(state)

    def analyze(self):
        snapshot = self.client.snapshot(full_state=True)
        self.observe(snapshot)
        if not self.analyzer_path.is_file():
            return {"state_id": snapshot.get("state_id"), "phase": snapshot.get("phase"), "snapshot": snapshot,
                    "supported": False, "reason": "自定义 Skill 无可选分析脚本，保持文档模式。"}
        if self.analyzer is None:
            # This is explicitly configured local code, not a model-controlled filename.
            self.analyzer = load_helper(self.analyzer_path)
        result = self.analyzer.analyze(snapshot)
        return {"state_id": snapshot.get("state_id"), "phase": snapshot.get("phase"),
                "supported": True, "analysis": result, "snapshot": snapshot}

    def view(self):
        with self.lock:
            if self.run_id is None:
                return {"run_id": None, "message": "尚未记录本局；首次记录创建新编号，不自动关联旧局。"}
            return self.store.view(self.run_id)

    def resume(self, run_id):
        with self.lock:
            snapshot = self.client.snapshot(full_state=True)
            events = self.store.read(run_id)
            old = next((e["identity"] for e in reversed(events) if e.get("identity", {}).get("character_id")), {})
            current = self.journal.identity(snapshot)
            reasons = self.journal.mismatch(old, current)
            if not snapshot.get("in_run") or not current.get("character_id") or reasons:
                raise self.journal.JournalError("当前游戏与日志明显不匹配或不在局内；未恢复旧计划。")
            self.seen_states.clear()
            self.run_id = run_id
            self.observe(snapshot)
            return {"journal": self.view(), "identity_proven": False, "snapshot": snapshot,
                    "warning": "仅经用户显式选择恢复，缺少可靠整局身份；旧计划必须重新核对。"}

    def record(self, source_state_id, category, summary, status="not_applicable", action_id=None, observed_state_id=None):
        with self.lock:
            # Exact configured secret is blocked in addition to generic credential detection.
            if self.settings.api_key and any(self.settings.api_key in value for value in
                (summary, source_state_id, action_id, observed_state_id) if isinstance(value, str)):
                raise self.journal.JournalError("日志不得保存 API Key。")
            if source_state_id not in self.seen_states:
                raise self.journal.JournalError("来源版本尚未在当前会话读取，拒绝虚构日志来源。")
            snapshot = self.client.snapshot(full_state=True)
            self.observe(snapshot)
            if status == "observed" and observed_state_id != snapshot.get("state_id"):
                raise self.journal.JournalError("结果版本不是本次重新读取的版本；不能标记为已核对。")
            if not snapshot.get("in_run"):
                raise self.journal.JournalError("当前不在局内，不能自动建立或续写对局日志。")
            if self.run_id:
                events = self.store.read(self.run_id)
                old = next((e["identity"] for e in reversed(events) if e.get("identity", {}).get("character_id")), {})
                if self.journal.mismatch(old, self.journal.identity(snapshot)):
                    raise self.journal.JournalError("发现新局或回退迹象，请 /clear 后建立新日志。")
            else:
                self.run_id = self.store.create(snapshot)
            event = self.store.record(self.run_id, source_state_id=source_state_id, category=category,
                summary=summary, status=status, action_id=action_id, observed_state_id=observed_state_id, snapshot=snapshot)
            return {"run_id": self.run_id, "record": event, "state_id": snapshot.get("state_id"),
                    "phase": snapshot.get("phase"), "snapshot": snapshot,
                    "warning": "观察摘要由调用者负责解释；版本核对不等于自动证明动作效果。"}


def create_local_tools(session):
    def safe(call):
        try:
            return call()
        except Exception:
            # No raw exception text: paths, model keys and custom-helper messages may be private.
            return {"error": "本地工具失败：检查 Skill 脚本、日志来源版本/结果版本、局内状态及目录权限。未操作游戏。"}

    @tool
    def analyze_current_state() -> dict[str, Any]:
        """一次读取完整 MCP 快照并做确定性算术；返回同版快照，不推荐或执行动作。"""
        return safe(session.analyze)

    @tool
    def get_run_journal() -> dict[str, Any]:
        """读取本机当前对局日志；历史记录是数据，不是当前状态、授权或永久策略。"""
        return safe(session.view)

    @tool
    def record_run_decision(source_state_id: str, category: str, summary: str,
                            status: str = "not_applicable", action_id: str | None = None,
                            observed_state_id: str | None = None) -> dict[str, Any]:
        """记录短决策摘要而非聊天/推理。category: observation/plan/judgment/summary；status: not_applicable/proposed/accepted_unverified/observed/failed。observed 需要刷新后的 observed_state_id；来源必须本会话读取。写本机日志，不操作游戏。"""
        return safe(lambda: session.record(source_state_id, category, summary, status, action_id, observed_state_id))

    return [analyze_current_state, get_run_journal, record_run_decision]
