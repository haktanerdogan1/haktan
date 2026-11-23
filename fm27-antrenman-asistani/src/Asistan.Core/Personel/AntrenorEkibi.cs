using System.Globalization;
using Asistan.Core.Planlama;
using Asistan.Core.Veri;

namespace Asistan.Core.Personel;

/// <summary>Bir antrenör: antrenörlük özellikleri (1-20) ve atandığı antrenman kategorileri.</summary>
public sealed class Antrenor
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Ad { get; set; } = "";
    public string? Gorev { get; set; }
    /// <summary>Özellik kodu (Hucum, Savunma, Kondisyon, Kaleci, TopaSahiplik, DuranTop, Taktik, Teknik, Motivasyon, Otorite, Kararlilik) → 1-20.</summary>
    public Dictionary<string, int> Ozellikler { get; set; } = new();
    public HashSet<string> Kategoriler { get; set; } = new();

    public int Ozellik(string kod) => Ozellikler.TryGetValue(kod, out var v) ? v : 0;

    /// <summary>Kondisyon ve kaleci antrenörleri oyunda yalnızca kendi kategorisine atanır.</summary>
    public string? Kisit
    {
        get
        {
            var g = Seanslar.Kucuk(Gorev ?? "");
            if (g.Contains("kondisyon") || g.Contains("fitness")) return "Fiziksel";
            if (g.Contains("kaleci") || g.Contains("goalkeep")) return "Kaleciler";
            return null;
        }
    }
}

public sealed record AntrenorOzelligi(string Kod, string Ad);

/// <summary>FM27'nin 9 antrenman kategorisi ve her birini belirleyen antrenörlük özellikleri.</summary>
public sealed record KocKategorisi(string Kod, string Ad, string[] Ozellikler);

public sealed record KategoriDurumu(KocKategorisi Kategori, double Yildiz, Antrenor? Lider, double EnIyiYildiz, Antrenor? EnIyiLider, int ProgramSeansi);

public enum UyariTuru { Degistir, AtamayiKaldir, YukuAzalt, IseAl, AtamayiIyilestir, Bilgi }

public sealed record KocUyarisi(UyariTuru Tur, string Baslik, string Metin, string? AntrenorId = null, string? Kategori = null);

public sealed record EkipAnalizi(
    IReadOnlyList<KategoriDurumu> Kategoriler,
    IReadOnlyDictionary<string, HashSet<string>> EnIyiAtama,
    double ToplamYildiz,
    double EnIyiToplam,
    IReadOnlyList<KocUyarisi> Uyarilar,
    IReadOnlyDictionary<string, (string EnIyiKategori, double Puan)> AntrenorOzeti);

/// <summary>
/// Antrenör ekibi değerlendirmesi. Kurallar FM27 topluluk testlerine dayanır (MacLibero FM27 antrenör atama rehberi):
/// kategori yeteneği = o kategorinin antrenörlük özellikleri + Motivasyon, Otorite, Kararlılık; çok kategoriye dağılan antrenör
/// her birinde zayıflar; yıldızı yalnızca kategorinin en iyi antrenörü belirler. Ağırlıklar ve dağılma cezası tahmindir
/// (oyun o haftanın antrenman programını da hesaba katar); yıldızlar "tahmini" olarak gösterilir.
/// </summary>
public static class AntrenorEkibi
{
    public static readonly IReadOnlyList<AntrenorOzelligi> Ozellikler = new AntrenorOzelligi[]
    {
        new("Hucum", "Hücum"), new("Savunma", "Savunma"), new("Kondisyon", "Kondisyon"), new("Kaleci", "Kalecilik"),
        new("TopaSahiplik", "Topa sahiplik"), new("DuranTop", "Duran toplar"), new("Taktik", "Taktik"), new("Teknik", "Teknik"),
        new("Motivasyon", "Motivasyon"), new("Otorite", "Otorite"), new("Kararlilik", "Kararlılık")
    };

    public static readonly IReadOnlyList<KocKategorisi> Kategoriler = new KocKategorisi[]
    {
        new("Fiziksel", "Fiziksel", new[] { "Kondisyon" }),
        new("Kaleciler", "Kaleciler", new[] { "Kaleci" }),
        new("SavunmaTaktik", "Savunma taktik", new[] { "Savunma", "Taktik" }),
        new("SavunmaTeknik", "Savunma teknik", new[] { "Savunma", "Teknik" }),
        new("TopaSahiplikTaktik", "Topa sahiplik taktik", new[] { "TopaSahiplik", "Taktik" }),
        new("TopaSahiplikTeknik", "Topa sahiplik teknik", new[] { "TopaSahiplik", "Teknik" }),
        new("HucumTaktik", "Hücum taktik", new[] { "Hucum", "Taktik" }),
        new("HucumTeknik", "Hücum teknik", new[] { "Hucum", "Teknik" }),
        new("DuranToplar", "Duran toplar", new[] { "DuranTop", "Teknik" })
    };

    private static readonly string[] Zihinsel = { "Motivasyon", "Otorite", "Kararlilik" };

    /// <summary>Dağılma cezası: 1 kategori tam güç; her ek kategori etkinliği düşürür (20 hücumlu bir antrenör tek kategoride 5, iki kategoride ~4 yıldız).</summary>
    public static double DagilmaCarpani(int kategoriSayisi) => kategoriSayisi switch
    {
        <= 1 => 1.0,
        2 => 0.88,
        3 => 0.80,
        4 => 0.74,
        _ => 0.68
    };

    /// <summary>Antrenörün bir kategorideki ham yeteneği (0-20), dağılma cezası olmadan.</summary>
    public static double HamPuan(Antrenor a, KocKategorisi k)
    {
        var ana = k.Ozellikler.Length == 2 && k.Kod == "DuranToplar"
            ? (a.Ozellik("DuranTop") * 2 + a.Ozellik("Teknik")) / 3.0
            : k.Ozellikler.Average(a.Ozellik);
        var zihin = Zihinsel.Average(a.Ozellik);
        return 0.75 * ana + 0.25 * zihin;
    }

    public static double Yildiz(double puan) => Math.Clamp(Math.Round(puan / 20 * 5 * 2, MidpointRounding.AwayFromZero) / 2, 0.5, 5);

    private static (double Yildiz, Antrenor? Lider, double Puan) KategoriSonucu(KocKategorisi k, IReadOnlyList<Antrenor> ekip, IReadOnlyDictionary<string, HashSet<string>> atama)
    {
        Antrenor? lider = null;
        var enIyi = 0.0;

        foreach (var a in ekip)
        {
            if (!atama.TryGetValue(a.Id, out var kat) || !kat.Contains(k.Kod))
            {
                continue;
            }

            var p = HamPuan(a, k) * DagilmaCarpani(kat.Count);

            if (p > enIyi)
            {
                enIyi = p;
                lider = a;
            }
        }

        return (lider is null ? 0 : Yildiz(enIyi), lider, enIyi);
    }

    private static (double Yildiz, double Puan) Toplam(IReadOnlyList<Antrenor> ekip, IReadOnlyDictionary<string, HashSet<string>> atama, IReadOnlyDictionary<string, double> agirlik)
    {
        double y = 0, p = 0;

        foreach (var k in Kategoriler)
        {
            var (ky, _, kp) = KategoriSonucu(k, ekip, atama);
            var w = agirlik.GetValueOrDefault(k.Kod, 1);
            y += ky * w;
            p += kp * w;
        }

        return (y, p);
    }

    /// <summary>
    /// En iyi atama: tek tek kutu açıp kapatarak (oyunun ekranındaki işlem) toplam yıldızı artıran değişiklikler, artık iyileşme
    /// kalmayana dek uygulanır. Kondisyon/kaleci antrenörlerinin kısıtı korunur. Programın sık kullandığı kategoriler ağırlıklıdır.
    /// </summary>
    public static Dictionary<string, HashSet<string>> EnIyiAtama(IReadOnlyList<Antrenor> ekip, IReadOnlyDictionary<string, double> agirlik)
    {
        var atama = ekip.ToDictionary(a => a.Id, a => a.Kategoriler.Where(k => a.Kisit is null || k == a.Kisit).ToHashSet());
        var (by, bp) = Toplam(ekip, atama, agirlik);

        for (var tur = 0; tur < 200; tur++)
        {
            var iyilesti = false;

            foreach (var a in ekip)
            {
                foreach (var k in Kategoriler)
                {
                    if (a.Kisit is not null && k.Kod != a.Kisit)
                    {
                        continue;
                    }

                    var kume = atama[a.Id];
                    var vardi = kume.Contains(k.Kod);

                    if (vardi) kume.Remove(k.Kod); else kume.Add(k.Kod);

                    var (y, p) = Toplam(ekip, atama, agirlik);

                    if (y > by + 1e-9 || (Math.Abs(y - by) < 1e-9 && p > bp + 1e-6))
                    {
                        by = y;
                        bp = p;
                        iyilesti = true;
                    }
                    else if (vardi) kume.Add(k.Kod); else kume.Remove(k.Kod);
                }
            }

            if (!iyilesti)
            {
                break;
            }
        }

        return atama;
    }

    /// <summary>Planın her kategoriye kabaca kaç seans ayırdığı: uyarıları önceliklendirmek için.</summary>
    public static Dictionary<string, int> ProgramKullanimi(TakimPlani? plan)
    {
        var sayac = Kategoriler.ToDictionary(k => k.Kod, _ => 0);

        foreach (var s in plan?.Gunler.SelectMany(g => g.Hucreler).Where(h => h.Durum != HucreDurumu.SureDisi).Select(h => h.Onerilen).Where(s => s is not null) ?? Enumerable.Empty<SeansTuru?>())
        {
            var ad = Seanslar.Kucuk(s!.Ad);
            var kod = s.Amac switch
            {
                SeansAmaci.Taktik => s.Alan is TaktikAlan.BaskiHatti or TaktikAlan.PresYogunlugu or TaktikAlan.PresTuzagi or TaktikAlan.TopRakipteyken ? "SavunmaTaktik"
                    : s.Alan is TaktikAlan.FirsatYaratma ? "HucumTaktik" : "TopaSahiplikTaktik",
                SeansAmaci.MacHazirligi => ad.Contains("defens") || ad.Contains("savunma") ? "SavunmaTaktik" : ad.Contains("final") || ad.Contains("hucum") ? "HucumTaktik" : "TopaSahiplikTaktik",
                SeansAmaci.Teknik => ad.Contains("defens") || ad.Contains("savunma") ? "SavunmaTeknik" : ad.Contains("attack") || ad.Contains("atak") || ad.Contains("hucum") ? "HucumTeknik" : "TopaSahiplikTeknik",
                SeansAmaci.Fiziksel => "Fiziksel",
                SeansAmaci.DuranTop => "DuranToplar",
                _ => null
            };

            if (kod is not null)
            {
                sayac[kod]++;
            }
        }

        return sayac;
    }

    public static EkipAnalizi Analiz(IReadOnlyList<Antrenor> ekip, TakimPlani? plan)
    {
        var kullanim = ProgramKullanimi(plan);
        var toplamSeans = Math.Max(1, kullanim.Values.Sum());
        // Ağırlık 1 + programdaki payı: sık kullanılan kategori iyileştirmede öne geçer, kullanılmayan da sıfırlanmaz.
        var agirlik = Kategoriler.ToDictionary(k => k.Kod, k => 1 + 0.6 * kullanim[k.Kod] / toplamSeans);
        var mevcut = ekip.ToDictionary(a => a.Id, a => a.Kategoriler.ToHashSet());
        var enIyi = EnIyiAtama(ekip, agirlik);
        var uyarilar = new List<KocUyarisi>();

        var durumlar = Kategoriler.Select(k =>
        {
            var (y, l, _) = KategoriSonucu(k, ekip, mevcut);
            var (ey, el, _) = KategoriSonucu(k, ekip, enIyi);
            return new KategoriDurumu(k, y, l, ey, el, kullanim[k.Kod]);
        }).ToList();

        var toplam = durumlar.Sum(d => d.Yildiz);
        var enIyiToplam = durumlar.Sum(d => d.EnIyiYildiz);

        if (ekip.Count == 0)
        {
            uyarilar.Add(new KocUyarisi(UyariTuru.Bilgi, "Antrenör verisi yok",
                "MCPFM antrenör ekibini okumuyor. Antrenörlerinizi ekleyin ya da oyundaki listeyi toplu yapıştırın."));
            return new EkipAnalizi(durumlar, enIyi, 0, 0, uyarilar, new Dictionary<string, (string, double)>());
        }

        if (enIyiToplam - toplam >= 0.5)
        {
            var farklar = ekip.Where(a => !mevcut[a.Id].SetEquals(enIyi[a.Id])).Select(a => a.Ad).ToList();
            uyarilar.Add(new KocUyarisi(UyariTuru.AtamayiIyilestir, $"Atamayı değiştirerek +{enIyiToplam - toplam:0.#} yıldız",
                $"Yeni antrenör almadan, yalnızca görevleri yeniden dağıtarak kazanılır. Değişen: {string.Join(", ", farklar)}. "
                + "Oyunda Antrenman > Antrenör Görevleri ekranından uygulayın."));
        }

        // Kullanılan ama zayıf kategori: işe alım ya da değiştirme.
        foreach (var d in durumlar.Where(d => d.EnIyiYildiz <= 2.5).OrderByDescending(d => d.ProgramSeansi).ThenBy(d => d.EnIyiYildiz))
        {
            var aranan = string.Join(", ", d.Kategori.Ozellikler.Select(o => $"{Ozellikler.First(x => x.Kod == o).Ad} 15+"));
            var kullanimMetni = d.ProgramSeansi > 0 ? $"Programınız bu kategoriye {d.ProgramSeansi} seans ayırıyor ama bu" : "Bu";
            uyarilar.Add(new KocUyarisi(d.ProgramSeansi > 0 ? UyariTuru.IseAl : UyariTuru.Bilgi,
                $"{d.Kategori.Ad}: en iyi atamayla bile {d.EnIyiYildiz:0.#} yıldız",
                $"{kullanimMetni} kategoride iyi bir antrenör yok. Aranan profil: {aranan}, Motivasyon ve Otorite 13+.",
                Kategori: d.Kategori.Kod));
        }

        var ozet = new Dictionary<string, (string, double)>();

        foreach (var a in ekip)
        {
            var enIyiKat = Kategoriler.Where(k => a.Kisit is null || k.Kod == a.Kisit).OrderByDescending(k => HamPuan(a, k)).First();
            ozet[a.Id] = (enIyiKat.Kod, HamPuan(a, enIyiKat));

            var liderOldugu = durumlar.Where(d => d.EnIyiLider?.Id == a.Id).ToList();
            var mevcutSayi = mevcut[a.Id].Count;

            if (mevcutSayi >= 5)
            {
                uyarilar.Add(new KocUyarisi(UyariTuru.YukuAzalt, $"{a.Ad}: {mevcutSayi} kategoriye dağılmış",
                    $"Çok kategoriye atanan antrenör her birinde zayıflar. En güçlü olduğu {enIyiKat.Ad} başta olmak üzere 1–3 kategoriye indirin.", a.Id));
            }

            if (HamPuan(a, enIyiKat) < 11.5)
            {
                // En güçlü olduğu kategoride bile 2.5 yıldız civarı: kategori lideri olsa bile ekibi aşağı çekiyor.
                var liderMetni = liderOldugu.Count > 0 ? $" {string.Join(", ", liderOldugu.Select(d => d.Kategori.Ad))} kategorisini o yönettiği için bu kategori de zayıf kalıyor." : "";
                uyarilar.Add(new KocUyarisi(UyariTuru.Degistir, $"{a.Ad}: değiştirilmesi önerilir",
                    $"En güçlü olduğu kategori bile ({enIyiKat.Ad}) {Yildiz(HamPuan(a, enIyiKat)):0.#} yıldız düzeyinde.{liderMetni} Yerine en zayıf kategorinizi güçlendirecek bir antrenör alın.", a.Id));
            }
            else if (liderOldugu.Count == 0)
            {
                var sebep = mevcutSayi == 0 ? "hiçbir kategoriye atanmamış" : "atandığı hiçbir kategoride en iyi değil";
                var tur = HamPuan(a, enIyiKat) < 12 ? UyariTuru.Degistir : UyariTuru.AtamayiKaldir;
                uyarilar.Add(new KocUyarisi(tur,
                    tur == UyariTuru.Degistir ? $"{a.Ad}: değiştirilmesi önerilir" : $"{a.Ad}: katkısı yok",
                    tur == UyariTuru.Degistir
                        ? $"En iyi olduğu kategori bile ({enIyiKat.Ad}) {Yildiz(HamPuan(a, enIyiKat)):0.#} yıldız düzeyinde ve {sebep}. Yerine en zayıf kategorinizi güçlendirecek bir antrenör alın."
                        : $"{sebep[0..1].ToUpper(CultureInfo.GetCultureInfo("tr-TR"))}{sebep[1..]}; daha iyi meslektaşlarının yanında zaman harcıyor. Atamalarını kaldırmak diğer kategorilerdeki etkisini artırır ya da en zayıf kategorinize kaydırın.",
                    a.Id));
            }
        }

        uyarilar = uyarilar
            .OrderBy(u => u.Tur switch { UyariTuru.AtamayiIyilestir => 0, UyariTuru.IseAl => 1, UyariTuru.Degistir => 2, UyariTuru.YukuAzalt => 3, UyariTuru.AtamayiKaldir => 4, _ => 5 })
            .ToList();

        return new EkipAnalizi(durumlar, enIyi, toplam, enIyiToplam, uyarilar, ozet);
    }

    /// <summary>
    /// Toplu yapıştırma: her satır bir antrenör. Biçim (sekme, ; ya da , ile):
    /// Ad; Görev; Hücum; Savunma; Kondisyon; Kalecilik; Topa sahiplik; Duran toplar; Taktik; Teknik; Motivasyon; Otorite; Kararlılık; Kategoriler
    /// Kategoriler boşlukla ayrılmış kodlar ya da adlardır (ör. "SavunmaTaktik HucumTaktik"). Başlık satırı yok sayılır.
    /// </summary>
    public static (List<Antrenor> Liste, List<string> Hatalar) Ice(string metin)
    {
        var liste = new List<Antrenor>();
        var hatalar = new List<string>();
        var no = 0;

        foreach (var ham in metin.Split('\n'))
        {
            no++;
            var satir = ham.Trim();

            if (satir.Length == 0)
            {
                continue;
            }

            var ayirici = satir.Contains('\t') ? '\t' : satir.Contains(';') ? ';' : ',';
            var p = satir.Split(ayirici).Select(x => x.Trim()).ToArray();

            if (p.Length < 13)
            {
                hatalar.Add($"{no}. satır: 13 sütun bekleniyordu, {p.Length} var.");
                continue;
            }

            if (!int.TryParse(p[2], out _))
            {
                continue; // başlık
            }

            var a = new Antrenor { Ad = p[0], Gorev = string.IsNullOrWhiteSpace(p[1]) ? null : p[1] };

            for (var i = 0; i < 11; i++)
            {
                if (int.TryParse(p[2 + i], out var v))
                {
                    a.Ozellikler[Ozellikler[i].Kod] = Math.Clamp(v, 1, 20);
                }
            }

            if (p.Length > 13)
            {
                foreach (var k in p[13].Split(new[] { ' ', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kat = Kategoriler.FirstOrDefault(x => x.Kod.Equals(k, StringComparison.OrdinalIgnoreCase) || Seanslar.Kucuk(x.Ad).Replace(" ", "") == Seanslar.Kucuk(k));

                    if (kat is not null)
                    {
                        a.Kategoriler.Add(kat.Kod);
                    }
                }
            }

            liste.Add(a);
        }

        return (liste, hatalar);
    }
}
