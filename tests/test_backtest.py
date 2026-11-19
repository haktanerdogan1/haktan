# -*- coding: utf-8 -*-
"""backtest.py testleri — internet gerekmez."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import backtest as B  # noqa: E402

RESULTS_CSV = """Ankara;(82. Yarış Günü);12/11/2026
1. Kosu :   12.30;Maiden; 2 Yaşlı İngilizler; 57.00kg; 1600m; Çim;;;;Rekor Derece :1.33.82
At No;At İsmi;Yaş;Orijin(Baba);Orijin(Anne);Kilo;Jokey Adı;Sahip Adı;Antrenör Adı;St;AGF;H;Derece;Ganyan;Fark
1;TIME BREAKER DB SK;2y d  e;TIME TEST (GB);DALHOUSIE (GB);57;H.KARATAŞ;KEMAL KURT;İB.B.OĞULLARI;1;%56.39(1);;1:38.99;1,15;5 Boy
2;CHASE HORIZON KG DB SK;2y d  e;ROMANISED (IRE);VICUNA (FR);54 +2.00;ER.CANKILIÇ AP;X;Y;11 - DS;%14.55(2);32;1:39.89;4,50;Boyun
3;REGISTA SK;2y d  e;VICTOIRE PISA (JPN);AMBUSHER;57 +0.50;M.G.ARSLAN;X;Y;2;%0.56(9);15;Derecesiz;49,00;
10;GALVARİNO DB SKG (Koşmaz);2y a  e;BODYGUARD HERO (USA);ATEŞ GIRL;57;Ö.YILDIRIM;X;Y;4;%0.25(11);;Koşmaz;;
GANYAN(7) :1,15 TL
"""


class TestResults(unittest.TestCase):
    def test_parse_results(self):
        races = B.parse_results_csv(RESULTS_CSV)
        r = races[1]
        self.assertEqual([x["name"] for x in r], ["TIME BREAKER", "CHASE HORIZON", "REGISTA", "GALVARİNO"])
        self.assertEqual(r[0]["pos"], 1)
        self.assertEqual(r[0]["ganyan"], 1.15)
        self.assertEqual(r[1]["ganyan"], 4.5)
        self.assertTrue(r[2]["finished"])          # Derecesiz: koştu ama derece almadı
        self.assertTrue(r[3]["scratched"])

    def test_taki_tokens(self):
        self.assertEqual(B.taki_tokens("KGDBSK"), ["KG", "DB", "SK"])
        self.assertEqual(B.taki_tokens("SKGDB"), ["SKG", "DB"])


class TestSignals(unittest.TestCase):
    def test_signal_rules(self):
        rec = {"agf_pct": 4, "win_prob": 8, "hp_resid": -2.5, "apprentice": True,
               "last_zorluk": 40, "today_zorluk": 36, "surf_starts": 4, "surf_top3": 50,
               "dist_starts": 2, "dist_top3": 100, "season_starts": 3, "season_top3": 66.7,
               "other_season_starts": 6, "other_season_top3": 33.3, "last_pos": 2,
               "trend": "Yükselişte", "style": "E", "pace": "LONE_SPEED",
               "equipment": ["KG"], "h_starts": 5, "ever_kg": False, "days_since": 21}
        s = B.compute_signals(rec)
        for k in ("model_value", "hp_light", "apprentice", "class_drop", "surface_fit", "season_fit",
                  "recent_top3", "improving", "lone_speed", "first_blinkers", "fresh"):
            self.assertIn(k, s)
        self.assertNotIn("distance_fit", s)   # 3 koşu şartı
        self.assertTrue(B.is_overlooked({"agf_rank": 5, "agf_pct": 9}))
        self.assertFalse(B.is_overlooked({"agf_rank": 2, "agf_pct": 25}))

    def test_market_probs_sum_to_one(self):
        race = [{"agf_pct": 50, "win_prob": 30}, {"agf_pct": 30, "win_prob": 50}, {"agf_pct": 20, "win_prob": 20}]
        p = B.market_probs(race, 0.9, 0.0)
        self.assertAlmostEqual(sum(p), 1.0, places=6)
        self.assertGreater(p[0], p[1])        # model ağırlığı 0: sıralamayı AGF belirler


if __name__ == "__main__":
    unittest.main()
