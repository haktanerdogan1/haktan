namespace Asistan.Core.Veri;

/// <summary>Bir değerin oyundan okunma durumu. Sıfır, boş ve okunamama birbirine dönüştürülmez.</summary>
public enum VeriDurumu
{
    /// <summary>Oyundan okundu, güncel.</summary>
    Mevcut,
    /// <summary>Bağlantı bu alanı döndürmedi veya oyun maskeledi.</summary>
    Eksik,
    /// <summary>Okundu ama süresi geçti ya da kariyer/tarih değişti.</summary>
    Eski,
    /// <summary>Bu oyun/bağlantı sürümü bu alanı hiç sunmuyor.</summary>
    Desteklenmiyor
}

/// <summary>Veri sözleşmesi: her değer kaynağı, okunma zamanı, ölçeği ve durumuyla taşınır.</summary>
public sealed record Okuma<T>(T? Deger, VeriDurumu Durum, string Kaynak, DateTimeOffset OkunmaZamani, string? Olcek = null)
{
    public bool VarMi => Durum == VeriDurumu.Mevcut && Deger is not null;

    public static Okuma<T> Var(T deger, string kaynak, DateTimeOffset zaman, string? olcek = null) =>
        new(deger, VeriDurumu.Mevcut, kaynak, zaman, olcek);

    public static Okuma<T> Yok(string kaynak, DateTimeOffset zaman, VeriDurumu durum = VeriDurumu.Eksik, string? olcek = null) =>
        new(default, durum, kaynak, zaman, olcek);

    public static Okuma<T> Belki(T? deger, string kaynak, DateTimeOffset zaman, string? olcek = null) =>
        deger is null ? Yok(kaynak, zaman, VeriDurumu.Eksik, olcek) : Var(deger, kaynak, zaman, olcek);

    public override string ToString() => Durum switch
    {
        VeriDurumu.Mevcut => Deger?.ToString() ?? "—",
        VeriDurumu.Eksik => "okunamadı",
        VeriDurumu.Eski => "eski veri",
        _ => "desteklenmiyor"
    };
}

/// <summary>Oyunun "49" veya bilgi eksikken verdiği "45-52" biçimli 0-100 puanı.</summary>
public sealed record Puan(double Alt, double Ust)
{
    public double Orta => (Alt + Ust) / 2;
    public bool AralikMi => Ust > Alt;

    /// <summary>Aralık genişledikçe ölçümün güveni düşer (tek değer = 1).</summary>
    public double Guven => Math.Clamp(1 - (Ust - Alt) / 40.0, 0.3, 1);

    public static Puan? Coz(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return null;
        }

        var parcalar = metin.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parcalar.Length == 1 && double.TryParse(parcalar[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tek))
        {
            return new Puan(tek, tek);
        }

        if (parcalar.Length == 2
            && double.TryParse(parcalar[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var alt)
            && double.TryParse(parcalar[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ust))
        {
            return new Puan(Math.Min(alt, ust), Math.Max(alt, ust));
        }

        return null;
    }

    public override string ToString() => AralikMi ? $"{Alt:0}-{Ust:0}" : $"{Alt:0}";
}
