using Asistan.Core.Veri;

namespace Asistan.Core.Analiz;

public enum Oncelik { Yuksek, Orta, Dusuk, Yok, Belirsiz }

/// <summary>
/// Bir taktik alanı için açıklanabilir sonuç. Dört soruya cevap verir: neyi gözlemledik, hangi talimatla ilişkili,
/// hangi seansı neden önerdik, hangi veri/kısıt yüzünden sınırlıyız.
/// </summary>
public sealed record SorunKarti(
    TaktikAlan Alan,
    Oncelik Oncelik,
    double Skor,
    Okuma<Puan> Anlayis,
    double Eksiklik,
    double Onem,
    double Guven,
    string Gozlem,
    string IlgiliTalimat,
    IReadOnlyList<SeansTuru> UygunSeanslar,
    string Oneri,
    IReadOnlyList<string> Sinirlar)
{
    public string AlanAdi => Seanslar.AlanAdi(Alan);
    public bool SeansOnerilir => Oncelik is Oncelik.Yuksek or Oncelik.Orta && UygunSeanslar.Count > 0;
}

public sealed record AnalizSonucu(string PlanAnahtari, DateTimeOffset Zaman, IReadOnlyList<SorunKarti> Kartlar, IReadOnlyList<string> GenelNotlar)
{
    public IEnumerable<SorunKarti> Oncelikli => Kartlar.Where(k => k.SeansOnerilir).OrderByDescending(k => k.Skor);
}

/// <summary>
/// Taktiksel anlayış, oyuncu yeteneği ve maç sonucu ayrı tutulur: burada yalnızca oyunun ölçtüğü anlayış alanlarına bakılır,
/// kötü bir maç sonucu tek başına "anlayış eksik" sayılmaz. Öncelik = eksiklik × taktik açısından önem × veri güveni.
/// </summary>
public static class TaktikAnalizi
{
    public static AnalizSonucu Calistir(Anlik a, Ayarlar ayar)
    {
        var notlar = new List<string>();
        // Oyunun kendi tavsiyesi ("Antrenmanlarda Baskı Hattı kurgularına daha fazla zaman ayırmamız gerekiyor.") en güçlü işarettir.
        var tavsiyeAlanlari = a.OyunTavsiyesiOlumlu ? new List<TaktikAlan>() : Seanslar.MetindekiAlanlar(a.OyunTavsiyesi);

        if (!string.IsNullOrWhiteSpace(a.OyunTavsiyesi))
        {
            notlar.Add("Oyunun tavsiyesi: \"" + a.OyunTavsiyesi + "\"");
        }
        var kartlar = new List<SorunKarti>();
        var olcumler = a.Anlayis.Values.Where(o => o.Anlayis.VarMi).Select(o => o.Anlayis.Deger!.Orta).ToList();
        var ortalama = olcumler.Count > 0 ? olcumler.Average() : (double?)null;

        if (!a.Taktik.VarMi)
        {
            notlar.Add("Aktif taktik okunamadı. Alanların taktik açısından önemi bilinmediği için hepsi eşit sayıldı ve güven düşürüldü.");
        }

        foreach (var alan in Enum.GetValues<TaktikAlan>())
        {
            var olcum = a.Anlayis.TryGetValue(alan, out var o) ? o : null;
            var seanslar = a.SeansTurleri.Values.Where(t => t.Amac == SeansAmaci.Taktik && t.Alan == alan).OrderBy(t => t.Id).ToList();
            var (onem, talimat) = Onem(alan, a.Taktik);
            var sinirlar = new List<string>();

            if (olcum is null || !olcum.Anlayis.VarMi)
            {
                // K01: veri eksikliği gerçek düşük değerden ayrılır. Değer uydurulmaz, seans önerilmez.
                var durum = olcum?.Anlayis.Durum ?? VeriDurumu.Desteklenmiyor;
                kartlar.Add(new SorunKarti(alan, Oncelik.Belirsiz, 0, olcum?.Anlayis ?? Okuma<Puan>.Yok(Anlik.KaynakTaktik, a.OkunmaZamani, VeriDurumu.Desteklenmiyor),
                    0, onem, 0,
                    durum == VeriDurumu.Desteklenmiyor ? "Bu alan bu oyun/bağlantı sürümünde okunamıyor." : "Bu alanın anlayış değeri okunamadı (veri eksik).",
                    talimat, seanslar, "Veri olmadan öneri yapılmadı.",
                    new[] { $"Veri durumu: {DurumAdi(durum)} (kaynak: {Anlik.KaynakTaktik})." }));
                continue;
            }

            var puan = olcum.Anlayis.Deger!;
            var guven = puan.Guven * (a.Taktik.VarMi ? 1 : 0.7);

            if (puan.AralikMi)
            {
                sinirlar.Add($"Oyun değeri aralık olarak veriyor ({puan}); bilgi eksik olduğundan güven %{guven * 100:0}.");
            }

            var eksiklik = Math.Max(0, ayar.HedefAnlayis - puan.Orta) / ayar.HedefAnlayis;
            var gozlem = $"{Seanslar.AlanAdi(alan)} anlayışı {puan}/100";

            if (ortalama is { } ort)
            {
                var fark = puan.Orta - ort;
                gozlem += Math.Abs(fark) < 3 ? ", diğer alanlarla aynı düzeyde." : fark < 0 ? $", diğer alanların ortalamasından {-fark:0} puan düşük." : $", ortalamanın {fark:0} puan üstünde.";
                // Göreli zayıflık önceliği hafifçe artırır: yalnız mutlak eşik değil, takımın kendi dengesi de önemli.
                if (fark < -5)
                {
                    eksiklik += Math.Min(0.1, -fark / 200);
                }
            }

            if (olcum.MacMaruziyeti.VarMi && olcum.MacMaruziyeti.Deger!.Orta < 30)
            {
                sinirlar.Add($"Maç maruziyeti düşük ({olcum.MacMaruziyeti.Deger}): taktik yeni olabilir; anlayış maç oynadıkça da artar.");
            }

            if (olcum.AntrenmanKatkisi.VarMi)
            {
                gozlem += $" Antrenman katkısı şu an {olcum.AntrenmanKatkisi.Deger}.";
            }

            var skor = eksiklik * onem * guven;
            var oyunIstiyor = tavsiyeAlanlari.Contains(alan);
            var oncelik = puan.Orta >= ayar.HedefAnlayis - ayar.AnlamliEksik ? Oncelik.Yok
                : skor >= ayar.YuksekOncelikEsigi ? Oncelik.Yuksek
                : skor >= ayar.OrtaOncelikEsigi ? Oncelik.Orta
                : Oncelik.Dusuk;

            if (oyunIstiyor)
            {
                // Oyun bu alan için açıkça daha fazla antrenman istiyor: değer yüksek görünse bile en az yüksek öncelik.
                oncelik = Oncelik.Yuksek;
                skor = Math.Max(skor, ayar.YuksekOncelikEsigi) + 0.5;
                gozlem += $" Oyunun kendi tavsiyesi bu alanı istiyor: \"{a.OyunTavsiyesi}\"";
            }

            string oneri;

            if (oncelik is Oncelik.Yok)
            {
                oneri = "Yeterli düzeyde; ek seans gerekmiyor.";
            }
            else if (seanslar.Count == 0)
            {
                oneri = "Bu alanı çalıştıran seans oyunun seans listesinde bulunamadı ya da eşleştirilmedi.";
                sinirlar.Add("Seans eşleştirmesi eksik: Ayarlar > Seans eşleştirme bölümünden düzeltilebilir.");
            }
            else if (oncelik is Oncelik.Dusuk)
            {
                oneri = $"Düşük öncelik: boş kapasite kalırsa \"{seanslar[0].Ad}\" eklenir.";
            }
            else
            {
                oneri = $"Bu iki hafta içinde \"{seanslar[0].Ad}\" seansı önerilir; seansın oyundaki tanımı bu alanı çalıştırıyor.";
            }

            sinirlar.Add("Bir seansın kesin sayısal gelişim etkisi bilinmiyor; değişim bir sonraki okumada izlenir.");
            kartlar.Add(new SorunKarti(alan, oncelik, skor, olcum.Anlayis, eksiklik, onem, guven, gozlem, talimat, seanslar, oneri, sinirlar));
        }

        var sirali = kartlar
            .OrderBy(k => k.Oncelik)
            .ThenByDescending(k => k.Skor)
            .ThenBy(k => k.Alan)
            .ToList();

        return new AnalizSonucu(a.Kimlik.PlanAnahtari, a.OkunmaZamani, sirali, notlar);
    }

    private static string DurumAdi(VeriDurumu d) => d switch
    {
        VeriDurumu.Mevcut => "mevcut",
        VeriDurumu.Eksik => "eksik",
        VeriDurumu.Eski => "eski",
        _ => "desteklenmiyor"
    };

    // Talimat anahtarları oyunun İngilizce anahtarlarıdır (ör. "PressingIntensity": "MuchMoreUrgent"). Anahtar biçimleri
    // sürüme göre değişebileceği için kalıp aranır; eşleşme yoksa önem 1 kalır ve bu açıkça yazılır.
    internal static (double Onem, string Aciklama) Onem(TaktikAlan alan, Okuma<AktifTaktik> taktik)
    {
        if (!taktik.VarMi)
        {
            return (1, "Aktif taktik okunamadı.");
        }

        var t = taktik.Deger!;
        var eslesen = new List<string>();
        double onem = alan is TaktikAlan.TopaSahipkenDizilis or TaktikAlan.TopRakipteyken ? 1.2 : 1.0;

        bool Ara(string[] anahtarKaliplari, out string? deger)
        {
            foreach (var (k, v) in t.Talimatlar)
            {
                if (anahtarKaliplari.Any(p => k.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    eslesen.Add($"{k} = {v ?? "—"}");
                    deger = v;
                    return true;
                }
            }

            deger = null;
            return false;
        }

        // "LessUrgent" içinde "Urgent" geçer: azalan kalıp her zaman önce denetlenir.
        static bool Artan(string? v) => v is not null && (v.Contains("More", StringComparison.OrdinalIgnoreCase) || v.Contains("High", StringComparison.OrdinalIgnoreCase)
                                                        || v.Contains("Much", StringComparison.OrdinalIgnoreCase) || v.Contains("Extreme", StringComparison.OrdinalIgnoreCase)
                                                        || v.Contains("Urgent", StringComparison.OrdinalIgnoreCase));
        static bool Azalan(string? v) => v is not null && (v.Contains("Less", StringComparison.OrdinalIgnoreCase) || v.Contains("Low", StringComparison.OrdinalIgnoreCase)
                                                         || v.Contains("Fewer", StringComparison.OrdinalIgnoreCase));
        static bool Acik(string? v) => v is not null && !v.Contains("None", StringComparison.OrdinalIgnoreCase) && !v.Contains("Off", StringComparison.OrdinalIgnoreCase)
                                       && !v.Contains("Default", StringComparison.OrdinalIgnoreCase) && !v.Equals("No", StringComparison.OrdinalIgnoreCase);

        string? d;

        switch (alan)
        {
            case TaktikAlan.PresYogunlugu:
                if (Ara(new[] { "PressingIntensity", "Pressing", "TriggerPress", "Press" }, out d))
                {
                    onem = Azalan(d) ? 0.6 : Artan(d) ? 1.5 : 1.0;
                }
                break;
            case TaktikAlan.PresTuzagi:
                if (Ara(new[] { "Trap" }, out d))
                {
                    onem = Acik(d) ? 1.5 : 0.6;
                }
                break;
            case TaktikAlan.BaskiHatti:
                if (Ara(new[] { "LineOfEngagement", "Engagement" }, out d))
                {
                    onem = Azalan(d) ? 0.9 : Artan(d) ? 1.4 : 1.0;
                }
                break;
            case TaktikAlan.PasStili:
                if (Ara(new[] { "Passing", "Directness", "Pass" }, out d))
                {
                    onem = Acik(d) ? 1.3 : 1.0;
                }
                break;
            case TaktikAlan.OyunKurma:
                if (Ara(new[] { "BuildUp", "GoalKick", "Distribution", "PlayOut" }, out d))
                {
                    onem = Acik(d) ? 1.3 : 1.0;
                }
                break;
            case TaktikAlan.FirsatYaratma:
                if (Ara(new[] { "ChanceCreation", "Cross", "Shoot", "Dribble", "WorkBall", "Final" }, out d))
                {
                    onem = Acik(d) ? 1.3 : 1.0;
                }
                break;
        }

        if (t.StilAnahtari is { } stil && alan is TaktikAlan.PresYogunlugu or TaktikAlan.PresTuzagi or TaktikAlan.BaskiHatti
            && (stil.Contains("Press", StringComparison.OrdinalIgnoreCase) || stil.Contains("Gegen", StringComparison.OrdinalIgnoreCase)))
        {
            onem = Math.Max(onem, 1.5);
            eslesen.Add($"Stil = {stil}");
        }

        var aciklama = eslesen.Count > 0
            ? $"\"{t.Ad}\" taktiğinde ilgili ayar: {string.Join(", ", eslesen.Distinct())}."
            : $"\"{t.Ad}\" taktiğinde bu alana özel talimat bulunamadı; varsayılan önem kullanıldı.";

        return (onem, aciklama);
    }
}
