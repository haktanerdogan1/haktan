using Asistan.Core.Veri;

namespace Asistan.Core.Planlama;

/// <summary>
/// FM27'nin bireysel ek odakları. Oyunda tek tek özellik değil, özellik grupları vardır (MacLibero'nun FM27 listesi):
/// 16 saha oyuncusu, 4 kaleci ve herkese açık 8 odak. Hangi odağın hangi özellikleri çalıştırdığı oyunda yazılı değildir;
/// buradaki eşleştirme odak adlarından çıkarılmış bir tahmindir ve öyle gösterilir.
/// </summary>
public sealed record OdakGrubu(string Kod, string Ad, bool Kaleci, bool Herkes, string[] Ozellikler, string[] Kelimeler);

public static class OdakGruplari
{
    public static readonly IReadOnlyList<OdakGrubu> Hepsi = new OdakGrubu[]
    {
        new("ChanceConversion", "Pozisyonu gole çevirme", false, false, new[] { "Finishing", "Composure", "FirstTouch" }, new[] { "chance conversion", "gole cevirme", "pozisyonu gole", "bitiricilik" }),
        new("ChanceCreation", "Pozisyon yaratma", false, false, new[] { "Passing", "Vision", "Decisions", "Technique" }, new[] { "chance creation", "pozisyon yaratma", "firsat yaratma" }),
        new("BallRetention", "Topu saklama", false, false, new[] { "FirstTouch", "Composure", "Technique", "Balance" }, new[] { "ball retention", "topu saklama", "top saklama" }),
        new("BallProgression", "Topla ilerleme", false, false, new[] { "Dribbling", "Passing", "Vision" }, new[] { "ball progression", "topla ilerleme", "top tasima" }),
        new("Pressing", "Pres", false, false, new[] { "WorkRate", "Aggression", "Anticipation", "Stamina" }, new[] { "pressing", "pres" }),
        new("Containment", "Kontrol altında tutma", false, false, new[] { "Positioning", "Concentration", "Decisions", "Teamwork" }, new[] { "containment", "kontrol altinda", "engelleme" }),
        new("DefensivePositioning", "Savunma pozisyonu", false, false, new[] { "Positioning", "Anticipation", "Concentration", "Marking" }, new[] { "defensive positioning", "savunma pozisyon", "pozisyon alma" }),
        new("AttackingMovement", "Hücum hareketleri", false, false, new[] { "Movement", "Anticipation", "Acceleration" }, new[] { "attacking movement", "hucum hareket", "topsuz" }),
        new("FinalThird", "Son üçüncü bölge", false, false, new[] { "Technique", "Dribbling", "Flair", "Composure" }, new[] { "final third", "son ucuncu", "son 3", "hucum bolgesi" }),
        new("Shooting", "Şut", false, false, new[] { "Finishing", "LongShots", "Technique" }, new[] { "shooting", "sut" }),
        new("Passing", "Pas", false, false, new[] { "Passing", "Vision", "Technique" }, new[] { "passing", "pas" }),
        new("Crossing", "Orta", false, false, new[] { "Crossing", "Technique" }, new[] { "crossing", "orta" }),
        new("BallControl", "Top kontrolü", false, false, new[] { "FirstTouch", "Technique", "Dribbling" }, new[] { "ball control", "top kontrol", "ilk kontrol" }),
        new("Tackling", "Top kapma", false, false, new[] { "Tackling", "Aggression", "Bravery" }, new[] { "tackling", "top kapma", "mudahale" }),
        new("Marking", "Markaj", false, false, new[] { "Marking", "Concentration", "Strength" }, new[] { "marking", "markaj" }),
        new("Aerial", "Hava topları", false, false, new[] { "Heading", "JumpingReach", "Bravery" }, new[] { "aerial", "hava", "kafa" }),
        new("GkShotStopping", "Kaleci: şut kurtarma", true, false, new[] { "Reflexes", "OneOnOnes", "Handling" }, new[] { "shot stopping", "sut kurtarma", "kurtaris" }),
        new("GkHandling", "Kaleci: top tutma", true, false, new[] { "Handling", "AerialReach", "CommandOfArea" }, new[] { "gk handling", "handling", "elle kontrol", "top tutma" }),
        new("GkSweeping", "Kaleci: libero", true, false, new[] { "RushingOut", "Anticipation", "Decisions", "Acceleration" }, new[] { "sweeping", "libero", "kaleden cikma" }),
        new("GkDistribution", "Kaleci: oyun başlatma", true, false, new[] { "Kicking", "Throwing", "Passing" }, new[] { "distribution", "oyun baslatma", "dagitim" }),
        new("Quickness", "Çabukluk", false, true, new[] { "Acceleration", "Pace" }, new[] { "quickness", "cabukluk", "hiz" }),
        new("AgilityBalance", "Çeviklik ve denge", false, true, new[] { "Agility", "Balance" }, new[] { "agility", "ceviklik", "denge" }),
        new("Resistance", "Direnç", false, true, new[] { "Strength", "NaturalFitness" }, new[] { "resistance", "direnc", "guc" }),
        new("Stamina", "Dayanıklılık", false, true, new[] { "Stamina", "NaturalFitness" }, new[] { "stamina", "dayaniklilik", "kondisyon" }),
        new("FreeKicks", "Serbest vuruş", false, true, new[] { "FreeKicks" }, new[] { "free kick", "serbest vurus" }),
        new("Corners", "Korner", false, true, new[] { "Corners" }, new[] { "corner", "korner" }),
        new("Penalties", "Penaltı", false, true, new[] { "PenaltyTaking" }, new[] { "penalty", "penalti" }),
        new("LongThrows", "Uzun taç", false, true, new[] { "LongThrows" }, new[] { "long throw", "uzun tac" })
    };

    /// <summary>Oyunun (dilindeki) odak adını gruba bağlar. Önce çok kelimeli ifadeler, sonra tek kelimeler denenir.</summary>
    public static OdakGrubu? Bul(string odakAdi)
    {
        var ad = Seanslar.Kucuk(odakAdi);
        return Hepsi
            .SelectMany(g => g.Kelimeler.Select(k => (g, k: Seanslar.Kucuk(k))))
            .Where(x => KelimeVar(ad, x.k))
            .OrderByDescending(x => x.k.Length)
            .Select(x => x.g)
            .FirstOrDefault();
    }

    private static bool KelimeVar(string metin, string kelime)
    {
        var i = metin.IndexOf(kelime, StringComparison.Ordinal);

        while (i >= 0)
        {
            var once = i == 0 || !char.IsLetter(metin[i - 1]);
            var sonra = i + kelime.Length >= metin.Length || !char.IsLetter(metin[i + kelime.Length]);

            if (once && (sonra || kelime.Length >= 5))
            {
                return true;
            }

            i = metin.IndexOf(kelime, i + 1, StringComparison.Ordinal);
        }

        return false;
    }
}

public sealed record OzellikDegeri(string Anahtar, string Ad, double? Deger, bool Oncelikli, bool Zayif);

public sealed record RolDegerlendirmesi(RolEvresi Evre, string? RolAdi, RolTanimi? Rol, IReadOnlyList<OzellikDegeri> Ozellikler, double? Uyum, IReadOnlyList<(RolTanimi Rol, double Uyum)> Alternatifler)
{
    public bool Tanindi => Rol is not null;
}

public sealed record OdakOnerisi(Secenek Secenek, OdakGrubu Grup, double Puan, IReadOnlyList<string> Hedefler, string Gerekce);

public sealed record GelisimRaporu(
    Oyuncu Oyuncu,
    RolDegerlendirmesi TopaSahipken,
    RolDegerlendirmesi TopRakipteyken,
    IReadOnlyList<OzellikDegeri> EnZayif,
    IReadOnlyList<OdakOnerisi> Odaklar,
    string GelisimEvresi,
    IReadOnlyList<string> Karakteristikler,
    IReadOnlyList<string> Notlar);

/// <summary>
/// Oynanan taktiğe göre bireysel gelişim: oyuncunun topa sahipken ve top rakipteyken rolünün anahtar özelliklerini, oyuncunun bu
/// özelliklerdeki değerlerini, en zayıf halkayı, onu en çok kapatan FM27 ek odağını, aynı mevkide daha uygun rolleri ve yaşa göre
/// gelişim penceresini çıkarır. Rol değişikliği ve karakteristikler MCPFM ile yazılamadığı için yalnızca önerilir.
/// </summary>
public static class BireyselGelisim
{
    public static IReadOnlyList<GelisimRaporu> Olustur(Anlik a) => a.Oyuncular.Select(o => Rapor(o, a.OdakSecenekleri)).ToList();

    public static GelisimRaporu Rapor(Oyuncu o, IReadOnlyList<Secenek> odakSecenekleri)
    {
        var notlar = new List<string>();
        var ip = Degerlendir(o, o.RolAnahtari ?? o.RolAdi, RolEvresi.TopaSahipken);
        var oop = Degerlendir(o, o.OopRol, RolEvresi.TopRakipteyken);

        if (o.Ozellikler.Count == 0)
        {
            notlar.Add("Özellikler okunamadı (ya da gözlemci bilgisi yetersiz); gelişim önerisi yapılamadı.");
        }

        if (!ip.Tanindi)
        {
            notlar.Add($"Topa sahipken rolü ({o.RolAdi ?? o.RolAnahtari ?? "bilinmiyor"}) rol tablosunda bulunamadı.");
        }

        if (!oop.Tanindi)
        {
            notlar.Add(o.OopRol is null
                ? "Top rakipteyken rolü okunamadı; değerlendirme yalnızca topa sahipken rolüne göre yapıldı."
                : $"Top rakipteyken rolü ({o.OopRol}) rol tablosunda bulunamadı.");
        }

        // En zayıf halka: iki evrenin anahtar özellikleri birlikte, öncelikliler iki kat ağırlıkla.
        var agirlik = new Dictionary<string, double>();
        var deger = new Dictionary<string, double>();

        foreach (var oz in ip.Ozellikler.Concat(oop.Ozellikler).Where(x => x.Deger is not null))
        {
            agirlik[oz.Anahtar] = agirlik.GetValueOrDefault(oz.Anahtar) + (oz.Oncelikli ? 2 : 1);
            deger[oz.Anahtar] = oz.Deger!.Value;
        }

        var ortalama = deger.Count > 0 ? deger.Values.Average() : 0;
        double Eksik(string k) => deger.TryGetValue(k, out var v) ? Math.Max(0, 15 - v) * 0.5 + Math.Max(0, ortalama - v) : 0;

        var enZayif = ip.Ozellikler.Concat(oop.Ozellikler)
            .Where(x => x.Deger is not null)
            .GroupBy(x => x.Anahtar).Select(g => g.First())
            .OrderByDescending(x => agirlik[x.Anahtar] * Eksik(x.Anahtar))
            .Take(3)
            .Where(x => Eksik(x.Anahtar) > 0)
            .ToList();

        // Odak puanı: odağın çalıştırdığı anahtar özelliklerdeki ağırlıklı eksik toplamı (geniş odaklar hafifçe cezalandırılır).
        var kaleci = (o.Pozisyon ?? "").Contains("GK", StringComparison.OrdinalIgnoreCase) || ip.Rol?.MevkiGruplari.Contains("Goalkeeper") == true;
        var odaklar = new List<OdakOnerisi>();

        foreach (var sec in odakSecenekleri.Where(s => s.Id >= 0))
        {
            if (OdakGruplari.Bul(sec.Ad) is not { } grup || (grup.Kaleci && !kaleci) || (!grup.Kaleci && !grup.Herkes && kaleci))
            {
                continue;
            }

            var hedefler = grup.Ozellikler.Where(agirlik.ContainsKey).ToList();
            var puan = hedefler.Sum(k => agirlik[k] * Eksik(k)) / Math.Sqrt(grup.Ozellikler.Length);

            if (puan <= 0)
            {
                continue;
            }

            var hedefMetni = string.Join(", ", hedefler.OrderByDescending(k => agirlik[k] * Eksik(k)).Select(k => $"{OdakOzellikleri.TurkceAd(k)} {deger[k]:0}"));
            odaklar.Add(new OdakOnerisi(sec, grup, puan, hedefler, $"Rolün zayıf anahtar özelliklerini çalıştırır: {hedefMetni}."));
        }

        odaklar = odaklar.OrderByDescending(x => x.Puan).ThenBy(x => x.Secenek.Id).Take(3).ToList();

        var yas = o.Yas.VarMi ? o.Yas.Deger : (int?)null;
        var evre = yas switch
        {
            null => "Yaş okunamadı.",
            <= 19 => "En hızlı gelişim dönemi: ek odak ve uygun yoğunlukla özellikler belirgin artar; maç süresi de gelişimi besler.",
            <= 23 => "Gelişim sürüyor: rolün en zayıf anahtar özelliğine odaklanmak en çok karşılığı verir.",
            <= 28 => "Olgunluk: büyük sıçrama beklenmez; rolün zayıf halkasını kapatmak ve rol uyumunu artırmak öne çıkar.",
            <= 31 => "Zirve sonrası: fiziksel özellikleri korumak (Dayanıklılık, Direnç) ve rolü yaşına uygun seçmek önemli.",
            _ => "Koruma dönemi: fiziksel düşüş başlar; zihinsel ağırlıklı, daha az koşu isteyen roller tercih edilir."
        };

        if (ip.Uyum is { } u1 && oop.Uyum is { } u2 && Math.Abs(u1 - u2) >= 2)
        {
            notlar.Add(u1 < u2
                ? $"Topa sahipken rolüne uyumu ({u1:0.0}) top rakipteyken rolüne göre ({u2:0.0}) belirgin düşük."
                : $"Top rakipteyken rolüne uyumu ({u2:0.0}) topa sahipken rolüne göre ({u1:0.0}) belirgin düşük.");
        }

        return new GelisimRaporu(o, ip, oop, enZayif, odaklar, evre, Karakteristik(ip.Rol, oop.Rol), notlar);
    }

    private static RolDegerlendirmesi Degerlendir(Oyuncu o, string? rolAdi, RolEvresi evre)
    {
        var rol = RolTablosu.Bul(rolAdi, evre);

        if (rol is null)
        {
            return new RolDegerlendirmesi(evre, rolAdi, null, Array.Empty<OzellikDegeri>(), null, Array.Empty<(RolTanimi, double)>());
        }

        var oz = rol.Oncelikli.Select(k => (k, true)).Concat(rol.Destekleyici.Select(k => (k, false)))
            .Select(x => new OzellikDegeri(x.k, OdakOzellikleri.TurkceAd(x.k), o.Ozellikler.TryGetValue(x.k, out var p) ? p.Orta : null, x.Item2, false))
            .ToList();
        var uyum = Uyum(o, rol);

        if (uyum is { } ort)
        {
            oz = oz.Select(x => x with { Zayif = x.Deger is { } d && d < ort - 1.5 }).ToList();
        }

        var alternatifler = RolTablosu.AyniMevki(rol)
            .Select(r => (Rol: r, Uyum: Uyum(o, r)))
            .Where(x => x.Uyum is not null && uyum is not null && x.Uyum > uyum + 0.7)
            .OrderByDescending(x => x.Uyum)
            .Take(2)
            .Select(x => (x.Rol, x.Uyum!.Value))
            .ToList();

        return new RolDegerlendirmesi(evre, rolAdi, rol, oz, uyum, alternatifler);
    }

    /// <summary>Rol uyumu: öncelikli özellikler iki kat ağırlıklı ortalama (1-20). Özelliklerin yarısından azı okunduysa hesaplanmaz.</summary>
    public static double? Uyum(Oyuncu o, RolTanimi rol)
    {
        var toplam = 0.0;
        var agirlik = 0.0;
        var bulunan = 0;

        foreach (var (k, w) in rol.Oncelikli.Select(k => (k, 2.0)).Concat(rol.Destekleyici.Select(k => (k, 1.0))))
        {
            if (o.Ozellikler.TryGetValue(k, out var p))
            {
                toplam += p.Orta * w;
                agirlik += w;
                bulunan++;
            }
        }

        return bulunan * 2 >= rol.Oncelikli.Length + rol.Destekleyici.Length ? toplam / agirlik : null;
    }

    // Karakteristikler (oyuncu özellikleri/traits) MCPFM ile okunup yazılamaz: role uygun olanlar öneri olarak verilir,
    // oyunda Bireysel Antrenman ekranından öğretilir. Adlar oyunun İngilizce adlarıdır.
    private static IReadOnlyList<string> Karakteristik(RolTanimi? ip, RolTanimi? oop)
    {
        var liste = new List<string>();
        var a = ip?.Anahtar ?? "";

        void Ekle(params string[] s) => liste.AddRange(s);

        if (a is "Poacher" or "CentreForward" or "SecondStriker") Ekle("Gets Into Opposition Area — ceza sahasına girer", "Places Shots — plase vurur");
        if (a is "ChannelForward" or "WideForward") Ekle("Moves Into Channels — kanallara kaçar", "Places Shots — plase vurur");
        if (a is "TargetForward") Ekle("Plays With Back To Goal — sırtı dönük oynar");
        if (a is "FalseNine" or "DeepLyingForward") Ekle("Comes Deep To Get Ball — topu almaya geriye gelir", "Plays One-Twos — duvar pası oynar");
        if (a is "InsideForward") Ekle("Cuts Inside From Both Wings — iki kanattan içe kat eder", "Places Shots — plase vurur");
        if (a is "Winger") Ekle("Hugs Line — çizgiye yapışır", "Knocks Ball Past Opponent — topu rakibin yanından geçirip koşar");
        if (a is "InsideWinger" or "PlaymakingWinger") Ekle("Cuts Inside From Both Wings — iki kanattan içe kat eder", "Tries Killer Balls Often — öldürücü pas dener");
        if (a is "AdvancedPlaymaker" or "MidfieldPlaymaker" or "DeepLyingPlaymaker" or "BoxToBoxPlaymaker" or "FreeRole") Ekle("Tries Killer Balls Often — öldürücü pas dener", "Dictates Tempo — oyunun temposunu belirler");
        if (a is "BoxToBoxMidfielder" or "ChannelMidfielder" or "AttackingMidfielder") Ekle("Arrives Late In Opposition Area — ceza sahasına geç girer");
        if (a is "WingBack" or "AdvancedWingBack" or "OverlappingCentreBack") Ekle("Gets Forward Whenever Possible — fırsat buldukça ileri çıkar");
        if (a is "AdvancedCentreBack" or "BallPlayingCentreBack") Ekle("Brings Ball Out Of Defence — topu savunmadan çıkarır");
        if (a is "NoNonsenseCentreBack") Ekle("Stays Back At All Times — her zaman geride kalır");

        var d = oop?.Anahtar ?? "";
        if (d.StartsWith("Pressing", StringComparison.Ordinal) || d.StartsWith("Stopping", StringComparison.Ordinal)) Ekle("Dives Into Tackles — kayarak müdahaleye girer (dikkatli: faul riski)");
        if (d.StartsWith("Covering", StringComparison.Ordinal) || d.StartsWith("Holding", StringComparison.Ordinal) || d.StartsWith("Screening", StringComparison.Ordinal)) Ekle("Does Not Dive Into Tackles — ayakta kalır, pozisyonunu bozmaz");
        if (d.StartsWith("Tracking", StringComparison.Ordinal)) Ekle("Marks Opponent Tightly — rakibini yakından markeler");

        return liste.Distinct().ToList();
    }
}
