using Asistan.Core.Personel;
using Xunit;

namespace Asistan.Tests;

public class AntrenorTestleri
{
    public static Antrenor K(string ad, string? gorev, int huc, int sav, int kon, int kal, int top, int dur, int tak, int tek, int mot, int oto, int kar, params string[] kat)
    {
        var a = new Antrenor { Id = ad, Ad = ad, Gorev = gorev };
        var d = new[] { huc, sav, kon, kal, top, dur, tak, tek, mot, oto, kar };
        for (var i = 0; i < d.Length; i++) a.Ozellikler[AntrenorEkibi.Ozellikler[i].Kod] = d[i];
        foreach (var k in kat) a.Kategoriler.Add(k);
        return a;
    }

    private static readonly Dictionary<string, double> Esit = AntrenorEkibi.Kategoriler.ToDictionary(k => k.Kod, _ => 1.0);

    [Fact]
    public void TekKategoride5Yildiz_IkiKategorideDuser()
    {
        var a = K("Usta", null, 20, 5, 5, 1, 5, 5, 20, 20, 20, 20, 20, "HucumTaktik");
        var tek = AntrenorEkibi.Analiz(new[] { a }, null).Kategoriler.First(k => k.Kategori.Kod == "HucumTaktik").Yildiz;
        a.Kategoriler.Add("HucumTeknik");
        var iki = AntrenorEkibi.Analiz(new[] { a }, null).Kategoriler.First(k => k.Kategori.Kod == "HucumTaktik").Yildiz;
        Assert.Equal(5, tek);
        Assert.True(iki < tek, $"tek {tek}, iki {iki}");
    }

    [Fact]
    public void EnIyiAtama_BoşaHarcananKategoriyiKaldirir()
    {
        // B savunmada A'dan iyi; A'nın savunma kutusu onun hücum gücünü bölüyor.
        var a = K("A", null, 18, 12, 8, 1, 14, 10, 16, 15, 15, 15, 15, "HucumTaktik", "SavunmaTaktik");
        var b = K("B", null, 8, 19, 8, 1, 10, 10, 17, 12, 15, 15, 15, "SavunmaTaktik");
        var en = AntrenorEkibi.EnIyiAtama(new[] { a, b }, Esit);
        Assert.DoesNotContain("SavunmaTaktik", en["A"]);
        Assert.Contains("HucumTaktik", en["A"]);
        var analiz = AntrenorEkibi.Analiz(new[] { a, b }, null);
        Assert.True(analiz.EnIyiToplam >= analiz.ToplamYildiz);
    }

    [Fact]
    public void KondisyonAntrenoruSadeceFizikseleAtanir()
    {
        var f = K("Fit", "Kondisyon antrenörü", 5, 5, 19, 1, 5, 5, 5, 5, 14, 14, 14, "Fiziksel");
        var en = AntrenorEkibi.EnIyiAtama(new[] { f }, Esit);
        Assert.All(en["Fit"], k => Assert.Equal("Fiziksel", k));
    }

    [Fact]
    public void ZayifAntrenor_DegistirUyarisi_ZayifKategori_IseAlUyarisi()
    {
        var iyi = K("Iyi", null, 17, 17, 8, 1, 17, 12, 17, 17, 16, 16, 16, "HucumTaktik", "SavunmaTaktik", "TopaSahiplikTaktik");
        var zayif = K("Zayif", null, 7, 8, 6, 1, 7, 6, 8, 7, 9, 8, 9, "HucumTeknik");
        var analiz = AntrenorEkibi.Analiz(new[] { iyi, zayif }, null);
        Assert.Contains(analiz.Uyarilar, u => u.Tur == UyariTuru.Degistir && u.AntrenorId == "Zayif");
        Assert.Contains(analiz.Uyarilar, u => u.Baslik.StartsWith("Kaleciler"));
    }

    [Fact]
    public void TopluYapistirma()
    {
        var (l, h) = AntrenorEkibi.Ice("Ad;Görev;Hücum;Savunma;Kondisyon;Kalecilik;Topa sahiplik;Duran toplar;Taktik;Teknik;Motivasyon;Otorite;Kararlılık;Kategoriler\n"
                                       + "Ali Usta;Antrenör;16;12;8;1;15;11;17;14;15;14;16;HucumTaktik TopaSahiplikTaktik\n"
                                       + "Eksik;satır");
        Assert.Single(l);
        Assert.Equal(16, l[0].Ozellik("Hucum"));
        Assert.Equal(2, l[0].Kategoriler.Count);
        Assert.Single(h);
    }
}
