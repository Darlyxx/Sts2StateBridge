"""Local append-only decision journal; no game, network, or model access."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import sys
from uuid import uuid4


class JournalError(ValueError):
    pass


def data_directory():
    if os.name == "nt":
        return Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData" / "Local")) / "Sts2StateBridge"
    return Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share")) / "sts2-state-bridge"


def identity(snapshot):
    run = snapshot.get("run") or {}
    return {k: run.get(k) for k in ("character_id", "ascension", "act_number", "floor")}


def mismatch(previous, current):
    reasons = []
    for key in ("character_id", "ascension"):
        if previous.get(key) is not None and current.get(key) is not None and previous[key] != current[key]:
            reasons.append(f"{key} differs")
    for key in ("act_number", "floor"):
        old, new = previous.get(key), current.get(key)
        if isinstance(old, int) and isinstance(new, int) and new < old:
            reasons.append(f"{key} moved backwards")
    return reasons


def clean_text(value):
    if not isinstance(value, str) or len(value) > 2000:
        raise JournalError("每条日志字段必须为不超过 2000 字的短摘要，不接受完整聊天。")
    if re.search(r"(?:sk-[A-Za-z0-9_-]{6,}|bearer\s+\S+|(?:api[_ -]?key|token|secret)\s*[:=]\s*\S+)", value, re.I):
        raise JournalError("日志包含疑似凭据；请移除密钥后再记录。")
    return value.strip()


class JournalStore:
    def __init__(self, directory=None):
        self.directory = Path(directory) if directory else data_directory() / "journals"

    def path(self, run_id):
        if not isinstance(run_id, str) or not re.fullmatch(r"[a-f0-9]{32}", run_id):
            raise JournalError("无效日志编号；必须使用本机生成的 32 位编号。")
        root = self.directory.resolve()
        path = root / f"{run_id}.jsonl"
        if path.is_symlink() or path.resolve().parent != root:
            raise JournalError("日志路径不安全。")
        return path

    def create(self, snapshot):
        self.directory.mkdir(parents=True, exist_ok=True)
        run_id = uuid4().hex
        event = {"kind": "start", "run_id": run_id, "identity": identity(snapshot), "created_at": self.now(), "journal_version": 1}
        with self.path(run_id).open("x", encoding="utf-8") as handle:
            handle.write(json.dumps(event, ensure_ascii=False) + "\n")
        return run_id

    @staticmethod
    def now():
        return datetime.now(timezone.utc).isoformat()

    def read(self, run_id):
        try:
            events = [json.loads(line) for line in self.path(run_id).read_text(encoding="utf-8").splitlines() if line.strip()]
        except (OSError, ValueError) as exc:
            raise JournalError("日志不存在、不可读或已损坏；未采用旧计划。") from exc
        if not events or not all(isinstance(e, dict) for e in events) or events[0].get("run_id") != run_id:
            raise JournalError("日志格式或编号不匹配。")
        return events

    def record(self, run_id, *, source_state_id, category, summary, status="not_applicable",
               action_id=None, observed_state_id=None, snapshot=None):
        if category not in {"observation", "plan", "judgment", "summary"}:
            raise JournalError("category 必须为 observation / plan / judgment / summary。")
        if status not in {"not_applicable", "proposed", "accepted_unverified", "observed", "failed"}:
            raise JournalError("无效结果状态。")
        if not isinstance(source_state_id, str) or not source_state_id:
            raise JournalError("记录需要来源 state_id。")
        if status == "observed" and not observed_state_id:
            raise JournalError("已核对结果需要 observed_state_id；accepted 不是完成。")
        event = {"kind": "decision", "created_at": self.now(), "source_state_id": clean_text(source_state_id),
                 "category": category, "summary": clean_text(summary), "status": status,
                 "action_id": clean_text(action_id) if action_id else None,
                 "observed_state_id": clean_text(observed_state_id) if observed_state_id else None,
                 "identity": identity(snapshot or {})}
        path = self.path(run_id)
        # Exclusive short-lived lock prevents competing agents interleaving JSON records.
        lock = path.with_suffix(".lock")
        try:
            with lock.open("x", encoding="utf-8"):
                pass
        except FileExistsError as exc:
            raise JournalError("日志正在写入或存在中断留下的锁；请检查后重试，未覆盖记录。") from exc
        try:
            self.read(run_id)
            with path.open("a", encoding="utf-8") as handle:
                handle.write(json.dumps(event, ensure_ascii=False) + "\n")
                handle.flush()
                os.fsync(handle.fileno())
        finally:
            lock.unlink(missing_ok=True)
        return event

    def view(self, run_id):
        events = self.read(run_id)
        decisions = events[1:]
        summary = next((e for e in reversed(decisions) if e.get("category") == "summary"), None)
        return {"run_id": run_id, "identity": events[0]["identity"], "record_count": len(decisions),
                "latest_summary": summary, "recent_records": decisions[-20:],
                "older_records_retained": max(0, len(decisions) - 20),
                "warning": "日志为历史观察/计划，不是当前游戏事实或新授权；当前局身份无法自动证明。"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("new", help="stdin: snapshot JSON")
    for command in ("show", "record", "check-resume"):
        p = sub.add_parser(command)
        p.add_argument("run_id")
    args = parser.parse_args()
    store = JournalStore(args.directory)
    try:
        if args.command == "new":
            result = {"run_id": store.create(json.load(sys.stdin))}
        elif args.command == "show":
            result = store.view(args.run_id)
        elif args.command == "record":
            result = store.record(args.run_id, **json.load(sys.stdin))
        else:
            events = store.read(args.run_id)
            old = next((e["identity"] for e in reversed(events) if e.get("identity", {}).get("character_id")), {})
            reasons = mismatch(old, identity(json.load(sys.stdin)))
            result = {"compatible": not reasons, "reasons": reasons, "identity_proven": False,
                      "warning": "无明显冲突也不能证明同一局；仅在用户显式恢复后参考旧计划。"}
        print(json.dumps(result, ensure_ascii=False))
        return 0
    except (JournalError, OSError, TypeError, ValueError):
        print(json.dumps({"error": "Invalid journal input, unavailable file or unsafe record"}))
        return 2


if __name__ == "__main__":
    sys.stdin.reconfigure(encoding="utf-8-sig")
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
