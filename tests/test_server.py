# -*- coding: utf-8 -*-
"""
Analiz motoru testleri — internet bağlantısı gerektirmez, sentetik TJK CSV'si kullanır.
Çalıştırma (proje klasöründe):  python -m unittest discover -s tests -v
"""
import os
import random
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import server as S  # noqa: E402

HEADER = "At No;At İsmi;Yaş;Baba;Anne;Kilo;Jokey;Sahip;Antrenör;St;AGF;HP;Son 6 Yarış;KGS;s20;En İyi D."


def make_csv(n_races=8, n_horses=10, altili_starts=(3,), with_agf=True, seed=1):
    rnd = random.Random(seed)
    lines = ["İzmir;(1. Yarış Günü);19/11/2026"]
    for r in range(1, n_races + 1):
        dist = [1200, 1400, 1600, 1900, 2200][r % 5]
        lines.append(f"{r}. Kosu :   {14 + r % 9}.00;HANDİKAP 15; 3 Yaşlı İngilizler; 57.00kg; {dist}m; Kum;;;;Rekor Derece :1.24.15")
        lines.append("1.)250.000 t;2.)100.000 t;3.)50.000 t")
        lines.append(HEADER)
        for n in range(1, n_horses + 1):
            eq = rnd.choice(["", "KG", "DB", "SK", "KG DB"])
            son6 = "".join(rnd.choice("KÇ") + str(rnd.randint(0, 9)) for _ in range(6))
            agf = f"%{rnd.randint(1, 30)}({n})" if with_agf else ""
            lines.append(
                f"{n};AT{r}{n} {eq};{rnd.choice(['3y d e', '2y a k', '4y k d'])};BABA{n};ANNE{n};"
                f"{rnd.choice([54, 56, 57, 58, 60])};{rnd.choice(['H.KARATAŞ', 'A.ÇELİK', 'X.YILMAZ AP'])};S;A;{n};"
                f"{agf};{rnd.randint(40, 90)};{son6};{rnd.randint(5, 60)};{rnd.randint(0, 15)};"
                f"1.{rnd.randint(22, 30)}.{rnd.randint(10, 99)}"
            )
        if r == 2:
            lines.append("(2) AT22 ve (5) AT25 eküridir.")
        if r in altili_starts:
            lines.append("6'LI GANYAN Bu koşudan başlar. İkili, Sıralı İkili oynanır.")
    return "\n".join(lines)


class TestParsing(unittest.TestCase):
    def setUp(self):
        self.data = S.parse_tjk_csv(make_csv())

    def test_races_and_horses(self):
        self.assertEqual(self.data["total_races"], 8)
        self.assertTrue(all(len(r["horses"]) == 10 for r in self.data["races"]))

    def test_equipment_split(self):
        name, eq = S.split_horse_name_and_equipment("LADY ECRİN KG DB SK")
        self.assertEqual(name, "LADY ECRİN")
        self.assertEqual(eq, ["KG", "DB", "SK"])

    def test_ekuri(self):
        race = self.data["races"][1]
        ek = sorted(h["no"] for h in race["horses"] if h["is_ekuri"])
        self.assertEqual(ek, [2, 5])
        self.assertIn("eküridir", race["ekuri_info"])

    def test_time_parsing_and_format(self):
        self.assertAlmostEqual(S.parse_time_to_seconds("1.24.15"), 84.15)
        self.assertAlmostEqual(S.parse_time_to_seconds("1:29.93"), 89.93)
        self.assertEqual(S.format_seconds_to_time_str(84.15), "1.24.15")
        self.assertEqual(S.format_seconds_to_time_str(119.996), "2.00.00")


class TestAltili(unittest.TestCase):
    def test_single_altili_from_footer(self):
        d = S.parse_tjk_csv(make_csv(altili_starts=(3,)))
        self.assertEqual(len(d["altili_groups"]), 1)
        g = d["altili_groups"][0]
        self.assertEqual(g["source"], "tjk")
        self.assertEqual([d["races"][i]["race_no"] for i in g["race_indices"]], [3, 4, 5, 6, 7, 8])

    def test_two_altili(self):
        d = S.parse_tjk_csv(make_csv(n_races=12, altili_starts=(1, 6)))
        self.assertEqual([g["start_race_no"] for g in d["altili_groups"]], [1, 6])

    def test_fallback_last_six(self):
        d = S.parse_tjk_csv(make_csv(altili_starts=()))
        g = d["altili_groups"][0]
        self.assertEqual(g["source"], "fallback")
        self.assertEqual(len(g["race_indices"]), 6)

    def test_regex_variants(self):
        self.assertTrue(S.ALTILI_RE.search("6'LI GANYAN Bu koşudan başlar"))
        self.assertTrue(S.ALTILI_RE.search("2. 6’lı Ganyan"))
        self.assertFalse(S.ALTILI_RE.search("İKİLİ GANYAN"))


class TestModel(unittest.TestCase):
    def test_probabilities_are_sane(self):
        d = S.parse_tjk_csv(make_csv())
        for r in d["races"]:
            total_win = sum(h["simulation"]["win_prob"] for h in r["horses"])
            self.assertTrue(95 <= total_win <= 106, total_win)
            for h in r["horses"]:
                sim = h["simulation"]
                self.assertLessEqual(sim["win_prob"], sim["top2_prob"])
                self.assertLessEqual(sim["top2_prob"], sim["top3_prob"])

    def test_running_style_is_field_relative(self):
        d = S.parse_tjk_csv(make_csv(n_races=20, seed=7))
        for r in d["races"]:
            self.assertLessEqual(r["pace_scenario"]["counts"]["E"], 3)
            for h in r["horses"]:
                self.assertIn(h["running_style"]["code"], ("E", "P", "S"))
                self.assertTrue(h["running_style"]["factors"] or h["running_style"]["index"] == 50.0)

    def test_style_ignores_horse_name(self):
        a = S.parse_tjk_csv(make_csv(seed=3))
        csv_b = make_csv(seed=3).replace("AT", "ZZ")
        b = S.parse_tjk_csv(csv_b)
        codes = lambda d: [h["running_style"]["code"] for r in d["races"] for h in r["horses"]]
        self.assertEqual(codes(a), codes(b))

    def test_no_agf_means_no_value_flags(self):
        d = S.parse_tjk_csv(make_csv(with_agf=False))
        for r in d["races"]:
            for h in r["horses"]:
                self.assertFalse(h["simulation"]["is_value_bet"])
                self.assertFalse(h["simulation"]["is_dark_horse"])
                self.assertFalse(h["simulation"]["has_market"])

    def test_coupon_tiers_present(self):
        d = S.parse_tjk_csv(make_csv())
        for r in d["races"]:
            self.assertTrue(r["coupon_tiers"]["kucuk"])


class TestWeights(unittest.TestCase):
    def test_default_reweight_is_identity(self):
        d = S.parse_tjk_csv(make_csv())
        d2 = S.reweight_program(d, S.DEFAULT_WEIGHTS)
        for r1, r2 in zip(d["races"], d2["races"]):
            self.assertEqual([h["quant_score"] for h in r1["horses"]], [h["quant_score"] for h in r2["horses"]])

    def test_reweight_does_not_mutate_cache(self):
        d = S.parse_tjk_csv(make_csv())
        before = [h["quant_score"] for h in d["races"][0]["horses"]]
        S.reweight_program(d, {"speed": 50, "form": 5, "class": 5, "jockey": 5, "weight": 5})
        self.assertEqual(before, [h["quant_score"] for h in d["races"][0]["horses"]])

    def test_param_parsing(self):
        self.assertEqual(S.parse_weights_param("25,24,18,14,19"), S.DEFAULT_WEIGHTS)
        self.assertIsNone(S.parse_weights_param("a,b"))
        self.assertIsNone(S.parse_weights_param(""))
        self.assertEqual(S.parse_weights_param("0,0,0,0,0"), S.DEFAULT_WEIGHTS)


class TestCache(unittest.TestCase):
    def test_ttl(self):
        S.cache_set("_t", 1)
        self.assertEqual(S.cache_get("_t", 60), 1)
        self.assertIsNone(S.cache_get("_t", -1))
        self.assertIsNone(S.cache_get("_t", 60))


if __name__ == "__main__":
    unittest.main()


FOREIGN_HEADER = "At No;At İsmi;Yaş;Orijin(Baba);Orijin(Anne);Kilo;Jokey Adı;Sahip Adı;Antrenör Adı;St;AGF;H;Son 6 Yarış"


class TestRealWorldFormats(unittest.TestCase):
    """Kasım 2026 canlı TJK bültenlerinde görülen biçimler."""

    def foreign_csv(self):
        lines = ["Lingfield Birleşik Krallık;(0. Yarış Günü);19/11/2026"]
        for r, surf in [(1, "Sentetik"), (2, "TAPETA"), (3, "Çim")]:
            lines += [f"{r}. Kosu {14 + r}.48;Handikap; 2 Yaşlı İngilizler; kg; 1400m; {surf};;;;Rekor Derece :",
                      "İkramiye;;;;;Yetiştirici Primi;;;;", "1.)4.972 £;2.)2.333 £;", FOREIGN_HEADER]
            for n in range(1, 6):
                lines.append(f"{n};HORSE{r}{n} (GB);2y a d;SIRE (AUS);DAM (IRE);61;D EGAN;OWNER;TRAINER;{n};%{n * 3}.5({n})  %0(0);83;537211;40")
        return "\n".join(lines)

    def test_colonless_race_headers(self):
        d = S.parse_tjk_csv(self.foreign_csv())
        self.assertEqual(d["total_races"], 3)
        self.assertEqual([len(r["horses"]) for r in d["races"]], [5, 5, 5])
        self.assertEqual(d["races"][0]["time"], "15:48")

    def test_surface_aliases(self):
        d = S.parse_tjk_csv(self.foreign_csv())
        self.assertEqual([r["surface"] for r in d["races"]], ["Sentetik", "Sentetik (Tapeta)", "Çim"])

    def test_digit_only_son6(self):
        d = S.parse_tjk_csv(self.foreign_csv())
        h = d["races"][0]["horses"][0]
        self.assertEqual([it["pos"] for it in h["form_analysis"]["items"]], [5, 3, 7, 2, 1, 1])
        self.assertEqual(h["form_analysis"]["surface_win_bonus"], 0.0)
        self.assertEqual(h["agf_pct"], 3.5)

    def test_scratched_horses_excluded(self):
        csv = make_csv(n_races=1).replace("1;AT11", "1;AT11 (Koşmaz)", 1)
        d = S.parse_tjk_csv(csv)
        race = d["races"][0]
        self.assertEqual([x["no"] for x in race["scratched"]], [1])
        self.assertNotIn(1, [h["no"] for h in race["horses"]])
        self.assertAlmostEqual(sum(h["simulation"]["win_prob"] for h in race["horses"]), 100, delta=6)

    def test_multiple_ekuri_groups(self):
        race = {"bet_info": "", "ekuri_info": "", "is_altili_start": False, "ekuri_groups": [],
                "horses": [{"no": i, "is_ekuri": False} for i in range(1, 15)]}
        S.handle_race_footer(race, "[(1)A (CHI),(6)B (CHI),] eküridir.[(2)C (CHI)],(11)D (CHI)],(14)E (CHI)],] eküridir."
                                   "[(4)F (CHI)],(9)G (CHI)],] eküridir.7'Lİ PLASE;2. 6'LI GANYAN Bu koşudan başlar")
        self.assertEqual(race["ekuri_groups"], [[1, 6], [2, 11, 14], [4, 9]])
        self.assertTrue(race["is_altili_start"])
        self.assertEqual(sorted(h["no"] for h in race["horses"] if h["is_ekuri"]), [1, 2, 4, 6, 9, 11, 14])

    def test_turkey_date(self):
        self.assertEqual(S.tr_now().utcoffset().total_seconds(), 3 * 3600)
