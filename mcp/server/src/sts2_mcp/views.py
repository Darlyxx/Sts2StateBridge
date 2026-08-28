from __future__ import annotations

import json
from collections import Counter
from typing import Any

from .compact import compact_snapshot


def metadata(snapshot: dict) -> dict:
    return {
        key: snapshot.get(key)
        for key in ("schema_version", "bridge_version", "state_id", "phase", "screen_type", "in_run", "in_combat")
    }


def deck_summary(deck: Any) -> list[dict]:
    if not isinstance(deck, list):
        return []
    counts = Counter(
        (
            card.get("card_id") or card.get("name") or "unknown",
            bool(card.get("upgraded")),
            json.dumps(card.get("enchantment"), ensure_ascii=False, sort_keys=True)
            if card.get("enchantment") else None,
        )
        for card in deck
        if isinstance(card, dict)
    )
    return [
        {"card_id": card_id, "upgraded": upgraded, "enchantment": enchantment, "count": count}
        for (card_id, upgraded, enchantment), count in counts.items()
    ]


def run_overview(run: Any) -> dict:
    if not isinstance(run, dict):
        return {}
    result = {
        key: run.get(key)
        for key in (
            "character_id", "character_name", "current_hp", "max_hp", "gold",
            "floor", "act_number", "act_floor", "act_id", "ascension",
        )
    }
    result["deck_summary"] = deck_summary(run.get("deck"))
    result["relics"] = run.get("relics", [])
    result["potions"] = run.get("potions", [])
    return result


def clean(value: Any) -> Any:
    if isinstance(value, dict):
        return {
            key: clean(item)
            for key, item in value.items()
            if item is not None and item != [] and item != {}
        }
    if isinstance(value, list):
        return [clean(item) for item in value]
    return value


def game_overview_view(snapshot: dict) -> dict:
    return clean({**metadata(snapshot), "run": run_overview(snapshot.get("run"))})


def combat_state_view(snapshot: dict) -> dict:
    return clean({
        **metadata(snapshot),
        "run": run_overview(snapshot.get("run")),
        "combat": compact_snapshot(snapshot).get("combat"),
    })


def interaction_view(snapshot: dict) -> dict:
    return clean({
        **metadata(snapshot),
        "run": run_overview(snapshot.get("run")),
        "interaction": compact_snapshot(snapshot).get("interaction"),
    })


def full_snapshot_view(snapshot: dict) -> dict:
    return snapshot
