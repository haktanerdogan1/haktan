# -*- coding: utf-8 -*-
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import sim  # noqa: E402


class SimTests(unittest.TestCase):
    def test_win_rates_match_targets(self):
        entries = [{"no": i + 1, "mu": 85.0 + i * 0.3, "sd": 1.0, "target": t}
                   for i, t in enumerate((0.5, 0.3, 0.15, 0.05))]
        out = sim.simulate(entries, n=500)
        wins = {r["no"]: r["win"] for r in out["rows"]}
        for e in entries:
            self.assertAlmostEqual(wins[e["no"]], e["target"] * 100, delta=5)
        for r in out["rows"]:
            self.assertAlmostEqual(sum(r["pos_dist"]), 100, delta=0.6)
        self.assertEqual(len(out["samples"]), 25)
        self.assertEqual(len(out["samples"][0]), 4)

    def test_time_profile_converts_distance_and_weight(self):
        hist = [{"tarih": "01.10.2026", "sec": 72.0, "msf": 1200, "p_name": "Kum", "siklet": 56},
                {"tarih": "15.10.2026", "sec": 72.4, "msf": 1200, "p_name": "Kum", "siklet": 56}]
        p = sim.time_profile(hist, 1200, "Kum", 58, None)
        # ortalama 72.2 + 2 kg × 0.15 sn + 1.2 km × 1.25 sn
        self.assertAlmostEqual(p["mu"], 72.2 + 2 * sim.KG_SEC_PER_M * 1200 + sim.TIME_OFFSET_PER_KM * 1.2, places=2)
        self.assertEqual(p["basis"], "same")
        self.assertIsNone(sim.time_profile([], 1200, "Kum", 58, None))

    def test_fmt(self):
        self.assertEqual(sim.fmt(85.27), "1.25.27")
        self.assertEqual(sim.fmt(59.5), "59.50")


if __name__ == "__main__":
    unittest.main()
