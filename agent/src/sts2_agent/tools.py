from __future__ import annotations

import json
from collections import Counter
from dataclasses import dataclass
from typing import Any

from langchain_core.tools import BaseTool, tool

from .bridge import BridgeClient, BridgeError
from .compact import compact_snapshot


@dataclass(slots=True)
class ToolState:
    state_id: str | None = None
    phase: str = "unknown"


def _metadata(snapshot: dict) -> dict:
    return {key: snapshot.get(key) for key in ("schema_version", "bridge_version", "state_id", "phase", "screen_type", "in_run", "in_combat")}


def _deck_summary(deck: Any) -> list[dict]:
    if not isinstance(deck, list):
        return []
    counts = Counter((card.get("card_id") or card.get("name") or "unknown", bool(card.get("upgraded")), json.dumps(card.get("enchantment"), ensure_ascii=False, sort_keys=True) if card.get("enchantment") else None) for card in deck if isinstance(card, dict))
    return [{"card_id": card_id, "upgraded": upgraded, "enchantment": enchantment, "count": count} for (card_id, upgraded, enchantment), count in counts.items()]


def _run_overview(run: Any) -> dict:
    if not isinstance(run, dict):
        return {}
    result = {key: run.get(key) for key in ("character_id", "character_name", "current_hp", "max_hp", "gold", "floor", "act_number", "act_floor", "act_id", "ascension")}
    result["deck_summary"] = _deck_summary(run.get("deck"))
    result["relics"] = run.get("relics", [])
    result["potions"] = run.get("potions", [])
    return result


def _clean(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: _clean(item) for key, item in value.items() if item is not None and item != [] and item != {}}
    if isinstance(value, list):
        return [_clean(item) for item in value]
    return value


def build_read_tools(bridge: BridgeClient, state: ToolState) -> list[BaseTool]:
    def read() -> dict:
        snapshot = bridge.get_snapshot()
        state.state_id = snapshot.get("state_id")
        state.phase = snapshot.get("phase", "unknown")
        return snapshot

    def result_or_error(builder) -> str:
        try:
            return json.dumps(_clean(builder(read())), ensure_ascii=False, separators=(",", ":"))
        except BridgeError as exc:
            return json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False)

    @tool
    def get_game_overview() -> str:
        """读取最新游戏阶段、state_id、角色资源和整局构筑摘要。适合一般局势与构筑问题。"""
        return result_or_error(lambda snapshot: {**_metadata(snapshot), "run": _run_overview(snapshot.get("run"))})

    @tool
    def get_combat_state() -> str:
        """读取最新战斗状态，包括玩家、手牌、牌堆、药水、遗物、敌人、意图与合法候选。只用于战斗问题。"""
        return result_or_error(lambda snapshot: {**_metadata(snapshot), "run": _run_overview(snapshot.get("run")), "combat": compact_snapshot(snapshot).get("combat")})

    @tool
    def get_interaction() -> str:
        """读取最新非战斗交互，包括地图、奖励、事件、商店、休息点或宝箱的可见选项。"""
        return result_or_error(lambda snapshot: {**_metadata(snapshot), "run": _run_overview(snapshot.get("run")), "interaction": compact_snapshot(snapshot).get("interaction")})

    @tool
    def get_full_snapshot() -> str:
        """读取完整原始游戏快照。仅在其他只读工具确实缺少回答所需字段时使用。"""
        return result_or_error(lambda snapshot: snapshot)

    return [get_game_overview, get_combat_state, get_interaction, get_full_snapshot]
