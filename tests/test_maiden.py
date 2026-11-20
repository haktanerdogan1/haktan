# -*- coding: utf-8 -*-
import os
import sys
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import maiden as MD  # noqa: E402


def race(cond="Maiden/DHÖW", agf=(50, 30, 20)):
    return {"race_no": 3, "condition": cond, "horses": [
        {"no": i + 1, "name": f"AT{i+1}", "at_id": str(100 + i), "start_no": i + 1, "agf_pct": a, "sire": f"BABA{i+1}"}
        for i, a in enumerate(agf)]}


class MaidenTests(unittest.TestCase):
    def test_classes(self):
        self.assertTrue(MD.is_maiden_race(race("Maiden/DHÖW")))
        self.assertTrue(MD.is_maiden_race(race("ŞARTLI 1/Dişi")))
        self.assertTrue(MD.is_maiden_race(race("ŞARTLI 19")))
        self.assertFalse(MD.is_maiden_race(race("ŞARTLI 4")))
        self.assertFalse(MD.is_maiden_race(race("Handikap 15")))

    def test_model_file_has_weights(self):
        m = MD.load_model()
        for k in ("speed", "no_time", "first_time", "sire_win", "draw", "last_23", "agf"):
            self.assertIn(k, m["with_agf"])
        self.assertNotIn("agf", m["without_agf"])

    def _run(self, agf):
        priors = {"100": {"n_prev": 3, "last_pos": 2, "best_norm": 73.0, "best_norm_str": "1.13.00"},
                  "101": {"n_prev": 0, "last_pos": None, "best_norm": None},
                  "102": {"n_prev": 2, "last_pos": 5, "best_norm": 75.0, "best_norm_str": "1.15.00"}}
        sires = {"BABA1": {"win": 20.0}, "BABA2": {"win": 8.0}, "BABA3": None}
        with mock.patch.object(MD, "horse_prior", lambda at, d: priors[at]), \
             mock.patch.object(MD, "sire_stats", lambda n: sires[n]):
            return MD.analyze_race(race(agf=agf), "21/11/2026")

    def test_probabilities_and_features(self):
        d = self._run((50, 30, 20))
        self.assertTrue(d["has_agf"])
        self.assertAlmostEqual(sum(r["maiden_prob"] for r in d["rows"]), 100, delta=0.5)
        by = {r["no"]: r for r in d["rows"]}
        self.assertEqual(by[2]["features"]["first_time"], 1.0)
        self.assertEqual(by[2]["features"]["no_time"], 1.0)
        self.assertEqual(by[1]["speed_rank"], 1)
        self.assertEqual(d["rows"][0]["no"], 1)  # en hızlı, güçlü babalı, AGF favorisi
        self.assertEqual([r["maiden_rank"] for r in d["rows"]], [1, 2, 3])

    def test_without_agf_uses_features(self):
        d = self._run((0, 0, 0))
        self.assertFalse(d["has_agf"])
        self.assertEqual(d["rows"][0]["no"], 1)


if __name__ == "__main__":
    unittest.main()
