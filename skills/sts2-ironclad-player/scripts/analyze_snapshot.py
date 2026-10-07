"""Pure, conservative schema-2 arithmetic. stdin JSON -> stdout JSON; no I/O in analyze."""
from __future__ import annotations

import argparse
from collections import Counter, deque
from itertools import combinations
import json
import math
import sys

VERSION = "0.9.0"


def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def obj(value):
    return value if isinstance(value, dict) else {}


def rows(value):
    return value if isinstance(value, list) else []


def costs(cards):
    known, unknown, x = Counter(), 0, 0
    for value in cards:
        card = obj(value)
        if card.get("costs_x") is True:
            x += 1
        elif card.get("costs_x") is False and number(card.get("energy_cost")) and card["energy_cost"] >= 0:
            known[str(card["energy_cost"])] += 1
        else:
            unknown += 1
    return {"known": dict(known), "x": x, "unknown": unknown,
            "basis": "energy_cost as supplied; master-deck costs are not hand costs"}


def deck_analysis(run):
    if not isinstance(run.get("deck"), list):
        return {"count": None, "coverage": "run.deck missing"}
    cards = [obj(c) for c in run["deck"]]
    ids = Counter(c["card_id"] for c in cards if isinstance(c.get("card_id"), str))
    return {"count": len(cards), "by_card_id": dict(ids),
            "unknown_card_ids": sum(not isinstance(c.get("card_id"), str) for c in cards),
            "upgraded_known": sum(c.get("upgraded") is True for c in cards),
            "upgraded_unknown": sum(not isinstance(c.get("upgraded"), bool) for c in cards),
            "enchanted_known": sum(isinstance(c.get("enchantment"), dict) for c in cards),
            "enchantment_unknown": sum("enchantment" not in c for c in cards),
            "cost_distribution": costs(cards),
            "types": dict(Counter(c["card_type"] for c in cards if isinstance(c.get("card_type"), str))),
            "unknown_types": sum(not isinstance(c.get("card_type"), str) for c in cards)}


def combat_analysis(combat):
    if not isinstance(combat, dict):
        return None
    player = obj(combat.get("player"))
    attacks, unknown, known_total = [], [], 0
    enemies = combat.get("enemies")
    if not isinstance(enemies, list):
        unknown.append("enemies missing")
    known_nonattack = {"Buff", "Debuff", "StrongDebuff", "DebuffStrong", "Defend", "Heal", "Summon", "Stun", "Escape", "Sleep", "CardDebuff", "Status"}
    for index, raw in enumerate(rows(enemies)):
        enemy = obj(raw)
        eid = enemy.get("instance_id", f"index:{index}")
        if enemy.get("is_alive") is False or enemy.get("is_dead") is True or enemy.get("is_escaped") is True:
            continue
        if not isinstance(enemy.get("intents"), list):
            unknown.append(f"{eid}: intents missing")
            continue
        if not enemy["intents"]:
            unknown.append(f"{eid}: no visible intent")
        for n, raw_intent in enumerate(enemy["intents"]):
            intent = obj(raw_intent)
            kind = intent.get("intent_type")
            # Only stable enum / explicit effect type, never localized label matching.
            effects = rows(intent.get("effects"))
            attack = (isinstance(kind, str) and "Attack" in kind) or any(obj(e).get("effect_type") == "attack" for e in effects)
            if not attack and (not isinstance(kind, str) or kind not in known_nonattack):
                unknown.append(f"{eid}:{n}: intent type unknown")
            if not attack:
                continue
            damage, hits, total = intent.get("damage"), intent.get("hits"), intent.get("total_damage")
            multiplied = damage * hits if number(damage) and damage >= 0 and isinstance(hits, int) and not isinstance(hits, bool) and hits >= 0 else None
            if not number(total) or total < 0:
                total = multiplied
            if total is not None and multiplied is not None and total != multiplied:
                unknown.append(f"{eid}:{n}: inconsistent damage and total")
                total = None
            if total is None:
                unknown.append(f"{eid}:{n}: attack damage unknown")
            else:
                known_total += total
            attacks.append({"enemy_instance_id": eid, "intent_index": n, "damage": damage,
                            "hits": hits, "total_damage": total,
                            "target_instance_ids": intent.get("target_instance_ids"), "derived": True})
    block = player.get("block")
    all_known = not unknown
    selection = obj(combat.get("selection"))
    selected = selection.get("selected_count")
    if selected is None and isinstance(selection.get("selected_instance_ids"), list):
        selected = len(selection["selected_instance_ids"])
    return {"energy": player.get("energy"), "block": block,
            "mechanics": player.get("mechanics"), "readiness": combat.get("readiness"),
            "hand_costs": costs(combat["hand"]) if isinstance(combat.get("hand"), list) else None,
            "hand": [{key: obj(c).get(key) for key in (
                "instance_id", "card_id", "base_energy_cost", "energy_cost", "costs_x", "star_cost",
                "costs_star_x", "effective_damage", "effective_block", "playable", "valid_target_indices")}
                for c in rows(combat.get("hand"))],
            "attacks": attacks, "known_attack_subtotal": known_total,
            "total_visible_attack": known_total if all_known else None,
            "block_subtracted_arithmetic": max(0, known_total - block) if all_known and number(block) and block >= 0 else None,
            "is_exact_hp_loss": False, "unknown": unknown, "derived": True,
            "coverage": {"hand_present": isinstance(combat.get("hand"), list), "player_present": isinstance(combat.get("player"), dict)},
            "selection": {k: selection.get(k) for k in (
                "selection_type", "min_select", "max_select", "confirm_enabled", "cancel_enabled", "skippable")}
                | {"selected_count": selected} if selection else None,
            "limitations": ["Attack sum is not a forecast: target distribution, powers, shields, summons, self-damage and retaliation may change HP loss.",
                            "Card display damage is not guaranteed target-specific damage; hand damage is deliberately not summed."]}


def shop_analysis(run, interaction):
    if interaction.get("type") != "shop":
        return None
    gold = run.get("gold")
    options, excluded = [], []
    for raw in rows(interaction.get("options")):
        option = obj(raw)
        if option.get("enabled") is not True:
            continue
        price = option.get("price")
        if not number(price) or price < 0 or not isinstance(option.get("option_id"), str):
            excluded.append(option.get("option_id"))
        else:
            options.append({"option_id": option["option_id"], "price": price, "kind": option.get("kind")})
    bundles, truncated = [], False
    # Bounded exhaustive subsets (at most 12 offers); not a strength ranking.
    limited = options[:12]
    if number(gold) and gold >= 0:
        for size in range(len(limited) + 1):
            for group in combinations(limited, size):
                total = sum(o["price"] for o in group)
                if total <= gold:
                    if len(bundles) >= 256:
                        truncated = True
                        break
                    bundles.append({"option_ids": [o["option_id"] for o in group], "cost": total, "remaining_gold": gold - total})
            if truncated:
                break
    return {"gold": gold if number(gold) else None, "offers": options, "budget_combinations": bundles,
            "excluded_unknown": excluded, "truncated": truncated or len(options) > 12,
            "limits": {"offers": 12, "combinations": 256},
            "coverage": "unknown budget" if not number(gold) else "current visible prices only",
            "limitations": ["Budget feasibility is not purchase legality or value. Stock, removal limits, potion slots and price-changing relics require refresh after each purchase."]}


def map_analysis(interaction):
    data = interaction.get("map")
    if not isinstance(data, dict):
        return None
    nodes = {n["node_id"]: n for n in rows(data.get("nodes")) if isinstance(n, dict) and isinstance(n.get("node_id"), str)}
    roots = rows(data.get("reachable_node_ids"))
    routes, missing, cycles = [], set(), False
    queue = deque(([root],) for root in roots if isinstance(root, str))
    steps = 0
    while queue and len(routes) < 128 and steps < 4096:
        (path,) = queue.popleft()
        steps += 1
        node = nodes.get(path[-1])
        if node is None:
            missing.add(path[-1])
        children = rows(obj(node).get("children"))
        children = [c for c in children if isinstance(c, str)]
        children_known = isinstance(obj(node).get("children"), list)
        stop = not children or len(path) >= 32
        if stop:
            routes.append({"node_ids": path, "composition": dict(Counter(nodes[n].get("node_type") or "unknown" for n in path if n in nodes)),
                           "complete_revealed_path": node is not None and children_known and not children})
        else:
            for child in children:
                if child in path:
                    cycles = True
                    continue
                queue.append(([*path, child],))
    reachable = set()
    pending = deque(r for r in roots if isinstance(r, str))
    while pending:
        nid = pending.popleft()
        if nid in reachable:
            continue
        reachable.add(nid)
        node = nodes.get(nid)
        if node is None:
            missing.add(nid)
        pending.extend(c for c in rows(obj(node).get("children")) if isinstance(c, str) and c not in reachable)
    return {"current_node_id": data.get("current_node_id"), "immediate_reachable": roots,
            "reachable_nodes": [{"node_id": n, "node_type": nodes[n].get("node_type")} for n in sorted(reachable) if n in nodes],
            "reachable_rest_sites": sorted(n for n in reachable if obj(nodes.get(n)).get("node_type") == "RestSite"),
            "reachable_shops": sorted(n for n in reachable if obj(nodes.get(n)).get("node_type") == "Shop"),
            "routes": routes, "missing_nodes": sorted(missing), "cycle_detected": cycles,
            "coverage": {"roots_present": isinstance(data.get("reachable_node_ids"), list),
                         "nodes_present": isinstance(data.get("nodes"), list),
                         "unknown_children": sorted(n for n in reachable if n in nodes and not isinstance(nodes[n].get("children"), list))},
            "truncated": bool(queue) or any(len(r["node_ids"]) >= 32 for r in routes),
            "limitations": ["Revealed directed edges only; unknown node types stay unknown. No encounter probability or route score."]}


def analyze(snapshot):
    if not isinstance(snapshot, dict):
        raise ValueError("snapshot must be a JSON object")
    run, interaction = obj(snapshot.get("run")), obj(snapshot.get("interaction"))
    return {"analysis_version": VERSION, "state_id": snapshot.get("state_id"), "phase": snapshot.get("phase", "unknown"),
            "derived": True, "deck": deck_analysis(run), "combat": combat_analysis(snapshot.get("combat")),
            "shop": shop_analysis(run, interaction), "map": map_analysis(interaction),
            "assumptions": ["Input is one schema-2 snapshot; no hidden information is consulted.",
                            "Missing fields mean unknown, not zero. Legal actions remain exclusively in the supplied snapshot."],
            "limitations": ["Arithmetic aid, not a simulator, strength score, win probability or best-action recommendation."]}


def main():
    argparse.ArgumentParser(description=__doc__).parse_args()
    try:
        result = analyze(json.load(sys.stdin))
        print(json.dumps(result, ensure_ascii=False, allow_nan=False))
        return 0
    except (ValueError, TypeError, OverflowError):
        print(json.dumps({"error": "Invalid snapshot JSON or numeric fields"}))
        return 2


if __name__ == "__main__":
    sys.stdin.reconfigure(encoding="utf-8-sig")
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
