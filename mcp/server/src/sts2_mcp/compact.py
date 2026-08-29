from __future__ import annotations

from typing import Any


RUN_KEYS = {"character_id", "character_name", "current_hp", "max_hp", "gold", "floor", "act_number", "act_floor", "act_id", "ascension", "deck", "relics", "potions"}
COMBAT_KEYS = {"round", "current_side", "is_player_turn", "player", "hand", "enemies", "piles", "potions", "relics", "actions"}
INTERACTION_KEYS = {"type", "ready", "screen_type", "title", "description", "options", "actions", "map", "treasure", "player_gold"}


def _drop_empty(value: Any) -> Any:
    if isinstance(value, dict):
        cleaned = {key: _drop_empty(item) for key, item in value.items()}
        return {key: item for key, item in cleaned.items() if item is not None and item != [] and item != {}}
    if isinstance(value, list):
        return [_drop_empty(item) for item in value]
    return value


def _pick(source: Any, keys: set[str]) -> dict:
    if not isinstance(source, dict):
        return {}
    return {key: source[key] for key in keys if key in source}


def _compact_map(map_data: Any) -> dict:
    if not isinstance(map_data, dict):
        return {}
    nodes = map_data.get("nodes", [])
    relevant_ids = set(map_data.get("reachable_node_ids", []))
    current = map_data.get("current_node_id")
    if current:
        relevant_ids.add(current)
    return {
        "current_node_id": current,
        "reachable_node_ids": map_data.get("reachable_node_ids", []),
        "relevant_nodes": [node for node in nodes if isinstance(node, dict) and node.get("node_id") in relevant_ids],
    }


def _normalize_combat(combat: Any) -> dict:
    result = _pick(combat, COMBAT_KEYS)
    player = result.get("player")
    if not isinstance(player, dict):
        return result

    normalized_player = dict(player)
    if "mechanics" not in normalized_player and "stars" in normalized_player:
        normalized_player["mechanics"] = [
            {"type": "stars", "current": normalized_player["stars"]}
        ]
    normalized_player.pop("stars", None)
    result["player"] = normalized_player
    return result


def compact_snapshot(snapshot: dict, full_state: bool = False) -> dict:
    if full_state:
        return snapshot
    result: dict[str, Any] = {
        key: snapshot.get(key)
        for key in ("schema_version", "bridge_version", "state_id", "phase", "screen_type", "in_run", "in_combat")
    }
    result["run"] = _pick(snapshot.get("run"), RUN_KEYS)
    if snapshot.get("combat") is not None:
        result["combat"] = _normalize_combat(snapshot["combat"])
    elif snapshot.get("interaction") is not None:
        interaction = _pick(snapshot["interaction"], INTERACTION_KEYS)
        if "map" in interaction:
            interaction["map"] = _compact_map(interaction["map"])
        result["interaction"] = interaction
    return _drop_empty(result)
