using System.Globalization;
using System.Text;

namespace Asistan.Core.Veri;

/// <summary>Oyunun taktiksel anlayış alanları (MCPFM tacticalUnderstanding alt boyutları).</summary>
public enum TaktikAlan
{
    PasStili,
    OyunKurma,
    FirsatYaratma,
    TopaSahipkenDizilis,
    BaskiHatti,
    PresYogunlugu,
    PresTuzagi,
    TopRakipteyken
}

public enum SeansAmaci
{
    Bilinmiyor,
    Mac,
    MacHazirligi,
    Toparlanma,
    Dinlenme,
    Taktik,
    Teknik,
    Fiziksel,
    DuranTop,
    Sosyal
}

/// <summary>Tahmini yük sınıfı. Oyunun okunan kesin yük değeriyle karıştırılmaz; arayüzde "tahmini" diye gösterilir.</summary>
public enum YukSinifi { Yok = 0, Dusuk = 1, Orta = 2, Yuksek = 3 }

public sealed record SeansTuru(int Id, string Ad, int KategoriId, string KategoriAdi, SeansAmaci Amac, TaktikAlan? Alan, bool KullaniciEslestirdi)
{
    public YukSinifi Yuk => Seanslar.Yuk(Amac);
    /// <summary>Oyunda rejenerasyon/toparlanma seansları sağlık ekibine (fizyoterapist) dayanır.</summary>
    public bool FizyoterapistGerekir => Amac == SeansAmaci.Toparlanma;
}

public static class Seanslar
{
    public static string AlanAdi(TaktikAlan alan) => alan switch
    {
        TaktikAlan.PasStili => "Pas stili",
        TaktikAlan.OyunKurma => "Oyun kurma",
        TaktikAlan.FirsatYaratma => "Fırsat yaratma",
        TaktikAlan.TopaSahipkenDizilis => "Topa sahipken diziliş",
        TaktikAlan.BaskiHatti => "Baskı hattı",
        TaktikAlan.PresYogunlugu => "Pres yoğunluğu",
        TaktikAlan.PresTuzagi => "Pres tuzağı",
        _ => "Top rakipteyken diziliş"
    };

    public static string AmacAdi(SeansAmaci amac) => amac switch
    {
        SeansAmaci.Mac => "Maç",
        SeansAmaci.MacHazirligi => "Maç hazırlığı",
        SeansAmaci.Toparlanma => "Toparlanma",
        SeansAmaci.Dinlenme => "Dinlenme",
        SeansAmaci.Taktik => "Taktik",
        SeansAmaci.Teknik => "Teknik gelişim",
        SeansAmaci.Fiziksel => "Fiziksel",
        SeansAmaci.DuranTop => "Duran top",
        SeansAmaci.Sosyal => "Takım kaynaşması",
        _ => "Sınıflandırılmamış"
    };

    public static YukSinifi Yuk(SeansAmaci amac) => amac switch
    {
        SeansAmaci.Dinlenme or SeansAmaci.Mac or SeansAmaci.Bilinmiyor => YukSinifi.Yok,
        SeansAmaci.Toparlanma or SeansAmaci.MacHazirligi or SeansAmaci.Sosyal or SeansAmaci.DuranTop => YukSinifi.Dusuk,
        SeansAmaci.Taktik or SeansAmaci.Teknik => YukSinifi.Orta,
        _ => YukSinifi.Yuksek
    };

    // Seans adları oyunun dilinde gelir. İlk eşleştirme anahtar kelimeyle yapılır (Türkçe, İngilizce, Fransızca);
    // sonuç seans kimliğiyle kaydedilir ve kullanıcı panelden düzeltebilir. Eşleşmeyen seans planlamada kullanılmaz.
    private static readonly (TaktikAlan Alan, string[] Kelimeler)[] AlanKelimeleri =
    {
        (TaktikAlan.PresTuzagi, new[] { "pres tuzağı", "pres tuzagi", "pressing trap", "piège", "piege" }),
        (TaktikAlan.PresYogunlugu, new[] { "pres yoğunluğu", "pres yogunlugu", "pressing intensity", "intensité du pressing", "intensite du pressing" }),
        (TaktikAlan.BaskiHatti, new[] { "baskı hattı", "baski hatti", "line of engagement", "ligne d'engagement", "ligne de pressing" }),
        (TaktikAlan.TopRakipteyken, new[] { "top rakipteyken", "topsuz diziliş", "out of possession", "sans ballon", "sans le ballon" }),
        (TaktikAlan.TopaSahipkenDizilis, new[] { "topa sahipken", "toplu diziliş", "in possession", "avec ballon", "avec le ballon" }),
        (TaktikAlan.FirsatYaratma, new[] { "fırsat yaratma", "firsat yaratma", "chance creation", "création d'occasions", "creation d'occasions" }),
        (TaktikAlan.OyunKurma, new[] { "oyun kurma", "build-up", "build up", "buildup", "construction" }),
        (TaktikAlan.PasStili, new[] { "pas stili", "passing style", "style de passe" })
    };

    private static readonly (SeansAmaci Amac, string[] Kelimeler)[] AmacKelimeleri =
    {
        (SeansAmaci.Mac, new[] { "maç günü", "mac gunu", "match day", "jour de match" }),
        (SeansAmaci.MacHazirligi, new[] { "maç hazırlığı", "maç önizleme", "maç provası", "mac hazirligi", "match preview", "match practice", "match preparation", "préparation", "avant-match", "veille de match" }),
        (SeansAmaci.Toparlanma, new[] { "rejenerasyon", "toparlanma", "recovery", "regeneration", "récupération", "recuperation" }),
        (SeansAmaci.Dinlenme, new[] { "dinlenme", "izin", "rest", "repos", "no training", "antrenman yok" }),
        (SeansAmaci.DuranTop, new[] { "duran top", "set piece", "coups de pied arrêtés", "coups de pied arretes", "cpa" }),
        (SeansAmaci.Sosyal, new[] { "takım kaynaş", "kaynaşma", "team bonding", "cohésion", "cohesion", "sosyal" }),
        (SeansAmaci.Fiziksel, new[] { "fiziksel", "kondisyon", "dayanıklılık", "dayaniklilik", "physical", "endurance", "resistance", "résistance", "physique", "sürat", "quickness" }),
        (SeansAmaci.Teknik, new[] { "teknik", "technical", "technique", "atak", "savunma", "attacking", "defending", "attack", "defence", "defense", "kaleci", "goalkeeping", "şut", "shooting", "possession", "topa sahip olma" })
    };

    public static (SeansAmaci Amac, TaktikAlan? Alan) Siniflandir(string seansAdi, string kategoriAdi)
    {
        var ad = Kucuk(seansAdi);

        foreach (var (alan, kelimeler) in AlanKelimeleri)
        {
            if (kelimeler.Any(k => ad.Contains(Kucuk(k))))
            {
                return (SeansAmaci.Taktik, alan);
            }
        }

        foreach (var (amac, kelimeler) in AmacKelimeleri)
        {
            if (kelimeler.Any(k => KelimeVar(ad, k)))
            {
                return (amac, null);
            }
        }

        // Ad bir şey söylemiyorsa kategori adına bakılır (ör. "Taktik", "Fiziksel").
        var kategori = Kucuk(kategoriAdi);

        if (kategori.Contains("takti") || kategori.Contains("tacti"))
        {
            return (SeansAmaci.Taktik, null);
        }

        foreach (var (amac, kelimeler) in AmacKelimeleri)
        {
            if (kelimeler.Any(k => KelimeVar(kategori, k)))
            {
                return (amac, null);
            }
        }

        return (SeansAmaci.Bilinmiyor, null);
    }

    /// <summary>Küçük harf + aksan katlama: "BASKI HATTI", "Baskı hattı" ve "baski hatti" aynı metne iner.</summary>
    internal static string Kucuk(string metin)
    {
        var duz = (metin ?? "").Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(duz.Length);

        foreach (var c in duz)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c == '\u2019' ? '\'' : c);
            }
        }

        return sb.ToString();
    }

    // "rest" kelimesi "interest" içinde geçmesin diye kısa anahtarlar kelime sınırıyla aranır.
    private static bool KelimeVar(string metin, string kelime)
    {
        kelime = Kucuk(kelime);

        if (kelime.Length > 5)
        {
            return metin.Contains(kelime);
        }

        var i = metin.IndexOf(kelime, StringComparison.Ordinal);

        while (i >= 0)
        {
            var once = i == 0 || !char.IsLetter(metin[i - 1]);
            var sonra = i + kelime.Length >= metin.Length || !char.IsLetter(metin[i + kelime.Length]);

            if (once && sonra)
            {
                return true;
            }

            i = metin.IndexOf(kelime, i + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
