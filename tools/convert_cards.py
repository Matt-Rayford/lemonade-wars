#!/usr/bin/env python3
"""Convert Lemonade Wars card CSVs (InDesign data-merge exports) into game-data JSON.

Usage: python3 tools/convert_cards.py

Reads:  game-assets/csv/*.csv
Writes: game-data/*.json

The CSVs contain one row per physical card copy. This script groups copies,
extracts gameplay-relevant fields, and validates totals against the rulebook's
component list. Data not present in any CSV (stands, turf, supporting cards)
is defined inline from the rulebook and Supporting Cards PDF.
"""

import csv
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSV_DIR = ROOT / "game-assets" / "csv"
OUT_DIR = ROOT / "game-data"


def clean(text: str) -> str:
    """Normalize InDesign text: <b> tags are soft line breaks, collapse whitespace."""
    text = text.replace("<b>", " ")
    return re.sub(r"\s+", " ", text).strip()


def slug(name: str) -> str:
    s = name.lower()
    s = re.sub(r"[''!,.]", "", s)
    s = re.sub(r"[^a-z0-9]+", "-", s)
    return s.strip("-")


def icon_name(path: str) -> str:
    """'Dollar_icon.png' -> 'dollar', 'Steal_Card.png' -> 'steal-card'"""
    base = path.rsplit(".", 1)[0]
    base = re.sub(r"_icon$", "", base, flags=re.IGNORECASE)
    return base.replace("_", "-").lower()


# ---------------------------------------------------------------- lemon deck

LEMON_TYPE_BY_NAMEPLATE = {
    "Tactic-BG.indd": "plan",
    "Attack-BG.indd": "attack",
    "Instant-BG.indd": "instant",
}


def convert_lemon():
    rows = list(csv.DictReader(open(CSV_DIR / "Lemon Deck.csv")))
    groups = defaultdict(list)
    for r in rows:
        groups[r["Title"]].append(r)

    cards = []
    for title, copies in sorted(groups.items()):
        r = copies[0]
        card = {
            "id": slug(title),
            "name": title,
            "type": LEMON_TYPE_BY_NAMEPLATE[r["@TypeBG"]],
            "count": len(copies),
            "effect": clean(r["Main"]),
            "flavor": clean(r["Flavor"]),
        }
        highlight = clean(r["Highlight"])
        if highlight:
            # e.g. "INSTANT: Play after an attack is played" -> play condition
            card["condition"] = re.sub(r"^(INSTANT|PLAN|ATTACK):\s*", "", highlight)
        cards.append(card)

    # Timeout cards are shuffled into the Lemon deck (rulebook p4/p12,
    # Supporting Cards PDF p78-79). Not in the CSV; defined here.
    cards.append({
        "id": "timeout",
        "name": "Timeout",
        "type": "timeout",
        "count": 2,
        "effect": (
            "Stop play and immediately do the following: All players discard "
            "down to 10 Lemon cards. The Whiniest Baby must pay $3 for each "
            "played tantrum (sell cards if needed) then discard all their "
            "tantrums and this card. Pass the Whiniest Baby card, draw a new "
            "card, then continue."
        ),
        "flavor": "",
    })
    return {"deck": "lemon", "cards": cards}


# ------------------------------------------------------- black market (RnD)

def convert_black_market():
    rows = list(csv.DictReader(open(CSV_DIR / "RnD Deck.csv")))
    groups = defaultdict(list)
    for r in rows:
        groups[r["Title"]].append(r)

    icon_cols = [c for c in rows[0].keys() if c.startswith("@") and "Icon" in c]

    cards = []
    for title, copies in sorted(groups.items()):
        r = copies[0]
        target = "stand" if r["Highlight"].startswith("Stand") else "turf"
        icons = sorted({
            icon_name(r[c]) for c in icon_cols
            if r[c] and r[c].lower().endswith(".png")
        })
        cards.append({
            "id": slug(title),
            "name": title,
            "target": target,  # which card type it equips to
            "cost": int(r["Price"]),
            "count": len(copies),
            "timing": r["Timing"],        # Passive | On Sale | On Your Turn | Power Pour
            "category": r["Category"],    # designer taxonomy; used by Lemon Lord titles
            "effect": clean(r["Main"]),
            "flavor": clean(r["Flavor"]),
            # one shape per physical copy; First Dibs titles count shapes
            "shapes": sorted(c["Shape"].lower() for c in copies),
            # icons printed on the card; some Lemon Lord titles count these
            "icons": icons,
        })
    return {"deck": "black-market", "cards": cards}


# ------------------------------------------------------------------- titles

def convert_titles():
    rows = list(csv.DictReader(open(CSV_DIR / "Title Deck.csv")))
    first_dibs, lemon_lord = [], []

    for r in rows:
        kind = "first-dibs" if "First-Dibs" in r["@Banner"] else "lemon-lord"
        # Condition text + optional counted icon live in one of three column sets.
        text = clean(r["Main"] or r["MainIcon"] or r["MainIconAlt"] or r["MainIconFirst"])
        icon = r["@Icon"] or r["@IconAlt"] or r["@IconFirst"]
        flavor = clean(r["Flavor"] or r["FlavorIcon"])

        card = {
            "id": slug(r["Title"]),
            "name": r["Title"],
            "kind": kind,
            "victoryPoints": 1,
            "condition": text + (f" [{icon_name(icon)}]" if icon else ""),
            "flavor": flavor,
        }
        if icon:
            card["countedIcon"] = icon_name(icon)
        if r["Reqd"]:
            card["requiredCount"] = int(r["Reqd"])
        if r["Tot"]:
            card["qualifyingCardsInDeck"] = int(r["Tot"])  # designer metadata, for validation
        (first_dibs if kind == "first-dibs" else lemon_lord).append(card)

    return {"firstDibs": first_dibs, "lemonLord": lemon_lord}


# ------------------------------------------- stands / turf / supporting cards
# No CSV exists for these; values come from the rulebook, the Supporting Cards
# PDF (visual read of dice faces), and the designer's notes:
#   Bargain  $2+, 1 upgrade slot,  sells on 2-3, earns $1, 21 cards
#   Classic  $3+, 2 upgrade slots, sells on 4-5, earns $2, 18 cards
#   Gourmet  $4+, 4 upgrade slots, sells on 6,   earns $3, 15 cards
#   Shapes (diamond/circle/square) divided evenly within each stand type.
#   Turf: 6 cards, power pour numbers 1-6, 5 upgrade slots, base pour = +$1.

def even_shapes(count):
    per = count // 3
    return {"diamond": per, "circle": per, "square": per}


STANDS = {
    "standTypes": [
        {
            "id": "bargain", "name": "Bargain Stand", "baseCost": 2,
            "upgradeSlots": 1, "saleNumbers": [2, 3], "baseEarnings": 1,
            "count": 21, "shapes": even_shapes(21),
            "tagline": "Low potential & earnings, sells often",
            "flavor": "We've all gotta start somewhere.",
        },
        {
            "id": "classic", "name": "Classic Stand", "baseCost": 3,
            "upgradeSlots": 2, "saleNumbers": [4, 5], "baseEarnings": 2,
            "count": 18, "shapes": even_shapes(18),
            "tagline": "Decent potential, sales, and earnings",
            "flavor": "Your standard cartoon lemonade stand.",
        },
        {
            "id": "gourmet", "name": "Gourmet Stand", "baseCost": 4,
            "upgradeSlots": 4, "saleNumbers": [6], "baseEarnings": 3,
            "count": 15, "shapes": even_shapes(15),
            "tagline": "High potential & earnings, few sales",
            "flavor": "Lemonade snobs will love it (and pay more).",
        },
    ],
    # Each stand costs base price + $1 per stand you already own.
    "costEscalationPerOwnedStand": 1,
}

TURF = {
    "count": 6,
    "upgradeSlots": 5,
    "powerPourNumbers": [1, 2, 3, 4, 5, 6],  # one card per number
    "basePowerPourAbility": "Take $1 from the bank.",
    "tagline": "Your base of operations",
    "flavor": "Build your empire. Dominate the market.",
}

SUPPORTING = {
    "braggingRights": {
        "count": 11,
        "victoryPointsEach": 1,
        "prices": [16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36],
        "purchaseLimitPerTurn": 1,
    },
    "whiniestBaby": {
        "awardedTo": "player with the most played tantrums (ties: most recent gain)",
        "effect": (
            "At the start of each turn, draw 2 Lemon cards instead of 1, then "
            "discard 1 of them. You must pay $3 per tantrum when the Timeout "
            "card is drawn, then discard all your tantrums."
        ),
    },
    "spoiledRotten": {
        "awardedTo": "player with the fewest victory points (must be sole last; ties: return to market)",
        "effect": "You get 1 free sale roll for only yourself at the start of each of your turns.",
    },
}

CONFIG = {
    "players": {"min": 2, "max": 5},
    "victoryPointsToTriggerEnd": 3,
    "saleDie": 6,
    "setup": {
        "startingMoney": 15,
        "startingHandSize": 5,
        "lemonLordDealt": 3,
        "lemonLordKept": 2,
        "firstDibsFaceUp": "playerCount + 1",
        "blackMarketFaceUp": 4,
        "blackMarketFaceUp2Player": 5,
        "firstPlayerBonus": "$1 x (players after you in turn order)",
        "initialBuys": {
            "rounds": 2,
            "order": "clockwise, then counter-clockwise (snake)",
            "mustBuyStand": True,
            "mayBuyBlackMarket": True,
        },
    },
    "turn": {
        "startDraw": 1,
        "actions": 2,
        "handLimit": None,  # no hand limit (Timeout forces discard to 10)
        "timeoutHandLimit": 10,
        "blackMarketRefreshCost": 1,  # free action, once per turn
    },
}


# ------------------------------------------------------------------ validate

def validate(lemon, bm, titles):
    ok = True

    def check(label, actual, expected):
        nonlocal ok
        status = "OK " if actual == expected else "FAIL"
        if actual != expected:
            ok = False
        print(f"  [{status}] {label}: {actual} (expected {expected})")

    print("Validation against rulebook component list:")
    check("Lemon cards", sum(c["count"] for c in lemon["cards"] if c["type"] != "timeout"), 69)
    check("Timeout cards", sum(c["count"] for c in lemon["cards"] if c["type"] == "timeout"), 2)
    check("Black Market cards", sum(c["count"] for c in bm["cards"]), 69)
    check("First Dibs titles", len(titles["firstDibs"]), 16)
    check("Lemon Lord titles", len(titles["lemonLord"]), 20)
    check("Bragging Rights", SUPPORTING["braggingRights"]["count"], 11)
    check("Stands", sum(s["count"] for s in STANDS["standTypes"]), 54)
    check("Turf cards", TURF["count"], 6)

    # Shape copies should be evenly split across the BM deck
    shape_counts = Counter(s for c in bm["cards"] for s in c["shapes"])
    check("BM shapes even", dict(shape_counts), {"circle": 23, "diamond": 23, "square": 23})

    # Cross-check Lemon Lord icon conditions against actual BM deck contents
    print("\nLemon Lord icon-count cross-check (Tot column vs BM deck):")
    icon_to_predicate = {
        "power-pour": lambda c: c["timing"] == "Power Pour",
        "sale": lambda c: c["timing"] == "On Sale",
        "shield": lambda c: c["category"] == "Defense",
        "die-wild": lambda c: c["category"] == "Roll Modification",
        "draw": lambda c: c["category"] == "Gain Card",
        "steal-card": lambda c: "Swap Card" in c["category"],
        "steal-dollar": lambda c: "Steal Money" in c["category"],
        "dollar": lambda c: "dollar" in c["icons"],
    }
    for t in titles["lemonLord"]:
        icon = t.get("countedIcon")
        if not icon or "qualifyingCardsInDeck" not in t:
            continue
        pred = icon_to_predicate.get(icon)
        if not pred:
            print(f"  [??  ] {t['name']}: no predicate for icon '{icon}'")
            continue
        n = sum(c["count"] for c in bm["cards"] if pred(c))
        check(f"{t['name']} [{icon}]", n, t["qualifyingCardsInDeck"])

    return ok


def main():
    OUT_DIR.mkdir(exist_ok=True)
    lemon = convert_lemon()
    bm = convert_black_market()
    titles = convert_titles()

    outputs = {
        "lemon-cards.json": lemon,
        "black-market-cards.json": bm,
        "titles.json": titles,
        "stands.json": STANDS,
        "turf.json": TURF,
        "supporting.json": SUPPORTING,
        "config.json": CONFIG,
    }
    for name, data in outputs.items():
        path = OUT_DIR / name
        path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
        print(f"wrote {path.relative_to(ROOT)}")

    print()
    ok = validate(lemon, bm, titles)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
