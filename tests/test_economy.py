"""Economy tests for v0.7. Run: python -m unittest discover -s tests -v

Every assertion checks a rule against an independent computation (no tautologies like v0.6's
`assertGreaterEqual(87, 80)`), and the verifier must not modify design data.
"""
import hashlib
import json
import random
import sys
import tempfile
import shutil
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import verify_economy as ve  # noqa: E402

DATA = ve.load()
CURVES = DATA["gacha"]["curves"]
PROG = DATA["progression"]
VECTORS = json.loads((ROOT / "engine" / "tests" / "vectors" / "rng_gacha_vectors.json").read_text(encoding="utf-8"))


def simulate_featured(curve, pulls_budget, trials, seed):
    """Plain Monte Carlo of the pity rules (independent of the DP)."""
    rnd = random.Random(seed)
    feat = curve["featuredSsrBps"] / 10000
    hits = 0
    for _ in range(trials):
        n, guar = 0, False
        for _ in range(pulls_budget):
            p = ve.ssr_rate(curve, n) / 10000
            if rnd.random() < p:
                if guar or rnd.random() < feat:
                    hits += 1
                    break
                guar, n = True, 0
            else:
                n += 1
    return hits / trials


class GachaMath(unittest.TestCase):
    def test_distribution_sums_to_one_and_hard_pity_is_certain(self):
        for name, c in CURVES.items():
            dist = ve.ssr_distribution(c)
            self.assertAlmostEqual(sum(dist), 1.0, places=12, msg=name)
            self.assertEqual(len(dist), c["hard"])
            self.assertEqual(ve.ssr_rate(c, c["hard"] - 1), 10000)

    def test_soft_pity_is_monotonic(self):
        for c in CURVES.values():
            rates = [ve.ssr_rate(c, n) for n in range(c["hard"])]
            self.assertEqual(rates, sorted(rates))
            self.assertEqual(rates[0], c["ssrBase"])

    def test_python_mean_matches_js_engine_vectors(self):
        for name, mean in VECTORS["curveMeans"].items():
            self.assertAlmostEqual(ve.curve_stats(CURVES[name])["mean"], mean, places=9, msg=name)

    def test_featured_probability_dp_matches_monte_carlo(self):
        c = CURVES["character"]
        exact = ve.featured_within(c, 96)
        mc = simulate_featured(c, 96, 40000, 7)
        self.assertLess(abs(exact - mc), 0.01, (exact, mc))

    def test_featured_guaranteed_by_double_hard_pity(self):
        self.assertAlmostEqual(ve.featured_within(CURVES["character"], 160), 1.0, places=9)
        self.assertLess(ve.featured_within(CURVES["character"], 159), 1.0)
        self.assertAlmostEqual(ve.featured_within(CURVES["weapon"], 140), 1.0, places=9)

    def test_featured_mean_formula_matches_dp_expectation(self):
        for k in ("character", "weapon"):
            c = CURVES[k]
            # E[pulls] = Σ_{n≥0} P(no featured within n pulls)
            exp = sum(1 - ve.featured_within(c, n) for n in range(0, 2 * c["hard"] + 1))
            self.assertAlmostEqual(exp, ve.featured_mean(c), places=6, msg=k)

    def test_80_pulls_do_not_guarantee_the_featured_character(self):
        # the v0.6 test asserted 87 >= 80; the real question is P(featured | 80 pulls) < 1
        p80 = ve.featured_within(CURVES["character"], 80)
        self.assertGreater(p80, 0.5)
        self.assertLess(p80, 0.7)

    def test_starting_guarantee_carries_over(self):
        c = CURVES["character"]
        self.assertAlmostEqual(ve.featured_within(c, 80, start_n=0, start_guar=True), 1.0, places=9)
        self.assertAlmostEqual(ve.featured_within(c, 1, start_n=79, start_guar=True), 1.0, places=9)


class SeasonIncome(unittest.TestCase):
    def test_daily_ledger_rebuilt_from_rules_equals_declared_table(self):
        s = ve.season_summary(DATA)
        self.assertEqual(s["crystalsMax"], s["tableSum"])
        self.assertEqual(s["crystalsMax"], PROG["seasonIncome"]["f2p"]["crystals"])
        self.assertEqual(DATA["gacha"]["seasonIncome"]["f2p"], PROG["seasonIncome"]["f2p"])

    def test_optional_rift_is_the_only_difference_between_base_and_max(self):
        s = ve.season_summary(DATA)
        rift = sum(f["reward"] for f in PROG["recordRift"]["floors"]) * PROG["recordRift"]["cyclesPerSeason"]
        self.assertEqual(s["crystalsMax"] - s["crystalsBase"], rift)

    def test_ledger_is_monotonic_and_42_days(self):
        rows = ve.daily_ledger(PROG)
        self.assertEqual(len(rows), 42)
        self.assertTrue(all(b["cumulative"] >= a["cumulative"] for a, b in zip(rows, rows[1:])))

    def test_f2p_season_pulls_are_below_the_full_guarantee(self):
        s = ve.season_summary(DATA)
        self.assertLess(s["limitedPullsMax"], 2 * CURVES["character"]["hard"])
        self.assertGreater(s["pFeaturedWithinMaxPulls"], 0.6)


class Progression(unittest.TestCase):
    def test_xp_curve_matches_engine_vectors(self):
        for row in VECTORS["stageScale"]:
            if row["level"] < 60:
                self.assertEqual(ve.char_xp(PROG, row["level"]), row["xpToNext"])

    def test_full_build_totals(self):
        b = ve.full_build(PROG)
        self.assertEqual(b["charXp"], sum(ve.char_xp(PROG, lv) for lv in range(1, 60)))
        self.assertEqual(b["weaponXp"] * 2, b["charXp"])
        self.assertEqual(b["cores"], 6 + 2 * 4)
        self.assertEqual(b["books"], 2 * 118)

    def test_weekly_boss_is_the_binding_constraint(self):
        f = ve.stamina_for_build(PROG, ve.full_build(PROG))
        self.assertLess(f["fullBuildsPer42dByCores"], f["fullBuildsPer42dByStamina"])

    def test_ascension_account_gates_are_reachable_within_two_seasons(self):
        self.assertLess(ve.account_days(PROG, 40)["days"], 42)


class Safety(unittest.TestCase):
    def test_verifier_writes_reports_only(self):
        tmp = Path(tempfile.mkdtemp())
        try:
            (tmp / "data").mkdir()
            shutil.copy(ROOT / "data" / "game_data.json", tmp / "data" / "game_data.json")
            before = hashlib.sha256((tmp / "data" / "game_data.json").read_bytes()).hexdigest()
            ve.run(tmp, write=True)
            after = hashlib.sha256((tmp / "data" / "game_data.json").read_bytes()).hexdigest()
            self.assertEqual(before, after)
            written = sorted(p.relative_to(tmp).as_posix() for p in tmp.rglob("*") if p.is_file())
            self.assertEqual(written, ["data/game_data.json", "reports/daily_income_42d.csv", "reports/economy_v0.7.json", "reports/economy_v0.7.txt"])
        finally:
            shutil.rmtree(tmp)


if __name__ == "__main__":
    unittest.main()
