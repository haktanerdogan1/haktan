using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Asistan.Core.Veri;

/// <summary>Kariyer, takım, tarih ve taktik kimliği. Biri değişince eski analiz ve bekleyen plan geçersizleşir.</summary>
public sealed record Kimlik(string TakimAdi, string? OyunSurumu, string HaftaAnahtari, string TaktikParmakIzi, DateOnly? IlkDuzenlenebilirGun)
{
    /// <summary>Kariyerler ayrı anahtarlanır. MCPFM kariyer kimliği vermediği için takım adı kullanılır.</summary>
    public string KariyerAnahtari => Ozet(TakimAdi);

    /// <summary>Plan bu anahtarla bağlanır: takım, takvim haftaları veya taktik değişirse eşleşmez.</summary>
    public string PlanAnahtari => Ozet($"{TakimAdi}|{HaftaAnahtari}|{TaktikParmakIzi}");

    internal static string Ozet(string metin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metin)))[..12].ToLowerInvariant();
}

public sealed record Slot(int No, int SeansId, string SeansAdi, bool Duzenlenebilir);

public sealed record Gun(DateOnly Tarih, string? Mac, IReadOnlyList<Slot> Slotlar)
{
    public bool MacGunu => !string.IsNullOrWhiteSpace(Mac);
    public string TarihMetni => Tarih.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed record AlanOlcumu(TaktikAlan Alan, Okuma<Puan> Anlayis, Okuma<Puan> MacMaruziyeti, Okuma<Puan> AntrenmanKatkisi);

public sealed record Secenek(long Id, string Ad);

public sealed record Oyuncu(
    int Id,
    string Ad,
    Okuma<int> Yas,
    string? Pozisyon,
    string? RolAnahtari,
    string? RolAdi,
    Okuma<int> Kondisyon,
    Okuma<bool> Sakat,
    Secenek? Odak,
    Secenek? Yogunluk,
    bool YogunlukOtomatik,
    int? DinlenmeGunu,
    Okuma<string> IsYuku,
    bool OdakKilitli,
    bool YogunlukKilitli,
    IReadOnlyDictionary<string, Puan> Ozellikler,
    string? OopRol = null);

/// <summary>Taktiksel Verimlilik ekranındaki bir kalem. Antrenmanla düzeltilebilenler ayrıca işaretlenir.</summary>
public sealed record VerimlilikKalemi(string Baslik, string? Puan, string? Metin, bool Olumlu, bool AntrenmanlaDuzelir, string Ne);

public sealed record AktifTaktik(string Ad, int Slot, string? Bicim, string? StilAnahtari, string? MentaliteAnahtari, IReadOnlyDictionary<string, string?> Talimatlar);

/// <summary>Oyundan bir seferde okunan, normalleştirilmiş durum. Tüm analiz ve planlama bunun üzerinde çalışır.</summary>
public sealed class Anlik
{
    public required Kimlik Kimlik { get; init; }
    public required DateTimeOffset OkunmaZamani { get; init; }
    public required IReadOnlyList<Gun> Gunler { get; init; }
    public required IReadOnlyDictionary<int, SeansTuru> SeansTurleri { get; init; }
    public required IReadOnlyList<Oyuncu> Oyuncular { get; init; }
    public required IReadOnlyList<Secenek> OdakSecenekleri { get; init; }
    public required IReadOnlyList<Secenek> YogunlukSecenekleri { get; init; }
    public required Okuma<AktifTaktik> Taktik { get; init; }
    public required IReadOnlyDictionary<TaktikAlan, AlanOlcumu> Anlayis { get; init; }
    public required Okuma<Puan> GenelAnlayis { get; init; }
    public required Okuma<bool> Fizyoterapist { get; init; }
    public required IReadOnlyList<string> Uyarilar { get; init; }
    /// <summary>Oyunun taktik ekranındaki kendi tavsiyesi (ör. "Antrenmanlarda Baskı Hattı kurgularına daha fazla zaman ayırmamız gerekiyor.").</summary>
    public string? OyunTavsiyesi { get; init; }
    /// <summary>Taktiksel Verimlilik ekranının bütün kalemleri: oyuncu yeteneği, benimseme, anlayış, sonraki maç, uyarılar.</summary>
    public IReadOnlyList<VerimlilikKalemi> Verimlilik { get; init; } = Array.Empty<VerimlilikKalemi>();
    public string? GenelVerimlilik { get; init; }
    /// <summary>Oyunun tavsiyesinin olumlu mu (her şey yolunda) olduğu.</summary>
    public bool OyunTavsiyesiOlumlu { get; init; }

    public SeansTuru? Tur(int id) => SeansTurleri.TryGetValue(id, out var t) ? t : null;

    public const string KaynakAntrenman = "MCPFM get_training";
    public const string KaynakTaktik = "MCPFM get_tactics";
    public const string KaynakKadro = "MCPFM get_squad";

    public static Anlik Olustur(
        AntrenmanDto antrenman,
        Cevap<TaktiklerDto> taktikler,
        Cevap<KadroDto> kadro,
        string? surum,
        Ayarlar ayarlar,
        DateTimeOffset zaman)
    {
        var uyarilar = new List<string>();

        // ---- Seans türleri: oyunun sunduğu gerçek seçenekler; kullanıcı eşleştirmesi anahtar kelimeye üstün gelir.
        var turler = new Dictionary<int, SeansTuru>();

        foreach (var kategori in antrenman.SessionTypes)
        {
            foreach (var seans in kategori.Sessions)
            {
                var id = (int)seans.Id;
                SeansTuru tur;

                if (ayarlar.SeansEslestirmeleri.TryGetValue(id, out var elle))
                {
                    tur = new SeansTuru(id, seans.Name, kategori.Id, kategori.Name, elle.Amac, elle.Alan, true);
                }
                else
                {
                    var (amac, alan) = Seanslar.Siniflandir(seans.Name, kategori.Name);
                    tur = new SeansTuru(id, seans.Name, kategori.Id, kategori.Name, amac, alan, false);
                }

                turler[id] = tur;
            }
        }

        // ---- Takvim
        var gunler = new List<Gun>();

        foreach (var hafta in antrenman.Weeks)
        {
            foreach (var gun in hafta.Days)
            {
                if (!DateOnly.TryParseExact(gun.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tarih))
                {
                    uyarilar.Add($"Takvimde çözülemeyen tarih: {gun.Date}");
                    continue;
                }

                var slotlar = gun.Sessions
                    .OrderBy(s => s.Slot)
                    .Select(s => new Slot(s.Slot, s.Type, s.Name, s.CanEdit == true))
                    .ToList();

                // Takvimde görünüp seans listesinde olmayan türler de (ör. maç bloğu) sınıflandırılır.
                foreach (var s in gun.Sessions.Where(s => !turler.ContainsKey(s.Type)))
                {
                    var (amac, alan) = Seanslar.Siniflandir(s.Name, "");
                    turler[s.Type] = new SeansTuru(s.Type, s.Name, s.Category, "", amac, alan, false);
                }

                gunler.Add(new Gun(tarih, gun.Match, slotlar));
            }
        }

        gunler.Sort((a, b) => a.Tarih.CompareTo(b.Tarih));
        var ilkDuzenlenebilir = gunler.FirstOrDefault(g => g.Slotlar.Any(s => s.Duzenlenebilir))?.Tarih;
        var haftaAnahtari = string.Join(",", antrenman.Weeks.Select(w => w.StartDate ?? "?"));

        // ---- Kadro
        var kadroOyunculari = kadro.Basarili
            ? kadro.Veri!.Players.ToDictionary(p => p.Id)
            : new Dictionary<int, OyuncuDto>();

        if (!kadro.Basarili)
        {
            uyarilar.Add("Kadro okunamadı: kondisyon, yaş ve sakatlık bilgisi yok. Yoğunluk önerileri ihtiyatlı tutulur. (" + kadro.Hata + ")");
        }

        var oyuncular = antrenman.Players.Select(p =>
        {
            kadroOyunculari.TryGetValue(p.PlayerId, out var k);
            var kaynak = k is null ? VeriDurumu.Eksik : VeriDurumu.Mevcut;
            return new Oyuncu(
                p.PlayerId,
                p.Name,
                k?.Age is { } yas ? Okuma<int>.Var(yas, KaynakKadro, zaman, "yıl") : Okuma<int>.Yok(KaynakKadro, zaman),
                p.Position,
                p.RoleKey,
                p.RoleName,
                k?.Condition is { } kon ? Okuma<int>.Var(kon, KaynakKadro, zaman, "%") : Okuma<int>.Yok(KaynakKadro, zaman, kaynak, "%"),
                k is null ? Okuma<bool>.Yok(KaynakKadro, zaman) : Okuma<bool>.Var(k.IsInjured, KaynakKadro, zaman),
                p.Focus is null ? null : new Secenek(p.Focus.Id, p.Focus.Name),
                p.Intensity is null ? null : new Secenek(p.Intensity.Id, p.Intensity.Name),
                p.IntensityIsAutomatic == true,
                p.RestDays,
                Okuma<string>.Belki(p.Workload, KaynakAntrenman, zaman),
                p.FocusLocked == true,
                p.IntensityLocked == true,
                Ozellikleri(k),
                p.OutOfPossessionRole);
        }).ToList();

        // ---- Taktik ve taktiksel anlayış
        Okuma<AktifTaktik> taktik;
        string? tavsiye = null;
        IReadOnlyList<VerimlilikKalemi> verimlilik = Array.Empty<VerimlilikKalemi>();
        string? genelVerimlilik = null;
        var tavsiyeOlumlu = false;
        var anlayis = new Dictionary<TaktikAlan, AlanOlcumu>();
        Okuma<Puan> genel;

        if (taktikler.Basarili)
        {
            var t = taktikler.Veri!;
            var aktifSlot = t.Slots.FirstOrDefault(s => s.IsActive);
            var a = t.Active;
            taktik = a is null || aktifSlot is null
                ? Okuma<AktifTaktik>.Yok(KaynakTaktik, zaman)
                : Okuma<AktifTaktik>.Var(new AktifTaktik(aktifSlot.Name, aktifSlot.Slot, a.InPossessionFormation, a.StyleKey, a.MentalityKey, a.TeamInstructions), KaynakTaktik, zaman);

            var ta = aktifSlot?.Effectiveness?.TacticalUnderstanding;
            tavsiye = ta?.AreasAdvice?.Text;
            verimlilik = VerimlilikKalemleri(aktifSlot?.Effectiveness);
            genelVerimlilik = aktifSlot?.Effectiveness?.Overall;
            tavsiyeOlumlu = ta?.AreasAdvice?.IsPositive == true;
            genel = Okuma<Puan>.Belki(Puan.Coz(ta?.Overall), KaynakTaktik, zaman, "0-100");

            foreach (var (alan, dto) in Alanlar(ta))
            {
                anlayis[alan] = new AlanOlcumu(
                    alan,
                    Okuma<Puan>.Belki(Puan.Coz(dto?.Understanding), KaynakTaktik, zaman, "0-100"),
                    Okuma<Puan>.Belki(Puan.Coz(dto?.MatchExposure), KaynakTaktik, zaman, "0-100"),
                    Okuma<Puan>.Belki(Puan.Coz(dto?.TrainingContribution), KaynakTaktik, zaman, "0-100"));
            }

            if (ta is null)
            {
                uyarilar.Add("Aktif taktiğin taktiksel anlayış verisi okunamadı; taktik önerileri yapılmayacak.");
            }
        }
        else
        {
            taktik = Okuma<AktifTaktik>.Yok(KaynakTaktik, zaman);
            genel = Okuma<Puan>.Yok(KaynakTaktik, zaman);

            foreach (var alan in Enum.GetValues<TaktikAlan>())
            {
                var yok = Okuma<Puan>.Yok(KaynakTaktik, zaman);
                anlayis[alan] = new AlanOlcumu(alan, yok, yok, yok);
            }

            uyarilar.Add("Taktik okunamadı: " + taktikler.Hata);
        }

        var parmakIzi = taktik.VarMi
            ? Kimlik.Ozet($"{taktik.Deger!.Slot}|{taktik.Deger.Bicim}|{taktik.Deger.StilAnahtari}|{taktik.Deger.MentaliteAnahtari}|"
                          + string.Join(";", taktik.Deger.Talimatlar.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)))
            : "taktik-yok";

        // ---- Personel: MCPFM sağlık ekibini okumaz. Kullanıcının beyanı varsa o kullanılır, kaynağı öyle yazılır.
        var fizyo = ayarlar.FizyoterapistVar is { } beyan
            ? Okuma<bool>.Var(beyan, "Kullanıcı beyanı (Ayarlar)", zaman)
            : Okuma<bool>.Yok("MCPFM (personel okunmuyor)", zaman, VeriDurumu.Desteklenmiyor);

        return new Anlik
        {
            Kimlik = new Kimlik(kadro.Veri?.TeamName ?? taktikler.Veri?.TeamName ?? "?", surum, haftaAnahtari, parmakIzi, ilkDuzenlenebilir),
            OkunmaZamani = zaman,
            Gunler = gunler,
            SeansTurleri = turler,
            Oyuncular = oyuncular,
            OdakSecenekleri = antrenman.FocusOptions.Select(o => new Secenek(o.Id, o.Name)).ToList(),
            YogunlukSecenekleri = antrenman.IntensityOptions.Select(o => new Secenek(o.Id, o.Name)).ToList(),
            Taktik = taktik,
            Anlayis = anlayis,
            GenelAnlayis = genel,
            Fizyoterapist = fizyo,
            Uyarilar = uyarilar,
            OyunTavsiyesi = tavsiye,
            Verimlilik = verimlilik,
            GenelVerimlilik = genelVerimlilik,
            OyunTavsiyesiOlumlu = tavsiyeOlumlu
        };
    }

    private static IReadOnlyList<VerimlilikKalemi> VerimlilikKalemleri(EtkinlikDto? e)
    {
        var liste = new List<VerimlilikKalemi>();

        if (e is null)
        {
            return liste;
        }

        void Ekle(string baslik, string? puan, TavsiyeDto? t, bool antrenman, string ne)
        {
            if (puan is null && t?.Text is null) return;
            liste.Add(new VerimlilikKalemi(baslik, puan, t?.Text, t?.IsPositive ?? true, antrenman, ne));
        }

        var ta = e.TacticalUnderstanding;
        Ekle("Taktik anlayışı", ta?.Overall, ta?.AreasAdvice, true, "Taktik seanslarıyla ve maç oynadıkça artar.");
        Ekle("Kavrayış", ta?.Comprehension, ta?.ComprehensionAdvice, true, "Taktik seansları ve zamanla artar; karmaşık talimatlar yavaşlatır.");
        if (ta?.Complexity?.Overall is { } k)
            liste.Add(new VerimlilikKalemi("Taktik karmaşıklığı", k, null, true, false, "Talimat sayısı arttıkça anlayış yavaş gelir; antrenmanla değil talimatları sadeleştirerek düşer."));
        Ekle("Oyuncu yeteneği", e.PlayerAbility?.Overall, e.PlayerAbility?.Advice, false, "Rol uygunluğu ve oyuncu kalitesi: ek odak ve rol/pozisyon değişikliğiyle kısmen, asıl olarak kadro ile."); 
        Ekle("Oyuncu benimsemesi", e.PlayerBuyIn?.Overall, e.PlayerBuyIn?.Advice, false, "Menajere destek ve ilişkiler; antrenmanla düzelmez, takım kaynaşması kısmen yardımcı olur.");
        Ekle("Sonraki maç", e.NextMatch?.Overall, e.NextMatch?.Advice, false, "Rakibe uygunluk; maç hazırlığı seansı yardımcı olur.");

        foreach (var w in e.Warnings ?? new())
            liste.Add(new VerimlilikKalemi("Uyarı", null, w, false, false, "Oyunun taktik uyarısı."));

        return liste;
    }

    private static IEnumerable<(TaktikAlan, TaktikAlanDto?)> Alanlar(TaktikAnlayisDto? t)
    {
        yield return (TaktikAlan.PasStili, t?.PassingStyle);
        yield return (TaktikAlan.OyunKurma, t?.BuildUpStrategy);
        yield return (TaktikAlan.FirsatYaratma, t?.ChanceCreation);
        yield return (TaktikAlan.TopaSahipkenDizilis, t?.InPossessionShape);
        yield return (TaktikAlan.BaskiHatti, t?.LineOfEngagement);
        yield return (TaktikAlan.PresYogunlugu, t?.PressingIntensity);
        yield return (TaktikAlan.PresTuzagi, t?.PressingTrap);
        yield return (TaktikAlan.TopRakipteyken, t?.OutOfPossessionShape);
    }

    private static IReadOnlyDictionary<string, Puan> Ozellikleri(OyuncuDto? k)
    {
        var sonuc = new Dictionary<string, Puan>(StringComparer.OrdinalIgnoreCase);

        foreach (var kategori in k?.Attributes ?? new())
        {
            foreach (var (ad, deger) in kategori.Value)
            {
                if (Puan.Coz(deger) is { } p)
                {
                    sonuc[ad] = p;
                }
            }
        }

        return sonuc;
    }
}
