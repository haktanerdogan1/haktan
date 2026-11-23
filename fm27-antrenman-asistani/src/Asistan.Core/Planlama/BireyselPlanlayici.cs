using Asistan.Core.Veri;

namespace Asistan.Core.Planlama;

public enum OyuncuKarari
{
    /// <summary>Öneri var; kullanıcı uygula/koru/hariç tut seçebilir.</summary>
    Oneri,
    /// <summary>Değişiklik gerekmedi.</summary>
    DegisiklikYok,
    /// <summary>Oyun veya sağlık ekibi kilitlemiş; dokunulmadı.</summary>
    OyunKilidi,
    /// <summary>Kullanıcı korumaya aldı.</summary>
    KullaniciKilidi,
    /// <summary>Kullanıcı hariç tuttu.</summary>
    Haric
}

public sealed record OyuncuOnerisi(
    Oyuncu Oyuncu,
    OyuncuKarari Karar,
    Secenek? OnerilenOdak,
    Secenek? OnerilenYogunluk,
    IReadOnlyList<string> OdakGerekcesi,
    IReadOnlyList<string> YogunlukGerekcesi,
    IReadOnlyList<string> Uyarilar,
    double Guven)
{
    public bool OdakDegisiyor => OnerilenOdak is not null && OnerilenOdak.Id != (Oyuncu.Odak?.Id ?? -1);
    public bool YogunlukDegisiyor => OnerilenYogunluk is not null
                                      && (OnerilenYogunluk.Id == -1 ? !Oyuncu.YogunlukOtomatik : Oyuncu.YogunlukOtomatik || OnerilenYogunluk.Id != Oyuncu.Yogunluk?.Id);
    public bool DegisiklikVar => OdakDegisiyor || YogunlukDegisiyor;
}

/// <summary>
/// Oyuncuya özel ek odak ve yoğunluk. Takım planıyla birlikte değerlendirilir: haftanın maç sayısı ve tahmini takım yükü
/// yoğunluk kararına girer. Sağlık/oyun kilidi ve kullanıcı kararı her zaman önce gelir.
/// </summary>
public static class BireyselPlanlayici
{
    internal enum Seviye { Otomatik, Dusuk, Normal, Yuksek }

    public static IReadOnlyList<OyuncuOnerisi> Olustur(Anlik a, TakimPlani plan, Ayarlar ayar, IReadOnlyDictionary<int, DateOnly> sonOdakDegisimi)
    {
        var buHafta = plan.Haftalar.FirstOrDefault();
        var macSayisi = buHafta?.MacSayisi ?? 0;
        var yukOrani = buHafta is null || buHafta.YukSiniri == 0 ? 0 : (double)buHafta.TahminiYuk / buHafta.YukSiniri;
        var bugun = a.Kimlik.IlkDuzenlenebilirGun ?? DateOnly.FromDateTime(a.OkunmaZamani.Date);
        var seviyeler = Seviyeler(a.YogunlukSecenekleri);

        return a.Oyuncular
            .OrderBy(o => o.Ad, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), false))
            .Select(o => Oyuncu(o, a, ayar, seviyeler, macSayisi, yukOrani, bugun, sonOdakDegisimi))
            .ToList();
    }

    private static OyuncuOnerisi Oyuncu(Oyuncu o, Anlik a, Ayarlar ayar, Dictionary<Seviye, Secenek> seviyeler, int macSayisi, double yukOrani,
        DateOnly bugun, IReadOnlyDictionary<int, DateOnly> sonOdakDegisimi)
    {
        if (ayar.HaricOyuncular.Contains(o.Id))
        {
            return new OyuncuOnerisi(o, OyuncuKarari.Haric, null, null, new[] { "Önerilerden hariç tuttunuz." }, Array.Empty<string>(), Array.Empty<string>(), 1);
        }

        if (ayar.KilitliOyuncular.Contains(o.Id))
        {
            return new OyuncuOnerisi(o, OyuncuKarari.KullaniciKilidi, null, null, new[] { "Odak ve yoğunluğu korumaya aldınız." }, Array.Empty<string>(), Array.Empty<string>(), 1);
        }

        var uyarilar = new List<string>();
        var guven = 1.0;
        if (!o.Kondisyon.VarMi) guven -= 0.3;
        if (o.Ozellikler.Count == 0) guven -= 0.2;
        if (o.RolAnahtari is null) guven -= 0.2;

        var (yogunluk, yGerekce) = Yogunluk(o, ayar, seviyeler, macSayisi, yukOrani, uyarilar);
        var (odak, oGerekce) = Odak(o, a, ayar, bugun, sonOdakDegisimi, uyarilar);

        var oneri = new OyuncuOnerisi(o, OyuncuKarari.Oneri, odak, yogunluk, oGerekce, yGerekce, uyarilar, Math.Max(0.2, guven));
        var oyunKilidi = o.YogunlukKilitli && o.OdakKilitli;

        if (oyunKilidi)
        {
            return oneri with { Karar = OyuncuKarari.OyunKilidi };
        }

        return oneri.DegisiklikVar ? oneri : oneri with { Karar = OyuncuKarari.DegisiklikYok };
    }

    // ---------------- Yoğunluk ----------------

    private static (Secenek?, IReadOnlyList<string>) Yogunluk(Oyuncu o, Ayarlar ayar, Dictionary<Seviye, Secenek> seviyeler, int macSayisi, double yukOrani, List<string> uyarilar)
    {
        var g = new List<string>();

        // K06: sağlık ekibinin kilidi aşılmaz.
        if (o.YogunlukKilitli || (o.Sakat.VarMi && o.Sakat.Deger))
        {
            g.Add("Sağlık durumu nedeniyle değiştirilmedi" + (o.YogunlukKilitli ? " (yoğunluğu sağlık ekibi belirliyor)." : " (oyuncu sakat)."));
            return (null, g);
        }

        var mevcut = MevcutSeviye(o, seviyeler);
        var agirIs = o.IsYuku.VarMi && AgirIsYuku(o.IsYuku.Deger!);
        Seviye? hedef = null;

        if (!o.Kondisyon.VarMi)
        {
            if (mevcut == Seviye.Yuksek)
            {
                hedef = seviyeler.ContainsKey(Seviye.Otomatik) ? Seviye.Otomatik : Seviye.Normal;
                g.Add("Kondisyon okunamadı; yüksek yoğunluk ihtiyatlı olarak indirildi.");
            }
            else
            {
                g.Add("Kondisyon okunamadı; yoğunluk değiştirilmedi, yüksek yoğunluk önerilmez.");
            }
        }
        else
        {
            var kon = o.Kondisyon.Deger;

            if (kon < ayar.KritikKondisyonEsigi)
            {
                hedef = Seviye.Dusuk;
                g.Add($"Kondisyon %{kon}: kritik eşiğin (%{ayar.KritikKondisyonEsigi}) altında.");
            }
            else if (kon < ayar.DusukKondisyonEsigi && (macSayisi >= 2 || agirIs))
            {
                hedef = Seviye.Dusuk;
                g.Add($"Kondisyon %{kon}" + (macSayisi >= 2 ? $" ve bu hafta {macSayisi} maç var." : $" ve iş yükü {o.IsYuku.Deger}."));
            }
            else if (agirIs && mevcut == Seviye.Yuksek)
            {
                hedef = Seviye.Normal;
                g.Add($"İş yükü {o.IsYuku.Deger}: yüksek yoğunluk normale indirildi.");
            }
            else if (kon >= ayar.YuksekYogunlukKondisyonEsigi && macSayisi <= 1 && !agirIs
                     && o.Yas.VarMi && o.Yas.Deger <= ayar.YuksekYogunlukAzamiYas && mevcut != Seviye.Yuksek)
            {
                if (yukOrani >= ayar.BireyselYukTavani)
                {
                    g.Add($"Genç ve formda (%{kon}), ama takım programının tahmini yükü sınıra yakın (%{yukOrani * 100:0}); yoğunluk artırılmadı.");
                    uyarilar.Add("Takım programıyla toplam yük yüksek.");
                }
                else
                {
                    hedef = Seviye.Yuksek;
                    g.Add($"{o.Yas.Deger} yaşında, kondisyon %{kon}, bu hafta {macSayisi} maç: gelişim için yüksek yoğunluk.");
                }
            }
            else if (mevcut == Seviye.Dusuk && kon >= 90 && !agirIs)
            {
                hedef = seviyeler.ContainsKey(Seviye.Otomatik) ? Seviye.Otomatik : Seviye.Normal;
                g.Add($"Kondisyon %{kon} toparlandı; düşük yoğunluğa gerek kalmadı.");
            }
            else
            {
                g.Add($"Kondisyon %{kon}" + (o.IsYuku.VarMi ? $", iş yükü {o.IsYuku.Deger}" : "") + ": mevcut yoğunluk uygun.");
            }
        }

        if (hedef is { } h)
        {
            if (seviyeler.TryGetValue(h, out var secenek))
            {
                return (secenek, g);
            }

            g.Add("Oyunun yoğunluk seçenekleri arasında karşılığı bulunamadı; öneri yapılmadı.");
        }

        return (null, g);
    }

    private static bool AgirIsYuku(string metin)
    {
        var m = Seanslar.Kucuk(metin);
        return m.Contains("yuksek") || m.Contains("agir") || m.Contains("high") || m.Contains("heavy") || m.Contains("eleve") || m.Contains("lourd");
    }

    private static Seviye? MevcutSeviye(Oyuncu o, Dictionary<Seviye, Secenek> seviyeler)
    {
        if (o.YogunlukOtomatik)
        {
            return Seviye.Otomatik;
        }

        foreach (var (s, sec) in seviyeler)
        {
            if (sec.Id == o.Yogunluk?.Id)
            {
                return s;
            }
        }

        return null;
    }

    // MCPFM: -1 = oyuna bırak (otomatik), 0..2 yoğunluk, üstü dinlenme süreleri. Adlar dile göre değiştiği için önce ad, sonra kimlik.
    internal static Dictionary<Seviye, Secenek> Seviyeler(IReadOnlyList<Secenek> secenekler)
    {
        var s = new Dictionary<Seviye, Secenek>();

        foreach (var sec in secenekler)
        {
            var ad = Seanslar.Kucuk(sec.Ad);

            if (sec.Id == -1 || ad.Contains("otomatik") || ad.Contains("auto"))
            {
                s.TryAdd(Seviye.Otomatik, sec);
            }
            else if (sec.Id is >= 0 and <= 2)
            {
                var seviye = ad.Contains("yarim") || ad.Contains("dusuk") || ad.Contains("half") || ad.Contains("low") || ad.Contains("faible") || ad.Contains("demi") ? Seviye.Dusuk
                    : ad.Contains("cift") || ad.Contains("yuksek") || ad.Contains("double") || ad.Contains("high") || ad.Contains("elev") ? Seviye.Yuksek
                    : ad.Contains("ortalama") || ad.Contains("normal") || ad.Contains("average") || ad.Contains("moyen") ? Seviye.Normal
                    : sec.Id == 0 ? Seviye.Dusuk : sec.Id == 1 ? Seviye.Normal : Seviye.Yuksek;
                s.TryAdd(seviye, sec);
            }
        }

        return s;
    }

    // ---------------- Ek odak ----------------

    private static (Secenek?, IReadOnlyList<string>) Odak(Oyuncu o, Anlik a, Ayarlar ayar, DateOnly bugun, IReadOnlyDictionary<int, DateOnly> sonDegisim, List<string> uyarilar)
    {
        var g = new List<string>();

        if (o.OdakKilitli)
        {
            g.Add("Odağı antrenman ekibi belirliyor; oyunda kontrolü geri almadan değiştirilemez.");
            return (null, g);
        }

        if (o.Ozellikler.Count == 0)
        {
            g.Add("Özellikler okunamadı (ya da gözlemciniz bilmiyor); odak önerisi yapılmadı.");
            return (null, g);
        }

        // Oynanan taktikteki iki rol (topa sahipken + top rakipteyken) birlikte değerlendirilir; odak FM27'nin özellik grubu odaklarıdır.
        var rapor = BireyselGelisim.Rapor(o, a.OdakSecenekleri);

        if (!rapor.TopaSahipken.Tanindi && !rapor.TopRakipteyken.Tanindi)
        {
            g.Add($"Rol ({o.RolAdi ?? o.RolAnahtari ?? "bilinmiyor"}) tanınmadı; odak önerisi yapılmadı.");
            return (null, g);
        }

        foreach (var r in new[] { rapor.TopaSahipken, rapor.TopRakipteyken })
        {
            if (r.Uyum is < 9 && r.Rol is not null)
            {
                uyarilar.Add($"Rol uygunluğu düşük: {r.Rol.Ad} için anahtar özellik ortalaması {r.Uyum:0.0}/20. Ek odak tek başına yetmez; rol değişikliğini değerlendirin (otomatik yazılmaz).");
            }
        }

        if (o.Yas.VarMi && o.Yas.Deger >= 31)
        {
            g.Add($"{o.Yas.Deger} yaşında: özellik gelişimi sınırlı, odak değiştirilmedi.");
            return (null, g);
        }

        if (rapor.Odaklar.Count == 0)
        {
            g.Add("Rolün zayıf anahtar özelliklerini çalıştıran bir odak seçeneği bulunamadı.");
            return (null, g);
        }

        var enIyi = rapor.Odaklar[0];

        if (o.Odak?.Id == enIyi.Secenek.Id)
        {
            g.Add($"Mevcut odak ({enIyi.Secenek.Ad}) rolün en zayıf halkasını çalıştırıyor. {enIyi.Gerekce}");
            return (null, g);
        }

        var mevcut = rapor.Odaklar.FirstOrDefault(x => x.Secenek.Id == o.Odak?.Id);

        if (mevcut is not null && mevcut.Puan >= enIyi.Puan * 0.75)
        {
            g.Add($"Mevcut odak ({mevcut.Secenek.Ad}) hâlâ rol için anlamlı; daha iyisiyle fark değişiklik eşiğinin altında.");
            return (null, g);
        }

        if (sonDegisim.TryGetValue(o.Id, out var son) && bugun.DayNumber - son.DayNumber < ayar.OdakAsgariGun)
        {
            g.Add($"Odak {bugun.DayNumber - son.DayNumber} gün önce değişti; istikrar için {ayar.OdakAsgariGun} gün dolmadan yeniden değiştirilmez.");
            return (null, g);
        }

        var roller = string.Join(" + ", new[] { rapor.TopaSahipken.Rol?.Ad, rapor.TopRakipteyken.Rol?.Ad }.Where(x => x is not null));
        g.Add($"{roller}: {enIyi.Gerekce}");
        g.Add($"Önerilen ek odak: {enIyi.Secenek.Ad}.");
        return (enIyi.Secenek, g);
    }

    private static string OzellikAdi(string anahtar) => OdakOzellikleri.TurkceAd(anahtar);
}
