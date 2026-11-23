using Asistan.Core.Planlama;
using Asistan.Core.Veri;

namespace Asistan.Core.Uygulama;

public enum DegisiklikTuru { Seans, Odak, Yogunluk }

/// <summary>Oyuna gönderilebilecek tek bir değişiklik: eski değer → hedef değer.</summary>
public sealed record Degisiklik(DegisiklikTuru Tur, string Anahtar, string Baslik, Secenek? Eski, Secenek Hedef, string Gerekce, string? Tarih = null, int Slot = -1, int OyuncuId = 0)
{
    public static Degisiklik SeansDegisikligi(HucreOnerisi h) => new(
        DegisiklikTuru.Seans, $"seans:{h.Anahtar}", $"{h.Tarih:dd.MM} {h.Slot + 1}. seans",
        h.Mevcut is null ? null : new Secenek(h.Mevcut.Id, h.Mevcut.Ad), new Secenek(h.Onerilen!.Id, h.Onerilen.Ad), h.Gerekce,
        h.Tarih.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), h.Slot);

    public static Degisiklik OdakDegisikligi(OyuncuOnerisi o) => new(
        DegisiklikTuru.Odak, $"odak:{o.Oyuncu.Id}", $"{o.Oyuncu.Ad} – ek odak",
        o.Oyuncu.Odak ?? new Secenek(-1, "Yok"), o.OnerilenOdak!, string.Join(" ", o.OdakGerekcesi), OyuncuId: o.Oyuncu.Id);

    public static Degisiklik YogunlukDegisikligi(OyuncuOnerisi o) => new(
        DegisiklikTuru.Yogunluk, $"yogunluk:{o.Oyuncu.Id}", $"{o.Oyuncu.Ad} – yoğunluk",
        o.Oyuncu.YogunlukOtomatik ? new Secenek(-1, "Otomatik") : o.Oyuncu.Yogunluk, o.OnerilenYogunluk!, string.Join(" ", o.YogunlukGerekcesi), OyuncuId: o.Oyuncu.Id);

    public AntrenmanIstegi Istek(Secenek deger)
    {
        var istek = new AntrenmanIstegi();

        switch (Tur)
        {
            case DegisiklikTuru.Seans:
                istek.Sessions.Add(new SeansIstegi { Date = Tarih!, Slot = Slot, Session = (int)deger.Id });
                break;
            case DegisiklikTuru.Odak:
                istek.Players.Add(new OyuncuIstegi { PlayerId = OyuncuId, Focus = deger.Id });
                break;
            default:
                istek.Players.Add(new OyuncuIstegi { PlayerId = OyuncuId, Intensity = deger.Id });
                break;
        }

        return istek;
    }

    /// <summary>Oyundan okunan güncel değer. Bulunamazsa null (ör. oyuncu kadrodan çıktı, gün takvimden düştü).</summary>
    public Secenek? Oku(AntrenmanDto t)
    {
        switch (Tur)
        {
            case DegisiklikTuru.Seans:
                var s = t.Weeks.SelectMany(w => w.Days).FirstOrDefault(d => d.Date == Tarih)?.Sessions.FirstOrDefault(x => x.Slot == Slot);
                return s is null ? null : new Secenek(s.Type, s.Name);
            case DegisiklikTuru.Odak:
                var p = t.Players.FirstOrDefault(x => x.PlayerId == OyuncuId);
                return p is null ? null : p.Focus is null ? new Secenek(-1, "Yok") : new Secenek(p.Focus.Id, p.Focus.Name);
            default:
                var q = t.Players.FirstOrDefault(x => x.PlayerId == OyuncuId);
                return q is null ? null : q.IntensityIsAutomatic == true ? new Secenek(-1, "Otomatik") : q.Intensity is null ? null : new Secenek(q.Intensity.Id, q.Intensity.Name);
        }
    }

    public bool Duzenlenebilir(AntrenmanDto t)
    {
        switch (Tur)
        {
            case DegisiklikTuru.Seans:
                var s = t.Weeks.SelectMany(w => w.Days).FirstOrDefault(d => d.Date == Tarih)?.Sessions.FirstOrDefault(x => x.Slot == Slot);
                return s?.CanEdit == true;
            case DegisiklikTuru.Odak:
                var p = t.Players.FirstOrDefault(x => x.PlayerId == OyuncuId);
                return p is not null && p.FocusLocked != true;
            default:
                var q = t.Players.FirstOrDefault(x => x.PlayerId == OyuncuId);
                return q is not null && q.IntensityLocked != true;
        }
    }
}

public enum KalemDurumu
{
    Hazir,
    Uygulaniyor,
    /// <summary>Oyundan geri okundu ve hedefle eşleşti.</summary>
    Dogrulandi,
    /// <summary>Gönderilmeden önce oyunda zaten hedef değerdeydi (K09).</summary>
    ZatenUygulanmis,
    /// <summary>Plan yapıldıktan sonra oyunda değiştirildi; üzerine yazılmadı.</summary>
    Cakisma,
    Atlandi,
    Basarisiz,
    /// <summary>Zaman aşımı/kopma ve geri okuma da başarısız: sonuç bilinmiyor, tekrar gönderilmedi.</summary>
    Belirsiz
}

public enum IslemDurumu
{
    Hazir,
    Uygulaniyor,
    Dogrulandi,
    KismenUygulandi,
    BasarisizBelirsiz,
    Durduruldu
}

/// <summary>WRITE-03: her işlem için eski değer, hedef değer, gerçekte okunan değer ve hata kaydı.</summary>
public sealed class IslemKalemi
{
    public required Degisiklik Degisiklik { get; init; }
    public KalemDurumu Durum { get; set; } = KalemDurumu.Hazir;
    public Secenek? OncekiOkunan { get; set; }
    public Secenek? SonOkunan { get; set; }
    public string? Hata { get; set; }
    public DateTimeOffset? Zaman { get; set; }
}

public sealed class IslemRaporu
{
    public required string Id { get; init; }
    public required string TakimAdi { get; init; }
    public required string KariyerAnahtari { get; init; }
    public required string PlanAnahtari { get; init; }
    public required DateTimeOffset Baslangic { get; init; }
    public bool GeriAlma { get; init; }
    public IslemDurumu Durum { get; set; } = IslemDurumu.Hazir;
    public List<IslemKalemi> Kalemler { get; init; } = new();
    public List<string> Notlar { get; init; } = new();

    public int Dogrulanan => Kalemler.Count(k => k.Durum is KalemDurumu.Dogrulandi or KalemDurumu.ZatenUygulanmis);

    /// <summary>"0 doğrulandı" asla "tamamlandı" diye gösterilmez: özet her zaman sayılarla yazılır.</summary>
    public string Ozet => Durum switch
    {
        IslemDurumu.Hazir => "Hazır: oyunda hiçbir değişiklik yapılmadı.",
        IslemDurumu.Uygulaniyor => "Uygulanıyor: tamamlanma henüz doğrulanmadı.",
        IslemDurumu.Dogrulandi => $"Doğrulandı: {Dogrulanan}/{Kalemler.Count} değişiklik oyundan geri okundu ve eşleşti.",
        IslemDurumu.KismenUygulandi => $"Kısmen uygulandı: {Dogrulanan}/{Kalemler.Count} doğrulandı; kalanlar aşağıda.",
        IslemDurumu.Durduruldu => $"Durduruldu: {Dogrulanan}/{Kalemler.Count} doğrulandı. " + string.Join(" ", Notlar.Take(1)),
        _ => $"Başarısız/belirsiz: {Dogrulanan}/{Kalemler.Count} doğrulandı. Belirsiz işlemler otomatik tekrar gönderilmedi."
    };
}
