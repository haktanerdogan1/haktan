using Asistan.Core.Planlama;
using Asistan.Core.Veri;
using Xunit;

namespace Asistan.Tests;

public class BirimTestleri
{
    [Theory]
    [InlineData("49", 49, 49)]
    [InlineData("45-52", 45, 52)]
    [InlineData(" 52 - 45 ", 45, 52)]
    public void Puan_Cozulur(string metin, double alt, double ust)
    {
        var p = Puan.Coz(metin)!;
        Assert.Equal(alt, p.Alt);
        Assert.Equal(ust, p.Ust);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    public void Puan_OkunamayanNullKalir_SifirOlmaz(string? metin) => Assert.Null(Puan.Coz(metin));

    [Theory]
    [InlineData("Pres Yoğunluğu", "", SeansAmaci.Taktik, TaktikAlan.PresYogunlugu)]
    [InlineData("PRES TUZAĞI", "", SeansAmaci.Taktik, TaktikAlan.PresTuzagi)]
    [InlineData("Baskı Hattı", "", SeansAmaci.Taktik, TaktikAlan.BaskiHatti)]
    [InlineData("Pressing Intensity", "", SeansAmaci.Taktik, TaktikAlan.PresYogunlugu)]
    [InlineData("Line of Engagement", "", SeansAmaci.Taktik, TaktikAlan.BaskiHatti)]
    [InlineData("Out of Possession Shape", "", SeansAmaci.Taktik, TaktikAlan.TopRakipteyken)]
    [InlineData("Topa Sahipken Diziliş", "", SeansAmaci.Taktik, TaktikAlan.TopaSahipkenDizilis)]
    [InlineData("Oyun Kurma", "", SeansAmaci.Taktik, TaktikAlan.OyunKurma)]
    [InlineData("Rejenerasyon", "", SeansAmaci.Toparlanma, null)]
    [InlineData("Recovery", "", SeansAmaci.Toparlanma, null)]
    [InlineData("Dinlenme", "", SeansAmaci.Dinlenme, null)]
    [InlineData("Rest", "", SeansAmaci.Dinlenme, null)]
    [InlineData("Maç Önizleme", "", SeansAmaci.MacHazirligi, null)]
    [InlineData("Match Practice", "", SeansAmaci.MacHazirligi, null)]
    [InlineData("Takım Kaynaşması", "", SeansAmaci.Sosyal, null)]
    [InlineData("Duran Toplar – Atak", "", SeansAmaci.DuranTop, null)]
    [InlineData("Dayanıklılık", "", SeansAmaci.Fiziksel, null)]
    [InlineData("Bilinmeyen İsim", "", SeansAmaci.Bilinmiyor, null)]
    public void Seanslar_Siniflandirilir(string ad, string kategori, SeansAmaci amac, TaktikAlan? alan)
    {
        var (a, l) = Seanslar.Siniflandir(ad, kategori);
        Assert.Equal(amac, a);
        Assert.Equal(alan, l);
    }

    [Fact]
    public void Rest_KelimesiBaskaKelimeIcindeEslesmez() => Assert.NotEqual(SeansAmaci.Dinlenme, Seanslar.Siniflandir("Interest Drill", "").Amac);

    [Theory]
    [InlineData("Pas", "Passing")]
    [InlineData("Topsuz Alan", "Movement")]
    [InlineData("Bitiricilik", "Finishing")]
    [InlineData("Off The Ball", "Movement")]
    public void Odak_OzellikEslesir(string odak, string ozellik) => Assert.Contains(ozellik, OdakOzellikleri.Ozellikler(odak));

    [Fact]
    public void Odak_PasKelimesiPasifIcindeEslesmez() => Assert.DoesNotContain("Passing", OdakOzellikleri.Ozellikler("Pasif savunma"));

    [Theory]
    [InlineData("CentreForward", "Finishing")]
    [InlineData("InvertedWingBack", "Tackling")]
    [InlineData("BallPlayingDefender", "Marking")]
    [InlineData("SweeperKeeper", "Reflexes")]
    public void Rol_AnahtarOzellikler(string rol, string ozellik) => Assert.Contains(ozellik, RolOzellikleri.Anahtar(rol));

    [Fact]
    public void Yogunluk_SeviyeleriAdVeKimlikleTaninir()
    {
        var s = BireyselPlanlayici.Seviyeler(new[] { new Secenek(-1, "Auto"), new Secenek(0, "Half"), new Secenek(1, "Average"), new Secenek(2, "Double"), new Secenek(3, "Rest 1 week") });
        Assert.Equal(4, s.Count);
        Assert.Equal(2, s[BireyselPlanlayici.Seviye.Yuksek].Id);
    }

    [Theory]
    [InlineData("CentreForward", RolEvresi.TopaSahipken, "Centre Forward")]
    [InlineData("Ball Playing Centre-Back", RolEvresi.TopaSahipken, "Ball-Playing Centre-Back")]
    [InlineData("Pressing Full-Back", RolEvresi.TopRakipteyken, "Pressing Full-Back")]
    [InlineData("SweeperKeeper", RolEvresi.TopRakipteyken, "Sweeper Keeper")]
    public void RolTablosu_Bulur(string ad, RolEvresi evre, string beklenen) => Assert.Equal(beklenen, RolTablosu.Bul(ad, evre)?.Ad);

    [Fact]
    public void RolTablosu_FM27RolleriniKapsar()
    {
        Assert.True(RolTablosu.Hepsi.Count(r => r.Evre == RolEvresi.TopRakipteyken) >= 38);
        Assert.Contains(RolTablosu.Hepsi, r => r.Anahtar == "PressingFullBack" && r.Oncelikli.Contains("WorkRate"));
    }

    [Theory]
    [InlineData("Chance Conversion", "ChanceConversion")]
    [InlineData("Ball Retention", "BallRetention")]
    [InlineData("Agility and Balance", "AgilityBalance")]
    [InlineData("GK Shot Stopping", "GkShotStopping")]
    [InlineData("Defensive Positioning", "DefensivePositioning")]
    [InlineData("Free Kick Taking", "FreeKicks")]
    public void OdakGruplari_FM27Adlari(string ad, string kod) => Assert.Equal(kod, OdakGruplari.Bul(ad)?.Kod);

    [Fact]
    public void TalimatEslestirme_BenzerAdlarKarismaz()
    {
        var zaman = DateTimeOffset.Now;
        var t = Asistan.Core.Veri.Okuma<Asistan.Core.Veri.AktifTaktik>.Var(new Asistan.Core.Veri.AktifTaktik("T", 1, "4-2-3-1", "Control", "Balanced",
            new Dictionary<string, string?> { ["CrossEngagement"] = "CrossInvite", ["PressingTrap"] = "PressingTrapNone" }), "test", zaman);
        var (onemHat, aciklamaHat) = Asistan.Core.Analiz.TaktikAnalizi.Onem(TaktikAlan.BaskiHatti, t);
        var (_, aciklamaPres) = Asistan.Core.Analiz.TaktikAnalizi.Onem(TaktikAlan.PresYogunlugu, t);
        var (onemTuzak, aciklamaTuzak) = Asistan.Core.Analiz.TaktikAnalizi.Onem(TaktikAlan.PresTuzagi, t);
        Assert.DoesNotContain("CrossEngagement", aciklamaHat);
        Assert.Equal(1.0, onemHat);
        Assert.DoesNotContain("PressingTrap", aciklamaPres);
        Assert.Contains("PressingTrap", aciklamaTuzak);
        Assert.True(onemTuzak < 1, "tuzak kapalıysa önem düşer");
    }
}
