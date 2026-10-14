#!/usr/bin/env python3
"""Memory Forge v0.7 economy verification.

Reads data/game_data.json (generated from content/*.mjs) and writes ONLY to reports/:
  reports/economy_v0.7.json   machine-readable results
  reports/economy_v0.7.txt    human summary
  reports/daily_income_42d.csv day-by-day F2P ledger
It never writes to data/ or content/ (v0.6 verify_design.py overwrote design data; fixed).

Everything is computed exactly (Markov chains over pity state), not by sampling.
Python 3.11+, standard library only.
"""
from __future__ import annotations

import csv
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BPS = 10000


def load(root: Path = ROOT) -> dict:
    return json.loads((root / "data" / "game_data.json").read_text(encoding="utf-8"))


# ───────────────────────── gacha ─────────────────────────
def ssr_rate(curve: dict, n: int) -> int:
    """SSR chance (bps) of the next pull when n pulls passed since the last SSR (same as the engine)."""
    k = n + 1
    if k >= curve["hard"]:
        return BPS
    if k >= curve["softStart"]:
        return min(BPS, curve["ssrBase"] + curve["softStep"] * (k - curve["softStart"] + 1))
    return curve["ssrBase"]


def ssr_distribution(curve: dict) -> list[float]:
    """P(first SSR on pull k), k = 1..hard."""
    surv, out = 1.0, []
    for k in range(1, curve["hard"] + 1):
        p = ssr_rate(curve, k - 1) / BPS
        out.append(surv * p)
        surv *= 1 - p
    return out


def curve_stats(curve: dict) -> dict:
    dist = ssr_distribution(curve)
    mean = sum((i + 1) * p for i, p in enumerate(dist))
    cdf, acc = {}, 0.0
    for i, p in enumerate(dist):
        acc += p
        if i + 1 in (10, 30, 50, 60, 66, 70, 75, 80):
            cdf[i + 1] = acc
    return {"mean": mean, "consolidatedRate": 1 / mean, "cdf": cdf, "hard": curve["hard"], "sumProb": sum(dist)}


def featured_within(curve: dict, pulls: int, start_n: int = 0, start_guar: bool = False) -> float:
    """Exact P(at least one featured SSR within `pulls` pulls). State = (pulls since SSR, guarantee flag)."""
    feat = curve.get("featuredSsrBps", BPS) / BPS
    state = {(start_n, start_guar): 1.0}
    got = 0.0
    for _ in range(pulls):
        nxt: dict = {}
        for (n, guar), pr in state.items():
            p = ssr_rate(curve, n) / BPS
            if p < 1:
                nxt[(n + 1, guar)] = nxt.get((n + 1, guar), 0.0) + pr * (1 - p)
            if guar:
                got += pr * p
            else:
                got += pr * p * feat
                nxt[(0, True)] = nxt.get((0, True), 0.0) + pr * p * (1 - feat)
        state = nxt
    return got


def featured_mean(curve: dict) -> float:
    feat = curve.get("featuredSsrBps", BPS) / BPS
    return curve_stats(curve)["mean"] * (2 - feat)


# ───────────────────────── progression ─────────────────────────
def char_xp(prog: dict, lv: int) -> int:
    x = prog["levelling"]["charXp"]
    return x["k"] * (x["a"] + lv + (lv * lv) // x["sqDiv"])


def full_build(prog: dict) -> dict:
    """Materials and credits for ONE character from Lv.1 to fully built (Lv.60, A4, weapon Lv.60 A4, skills 10/10, gear +15 ×2)."""
    L = prog["levelling"]
    cap = prog["stats"]["levelCap"]
    cxp = sum(char_xp(prog, lv) for lv in range(1, cap))
    wxp = round(cxp * L["weaponXpRatio"])
    asc = L["charAscension"]
    wasc = L["weaponAscension"]
    skills = L["skillCosts"]
    lines = len(L["skillLines"])
    g = prog["stats"]["gear"]
    gear_polish = 2 * (L["gearCraft"]["polish"] + L["gearEnhance"]["polishPerLevel"] * sum(range(1, g["maxEnhance"] + 1)))
    gear_credits = 2 * (L["gearCraft"]["credits"] + L["gearEnhance"]["creditsPerLevel"] * sum(range(1, g["maxEnhance"] + 1)))
    credits = {
        "charLevel": int(cxp * L["creditsPerXp"]),
        "charAscension": sum(a["credits"] for a in asc),
        "weaponLevel": int(wxp * L["creditsPerXp"]),
        "weaponAscension": sum(a["credits"] for a in wasc),
        "skills": lines * sum(s["credits"] for s in skills),
        "gear": gear_credits,
    }
    return {
        "charXp": cxp, "weaponXp": wxp,
        "seals": sum(a["seals"] for a in asc), "alloys": sum(a["alloys"] for a in wasc),
        "books": lines * sum(s["books"] for s in skills),
        "cores": sum(a["cores"] for a in asc) + lines * sum(s["cores"] for s in skills),
        "polish": gear_polish,
        "credits": credits, "creditsTotal": sum(credits.values()),
    }


def stamina_for_build(prog: dict, build: dict) -> dict:
    """Growth-dungeon runs (and stamina) to farm one full build. Credits from non-dungeon sources are ignored (conservative)."""
    dg = {d["id"]: d for d in prog["dungeons"]}
    runs = {
        "dg_ink": math.ceil(build["charXp"] / dg["dg_ink"]["yield"]["ink"]),
        "dg_oil": max(math.ceil(build["weaponXp"] / dg["dg_oil"]["yield"]["oil"]), math.ceil(build["alloys"] / dg["dg_oil"]["yield"]["alloy"])),
        "dg_seal": math.ceil(build["seals"] / dg["dg_seal"]["yield"]["seal"]),
        "dg_book": math.ceil(build["books"] / dg["dg_book"]["yield"]["book"]),
        "dg_polish": math.ceil(build["polish"] / dg["dg_polish"]["yield"]["polish"]),
        "dg_credit": math.ceil(build["creditsTotal"] / dg["dg_credit"]["yield"]["credits"]),
    }
    total_runs = sum(runs.values())
    cost = prog["dungeons"][0]["cost"]
    st = prog["stamina"]
    daily = st["dailyRegen"] + st["missionBonusPerDay"]
    wb = prog["weeklyBoss"]
    cores_per_week = wb["claimsPerWeek"] * wb["perClaim"]["core"]
    # event shop: two 21-day events per season, each selling `core` up to its limit
    event_cores = 2 * sum(i["limit"] for i in prog["exchangeShops"]["event"]["items"] if i["item"] == "core")
    return {"runs": runs, "totalRuns": total_runs, "stamina": total_runs * cost, "daysOfStamina": total_runs * cost / daily,
            "coresPerWeek": cores_per_week, "eventCoresPerSeason": event_cores, "weeksOfWeeklyBoss": build["cores"] / cores_per_week,
            "fullBuildsPer42dByStamina": 42 * daily / (total_runs * cost), "fullBuildsPer42dByCores": (6 * cores_per_week + event_cores) / build["cores"]}


def account_days(prog: dict, target: int) -> dict:
    a = prog["accountLevel"]
    need = sum(a["xp"]["per"] * lv + a["xp"]["base"] for lv in range(1, target))
    st = prog["stamina"]
    per_day = (st["dailyRegen"] + st["missionBonusPerDay"]) * a["xpSources"]["staminaSpent"] + a["xpSources"]["dailyMissionComplete"]
    return {"target": target, "xp": need, "xpPerDay": per_day, "days": need / per_day}


# ───────────────────────── season income ─────────────────────────
def daily_ledger(prog: dict, include_optional: bool = True) -> list[dict]:
    """Day-by-day F2P crystals for one 42-day season, built from the mission/event RULES (not from the summary table)."""
    m = prog["missions"]
    si = prog["seasonIncome"]
    rows = []
    total = 0
    event_total = next(r["crystals"] for r in si["f2pRecurring"] if r["src"] == "시즌 이벤트")
    pass_total = next(r["crystals"] for r in si["f2pRecurring"] if r["src"] == "무료 패스")
    rift = prog["recordRift"]
    rift_cycle = sum(f["reward"] for f in rift["floors"])
    for day in range(1, si["days"] + 1):
        daily = sum(ms["crystals"] for ms in m["daily"]["milestones"])
        weekly = m["weekly"]["crystals"] if day % 7 == 0 else 0
        weekly_run = m["weeklyRun"]["crystals"] if day % 7 == 0 else 0
        attendance = m["attendance"]["weeklyCrystals"] if day % 7 == 0 else 0
        # event: two 21-day events, paid out evenly on days 7/14/21 and 28/35/42 (event shop + missions)
        event = event_total // 6 if day % 7 == 0 else 0
        free_pass = pass_total // 6 if day % 7 == 0 else 0
        rift_pay = rift_cycle if include_optional and day in (14, 28, 42) else 0
        day_total = daily + weekly + weekly_run + attendance + event + free_pass + rift_pay
        total += day_total
        rows.append({"day": day, "daily": daily, "weekly": weekly, "weeklyRun": weekly_run, "attendance": attendance, "event": event,
                     "freePass": free_pass, "rift": rift_pay, "dayTotal": day_total, "cumulative": total})
    return rows


def season_summary(data: dict) -> dict:
    prog = data["progression"]
    g = data["gacha"]
    full = daily_ledger(prog, True)
    base = daily_ledger(prog, False)
    t = prog["seasonIncome"]["tickets"]
    cost = g["costPerPull"]
    out = {
        "crystalsMax": full[-1]["cumulative"], "crystalsBase": base[-1]["cumulative"],
        "limitedTickets": t["limitedTicket"], "standardTickets": t["standardTicket"],
        "limitedPullsMax": full[-1]["cumulative"] / cost + t["limitedTicket"],
        "limitedPullsBase": base[-1]["cumulative"] / cost + t["limitedTicket"],
        "tableSum": sum(r["crystals"] for r in prog["seasonIncome"]["f2pRecurring"]),
        "declaredF2p": prog["seasonIncome"]["f2p"],
    }
    curve = g["curves"]["character"]
    out["pFeaturedWithinMaxPulls"] = featured_within(curve, int(out["limitedPullsMax"]))
    out["pFeaturedWithinBasePulls"] = featured_within(curve, int(out["limitedPullsBase"]))
    return out


def products_summary(data: dict) -> dict:
    p = data["progression"]["products"]
    cost = data["gacha"]["costPerPull"]
    packs = [{"id": x["id"], "krw": x["krw"], "crystals": x["crystals"], "krwPerPull": x["krw"] / (x["crystals"] / cost),
              "krwPerPullFirst": x["krw"] / ((x["crystals"] + x["firstBonus"]) / cost)} for x in p["crystalPacks"]]
    mo = p["monthly"]
    monthly_total = mo["upfrontPaid"] + mo["dailyFree"] * mo["days"]
    return {"packs": packs, "monthlyTotal": monthly_total, "monthlyKrwPerPull": mo["krw"] / (monthly_total / cost),
            "passKrwPerPull": p["pass"]["krw"] / (p["pass"]["crystals"] / cost),
            "hardPityKrwAtBestPack": 80 * min(x["krwPerPull"] for x in packs), "fullPityKrwAtBestPack": 160 * min(x["krwPerPull"] for x in packs)}


def run(root: Path = ROOT, write: bool = True) -> dict:
    data = load(root)
    prog = data["progression"]
    curves = data["gacha"]["curves"]
    gacha = {k: curve_stats(c) for k, c in curves.items()}
    for k in ("character", "weapon"):
        gacha[k]["featuredMean"] = featured_mean(curves[k])
        gacha[k]["featuredWorst"] = 2 * curves[k]["hard"]
        gacha[k]["pFeaturedWithin"] = {n: featured_within(curves[k], n) for n in (50, 80, 96, 120, 140, 160)}
    build = full_build(prog)
    result = {
        "version": data["meta"]["version"],
        "gacha": gacha,
        "season": season_summary(data),
        "fullBuild": build,
        "farming": stamina_for_build(prog, build),
        "account": [account_days(prog, 20), account_days(prog, 40), account_days(prog, 60)],
        "products": products_summary(data),
        "status": "DESIGN_PROPOSAL — exact math of the configured rules; not live balance, not a revenue forecast.",
    }
    if write:
        rep = root / "reports"
        rep.mkdir(exist_ok=True)
        (rep / "economy_v0.7.json").write_text(json.dumps(result, ensure_ascii=False, indent=1), encoding="utf-8")
        rows = daily_ledger(prog, True)
        with (rep / "daily_income_42d.csv").open("w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        (rep / "economy_v0.7.txt").write_text(summary_text(result), encoding="utf-8")
    return result


def summary_text(r: dict) -> str:
    g, s, b, f = r["gacha"], r["season"], r["fullBuild"], r["farming"]
    lines = [
        f"Memory Forge v{r['version']} economy verification (exact)",
        f"character banner: mean SSR {g['character']['mean']:.2f} pulls, consolidated {100 * g['character']['consolidatedRate']:.3f}%, "
        f"featured mean {g['character']['featuredMean']:.2f}, worst {g['character']['featuredWorst']}",
        f"weapon banner: mean SSR {g['weapon']['mean']:.2f}, featured mean {g['weapon']['featuredMean']:.2f}, worst {g['weapon']['featuredWorst']}",
        f"F2P 42 days: crystals {s['crystalsMax']} (base {s['crystalsBase']}), limited tickets {s['limitedTickets']} → "
        f"{s['limitedPullsMax']:.1f} pulls max / {s['limitedPullsBase']:.1f} base",
        f"P(featured limited within F2P season pulls, from 0 pity): max {100 * s['pFeaturedWithinMaxPulls']:.1f}% / base {100 * s['pFeaturedWithinBasePulls']:.1f}%",
        f"full build per character: charXP {b['charXp']}, weaponXP {b['weaponXp']}, credits {b['creditsTotal']}, books {b['books']}, cores {b['cores']}",
        f"farming: {f['totalRuns']} runs = {f['stamina']} stamina = {f['daysOfStamina']:.1f} days; cores {f['weeksOfWeeklyBoss']:.2f} weeks of weekly boss",
        f"builds per 42 days: by stamina {f['fullBuildsPer42dByStamina']:.2f}, by cores {f['fullBuildsPer42dByCores']:.2f}",
        f"account: AL20 {r['account'][0]['days']:.1f} days, AL40 {r['account'][1]['days']:.1f} days, AL60 {r['account'][2]['days']:.1f} days",
        r["status"],
    ]
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    res = run(Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT)
    sys.stdout.write(summary_text(res))
