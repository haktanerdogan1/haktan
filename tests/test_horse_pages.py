# -*- coding: utf-8 -*-
"""
TJK safkan sayfası ve idman ayrıştırma testleri.
Örnek HTML'ler TJK'nın gerçek sayfa yapısını (Kasım 2026) birebir taklit eder; internet gerekmez.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import server as S  # noqa: E402


def race_row(tarih, msf, pist, sira, derece, kcins, hp, siklet="56"):
    cells = [tarih, "İzmir", msf, pist, sira, derece, siklet, "KG", "A.ÇELİK", "3", "4,50", "3+İ", "2",
             kcins, "H.SUNKAR", "SAHİP", hp, "", "12", "", ""]
    return "<tr>" + "".join(f"<td>{c}</td>" for c in cells) + "</tr>"


SUMMARY = """<tbody>
<tr><td>TOPLAM</td><td>6</td><td>2</td><td>1</td><td>1</td><td>0</td><td>1</td><td>500.000 t</td></tr>
<tr><td>Çim</td><td>2</td><td>0</td><td>1</td><td>0</td><td>0</td><td>0</td><td>50.000 t</td></tr>
<tr><td>Kum</td><td>4</td><td>2</td><td>0</td><td>1</td><td>0</td><td>1</td><td>450.000 t</td></tr>
<tr><td>Sentetik</td><td>0</td><td>0</td><td>0</td><td>0</td><td>0</td><td>0</td><td>0 t</td></tr>
</tbody>"""

RACES = "<tbody>" + "".join([
    race_row("05.11.2026", "1400", "K:Normal", "1", "1.29.10", "Handikap 16", "62"),
    race_row("20.10.2026", "1600", "Ç:Yumuşak 3.7", "2", "1.40.20", "Handikap 15/Dişi", "58"),
    race_row("01.10.2026", "1400", "K:Nemli", "5", "1.30.50", "ŞARTLI 4", "56"),
    race_row("10.09.2026", "1200", "K:Normal", "", "", "ŞARTLI 4/Dişi", "56"),
    race_row("01.09.2026", "1400", "K:Normal", "1", "1.31.00", "G 3", "55"),
    race_row("01.08.2026", "1600", "Ç:Biraz Yumuşak 3.4", "3", "1.41.00", "KV-6/Dişi", "52"),
]) + "</tbody>"


class TestLongCareer(unittest.TestCase):
    """TJK varsayılan sayfada en çok 50 koşu gösterir; kalanlar yıl filtresiyle tamamlanmalı."""

    def test_year_pages_are_merged(self):
        summary = """<tbody><tr><td>TOPLAM</td><td>4</td><td>1</td><td>0</td><td>0</td><td>0</td><td>0</td><td>1 t</td></tr>
<tr><td>2025 Yılı</td><td>2</td><td>1</td><td>0</td><td>0</td><td>0</td><td>0</td><td>1 t</td></tr>
<tr><td>2026 Yılı</td><td>2</td><td>0</td><td>0</td><td>0</td><td>0</td><td>0</td><td>0 t</td></tr></tbody>"""
        r26 = [race_row("05.11.2026", "1400", "K:Normal", "4", "1.29.10", "Handikap 16", "62"),
               race_row("01.10.2026", "1400", "K:Normal", "6", "1.30.10", "Handikap 16", "62")]
        r25 = [race_row("05.11.2025", "1400", "K:Normal", "1", "1.29.10", "ŞARTLI 3", "50"),
               race_row("01.10.2025", "1400", "K:Normal", "7", "1.30.10", "ŞARTLI 3", "48")]
        pages = {
            "": "<html>" + summary + "<tbody>" + "".join(r26) + "</tbody></html>",  # yalnızca son koşular
            "2026": "<tbody>" + "".join(r26) + "</tbody>",
            "2025": "<tbody>" + "".join(r25) + "</tbody>",
        }
        orig = S.fetch_cached
        S.fetch_cached = lambda url, ttl: pages[url.split("QueryParameter_Yil=")[1] if "Yil=" in url else ""]
        try:
            S.CACHE.clear()
            d = S.fetch_horse_bankomat_sheet("777", 57, 1400, lite=True)
        finally:
            S.fetch_cached = orig
        self.assertEqual(d["total_career_races"], 4)
        self.assertEqual(sum(b["starts"] for b in d["class_breakdown"]), 4)
        self.assertEqual(d["races"][0]["tarih"], "05.11.2026")
        self.assertEqual(d["races"][-1]["tarih"], "01.10.2025")


class TestClassify(unittest.TestCase):
    def test_classes(self):
        self.assertEqual(S.classify_race("G 3")[0], "grup3")
        self.assertEqual(S.classify_race("Grup 1")[0], "grup1")
        self.assertEqual(S.classify_race("KV-6/Dişi")[0], "kv")
        self.assertEqual(S.classify_race("ŞARTLI 4/Dişi")[0], "sartli4")
        self.assertEqual(S.classify_race("Handikap 15/Dişi")[0], "handikap15")
        self.assertEqual(S.classify_race("Maiden/DHÖW")[0], "maiden")
        self.assertEqual(S.classify_race("SATIŞ 2")[0], "satis")
        self.assertEqual(S.classify_race("ŞARTLI 19/Dişi")[1], "Şartlı 19")

    def test_zorluk_turkish_case(self):
        # 'Handikap'.upper() == 'HANDIKAP' hatası: handikaplar Maiden seviyesine düşmemeli
        self.assertEqual(S.compute_zorluk_degree("Handikap 17"), 38.0)
        self.assertEqual(S.compute_zorluk_degree("Handikap 15/Dişi"), 35.0)
        self.assertEqual(S.compute_zorluk_degree("G 3"), 45.0)
        self.assertEqual(S.compute_zorluk_degree("Maiden"), 30.5)
        self.assertGreater(S.compute_zorluk_degree("Handikap 22"), S.compute_zorluk_degree("Handikap 17"))


class TestHorseSheet(unittest.TestCase):
    def run_sheet(self, page):
        orig = S.fetch_cached
        S.fetch_cached = lambda url, ttl: page
        try:
            S.CACHE.clear()
            return S.fetch_horse_bankomat_sheet("999", today_weight=56, target_dist=1400, lite=True)
        finally:
            S.fetch_cached = orig

    def test_with_summary_table(self):
        d = self.run_sheet("<html>" + SUMMARY + RACES + "</html>")
        c = d["career_stats"]
        self.assertEqual((c["starts"], c["w1"], c["w2"], c["w3"], c["w5"]), (6, 2, 1, 1, 1))
        self.assertEqual(c["win_pct"], 33.3)
        self.assertEqual(c["top5_pct"], 83.3)
        self.assertEqual(d["surface_summary"]["Kum"]["w5"], 1)
        self.assertEqual(d["total_career_races"], 6)

    def test_without_summary_table_young_horse(self):
        d = self.run_sheet("<html>" + RACES + "</html>")
        c = d["career_stats"]
        self.assertEqual(c["starts"], 6)          # derecesiz koşu da yarış sayılır
        self.assertEqual(c["w1"], 2)
        self.assertEqual(c["win_pct"], 33.3)
        self.assertEqual(d["surface_summary"]["Çim"]["starts"], 2)

    def test_class_breakdown_per_race(self):
        d = self.run_sheet("<html>" + SUMMARY + RACES + "</html>")
        by = {b["key"]: b for b in d["class_breakdown"]}
        self.assertEqual(by["sartli4"]["starts"], 2)      # biri derecesiz
        self.assertEqual(by["sartli4"]["top5_pct"], 50.0)
        self.assertEqual(by["grup3"]["w1"], 1)
        order = [b["key"] for b in d["class_breakdown"]]
        self.assertLess(order.index("grup3"), order.index("kv"))
        self.assertLess(order.index("kv"), order.index("sartli4"))
        self.assertLess(order.index("sartli4"), order.index("handikap16"))
        self.assertLess(order.index("handikap16"), order.index("handikap15"))
        self.assertEqual(sum(b["starts"] for b in d["class_breakdown"]), 6)

    def test_hp_and_track_condition(self):
        d = self.run_sheet("<html>" + SUMMARY + RACES + "</html>")
        self.assertEqual(d["hp_history"]["current"], 62)
        self.assertEqual(d["hp_history"]["max"], 62)
        r = d["races"][1]
        self.assertEqual((r["pd"], r["penetrometre"]), ("YM", "3.7"))
        self.assertEqual(d["races"][5]["pd"], "BY")
        self.assertEqual(d["races"][3]["sira"], "-")


IDMAN_PAGE = """<table id="KosuIdmanGaloplari_table" class="tablesorter"><thead><tr>
<th>At No</th><th>At Adı</th><th>2200</th><th>2000</th><th>1800</th><th>1600</th><th>1400</th><th>1200</th><th>1000</th>
<th>800</th><th>600</th><th>400</th><th>200</th><th>İ. Tarihi</th><th>Pist</th><th>İ. Türü</th><th>İdman Hipodromu</th><th>-</th>
</tr></thead><tbody>
<tr id="secili"><td>1</td><td>LADY ECRİN</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td>0.51.40</td><td></td><td>0.26.20</td><td></td>
<td>17.11.2026</td><td>Kum</td><td>Galop</td><td>İzmir</td><td><a href="../Page/../idmanpistiDetay?1=1&amp;QueryParameter_Id=860877">Detay</a></td></tr>
<tr class="odd"><td>3</td><td>BEAUTY ALYA</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td>0.26.60</td><td></td>
<td>17.11.2026</td><td>Kum</td><td>Sprint</td><td>İzmir</td><td><a href="../Page/../idmanpistiDetay?1=1&amp;QueryParameter_Id=860869">Detay</a></td></tr>
</tbody></table>"""

DETAIL = """<body><div class="baslik">200m:</div><div class="baslik">400m:</div>
<div class="icerik"></div><div class="icerik">0.26.20</div>
<div class="baslik">Koştuğu Hipodrom:</div><div class="baslik">Pist Durumu:</div><div class="baslik">İdman Türü:</div>
<div class="baslik">Pist Türü:</div><div class="baslik">İdman Jokey,:</div><div class="baslik">İdman Tarihi:</div>
<div class="baslik">Hava Durumu:</div><div class="baslik">Zemin Durumu:</div><div class="baslik">Galop Durumu:</div>
<div class="baslik">Takı Şekil:</div><div class="baslik">Açıklama:</div>
<div class="icerik">İzmir</div><div class="icerik"></div><div class="icerik">Galop</div><div class="icerik">Kum</div>
<div class="icerik">FERDİ KABADAYI</div><div class="icerik">17.11.2026</div><div class="icerik">A&#199;IK-G&#220;NEŞLİ</div>
<div class="icerik">NORMAL</div><div class="icerik">RAHAT</div><div class="icerik"></div><div class="icerik"></div></body>"""


class TestIdman(unittest.TestCase):
    def test_race_table(self):
        rows = S.parse_race_idman_table(IDMAN_PAGE)
        self.assertEqual([r["no"] for r in rows], [1, 3])
        self.assertEqual([(x["dist"], x["time"]) for x in rows[0]["splits"]], [(800, "0.51.40"), (400, "0.26.20")])
        self.assertEqual(rows[0]["tur"], "Galop")
        self.assertEqual(rows[1]["tur"], "Sprint")
        self.assertEqual(rows[0]["detay_id"], "860877")

    def test_detail(self):
        d = S.parse_idman_detail(DETAIL)
        self.assertEqual(d["galop_durumu"], "RAHAT")
        self.assertEqual(d["idman_jokeyi"], "FERDİ KABADAYI")
        self.assertEqual(d["hava"], "AÇIK-GÜNEŞLİ")
        self.assertEqual(d["zemin"], "NORMAL")

    def test_fetch_race_idman_enriches_rows(self):
        orig_url, orig_cached = S.fetch_url, S.fetch_cached
        S.fetch_url = lambda url, **kw: IDMAN_PAGE
        S.fetch_cached = lambda url, ttl: DETAIL
        try:
            S.CACHE.clear()
            d = S.fetch_race_idman("1", "2", "19/11/2026")
        finally:
            S.fetch_url, S.fetch_cached = orig_url, orig_cached
        r1 = d["rows"][0]
        self.assertEqual(r1["days_before"], 2)
        self.assertEqual(r1["pace_200"], 12.85)
        self.assertEqual(r1["son_400_rank"], 1)
        self.assertEqual(r1["galop_durumu"], "RAHAT")


class TestProgramExtras(unittest.TestCase):
    def test_idman_code_from_city_html(self):
        html = ('<tr><td class="gunluk-GunlukYarisProgrami-AtAdi">'
                '<a href="../../Query/ConnectedPage/AtKosuBilgileri?1=1&amp;QueryParameter_AtId=115239&Era=today">LADY ECRİN</a></td>'
                '<td><a href="../idmanpisti/Kosu?KosuKodu=228640&amp;Atkodu=115239">İdm</a></td></tr>')
        ex = S.extract_horse_extras_from_html(html)
        self.assertEqual(ex["LADY ECRİN"]["idman_kosu_kodu"], "228640")
        self.assertEqual(ex["LADY ECRİN"]["at_id"], "115239")


if __name__ == "__main__":
    unittest.main()
