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
        with mock.patch.object(MD, "horse_prior", lambda at, d, *a: priors[at]), \
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


class FormAndSpecialistTests(unittest.TestCase):
    def test_form_label(self):
        self.assertEqual(MD.form_label([], 0), "İlk koşu")
        self.assertEqual(MD.form_label([2, 3], 2), "Formda")
        self.assertEqual(MD.form_label([4, 7], 2), "Yükselişte")
        self.assertEqual(MD.form_label([9, 8, 10], 3), "Durgun")

    def test_clean_name(self):
        self.assertEqual(MD._clean_name("AY İNCİSİ (T)"), "AY İNCİSİ")
        self.assertEqual(MD._clean_name("SATONO ARES (JPN)%"), "SATONO ARES")
        self.assertEqual(MD._clean_name("LOMITAS*"), "LOMITAS")

    def test_jockey_city_profile(self):
        S = MD.S
        blk = lambda n, w: S.build_finish_block(n, [w, 0, 0, 0, 0])
        # N. Avcı örneği: genel %19,7; İzmir %24 → ilde etkili; İstanbul %10 → ilde zayıf
        izmir = S.jockey_city_profile(blk(2239, 441), blk(1071, 261))
        self.assertTrue(izmir["city_strong"])
        self.assertFalse(izmir["elite"])
        ist = S.jockey_city_profile(blk(2239, 441), blk(281, 29))
        self.assertTrue(ist["city_weak"])
        self.assertFalse(ist["city_strong"])
        # V. Abiş: her yerde etkili
        self.assertTrue(S.jockey_city_profile(blk(2159, 454), blk(191, 54))["elite"])
        # Az koşu: işaret yok
        few = S.jockey_city_profile(blk(2239, 441), blk(10, 5))
        self.assertFalse(few["city_strong"] or few["city_weak"])


class StaleProgramTests(unittest.TestCase):
    def test_stale_program_served_and_refreshed(self):
        import time as _t
        S = MD.S
        key = "prog_01/01/2030_99_1"
        old = {"date": "01/01/2030", "races": [], "v": "eski"}
        with S.CACHE_LOCK:
            S.CACHE[key] = (_t.time() - S.TTL_PROGRAM - 10, old)
        calls = []

        def fake_fetch(date_str, city_id=None, **kw):
            calls.append(city_id)
            S.cache_set(key, {"v": "yeni"})
            return {"v": "yeni"}

        with mock.patch.object(S, "fetch_city_program", fake_fetch):
            self.assertEqual(S.stale_program("01/01/2030", "99")["v"], "eski")
            for _ in range(50):
                if calls and not S.PROG_REFRESHING:
                    break
                _t.sleep(0.02)
            self.assertEqual(calls, ["99"])
            self.assertIsNone(S.stale_program("01/01/2030", "99"))  # artık taze
        with S.CACHE_LOCK:
            S.CACHE.pop(key, None)


class GeneralFormTableTests(unittest.TestCase):
    def test_general_race_uses_site_probability(self):
        r = race("Handikap 17", agf=(50, 30, 20))
        for h, mp in zip(r["horses"], (40.0, 35.0, 25.0)):
            h["simulation"] = {"market_prob": mp, "win_prob": 10.0}
        with mock.patch.object(MD, "horse_prior", lambda at, d, *a: {"n_prev": 2, "last3": [1, 2], "form": "Formda"}), \
             mock.patch.object(MD, "sire_stats", lambda n: None):
            d = MD.analyze_race(r, "21/11/2026", general=True)
        self.assertTrue(d["general"])
        self.assertEqual([x["maiden_prob"] for x in d["rows"]], [40.0, 35.0, 25.0])
