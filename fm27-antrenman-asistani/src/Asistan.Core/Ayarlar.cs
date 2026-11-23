using Asistan.Core.Veri;

namespace Asistan.Core;

/// <summary>Günün 3 antrenman bölümünün ne kadar doldurulacağı.</summary>
public enum ProgramYogunlugu
{
    /// <summary>Günde en fazla 1 çalışma seansı, kalan bölümler dinlenme.</summary>
    Az,
    /// <summary>Günde en fazla 2 çalışma seansı.</summary>
    Normal,
    /// <summary>Uygun günlerde 3 bölümün tamamı çalışma; yük sınırları yükselir.</summary>
    Yogun
}

public sealed record SeansEslestirmesi(SeansAmaci Amac, TaktikAlan? Alan);

/// <summary>
/// Kullanıcının değiştirebildiği parametreler. Sayısal eşikler ürün varsayımıdır, oyun motorunun katsayıları değildir;
/// oyunda denenerek ayarlanmalıdır.
/// </summary>
public sealed class Ayarlar
{
    /// <summary>Kural seti sürümü: aynı girdi + aynı sürüm = aynı plan.</summary>
    public const string KuralSurumu = "1.0.0";

    // ---- Analiz
    /// <summary>Taktiksel anlayışta "yeterli" kabul edilen düzey (0-100).</summary>
    public double HedefAnlayis { get; set; } = 75;
    /// <summary>Bir alanın sorun sayılması için hedefin ne kadar altında olması gerektiği.</summary>
    public double AnlamliEksik { get; set; } = 5;
    public double YuksekOncelikEsigi { get; set; } = 0.18;
    public double OrtaOncelikEsigi { get; set; } = 0.08;

    // ---- Takım planı
    /// <summary>Normal bir günün tahmini yük üst sınırı (Düşük=1, Orta=2, Yüksek=3 puan toplamı).</summary>
    public int GunlukYukSiniri { get; set; } = 5;
    public int CiftMacHaftasiGunlukYukSiniri { get; set; } = 3;
    /// <summary>Aynı taktik seansının bir haftada en fazla kaç kez konacağı.</summary>
    public int AyniSeansHaftalikSinir { get; set; } = 2;
    /// <summary>Haftalık tahmini yük sınırı (tek maçlı hafta).</summary>
    public int HaftalikYukSiniri { get; set; } = 24;
    /// <summary>Takım programı haftalık sınırın bu oranına kadar doldurulur; kalan pay bireysel yoğunluk artışları içindir.</summary>
    public double HaftalikHedefOrani { get; set; } = 0.85;
    /// <summary>Takım yükü sınırın bu oranını geçtiyse hiçbir oyuncuya yüksek yoğunluk önerilmez.</summary>
    public double BireyselYukTavani { get; set; } = 0.9;

    // ---- Bireysel
    public int DusukKondisyonEsigi { get; set; } = 80;
    public int KritikKondisyonEsigi { get; set; } = 70;
    public int YuksekYogunlukKondisyonEsigi { get; set; } = 92;
    public int YuksekYogunlukAzamiYas { get; set; } = 23;
    /// <summary>Bir oyuncunun odağı bu kadar gün dolmadan yeniden değiştirilmez (yeni sakatlık hariç).</summary>
    public int OdakAsgariGun { get; set; } = 21;
    /// <summary>Yeni odağın mevcuda göre en az bu kadar puan (1-20 ölçeği) daha zayıf bir özelliği hedeflemesi gerekir.</summary>
    public double OdakDegisimEsigi { get; set; } = 1.5;

    // ---- Personel (MCPFM okuyamadığı için kullanıcı beyanı). null = bilinmiyor.
    public bool? FizyoterapistVar { get; set; }

    // ---- Güvenlik
    /// <summary>Yazma işlemleri yalnızca yetenek testinde "oyun içi doğrulandı" çıkan sürümlerde açılır.</summary>
    public List<string> DogrulanmisSurumler { get; set; } = new();
    /// <summary>İlk kullanımda otomatik uygulama kapalıdır; her uygulama kullanıcı onayıyla yapılır.</summary>
    public bool OtomatikUygula { get; set; }
    /// <summary>Otomatik modun kendiliğinden kapanacağı oyun günü ("yyyy-MM-dd"); "1 ay" seçeneği bunu kurar.</summary>
    public string? OtomatikBitis { get; set; }

    // ---- Eksikleri gider seçenekleri
    public ProgramYogunlugu Yogunluk { get; set; } = ProgramYogunlugu.Normal;
    /// <summary>Planlanacak süre (gün): 7 = 1 hafta, 14 = 2 hafta, 28 = 1 ay (oyun 2 haftayı düzenletir; kalanı otomatik modla yazılır).</summary>
    public int PlanSuresiGun { get; set; } = 14;
    /// <summary>Kullanıcının hedeflemek istemediği taktik alanları.</summary>
    public HashSet<TaktikAlan> HedefDisiAlanlar { get; set; } = new();

    // ---- Kullanıcı kararları
    public Dictionary<int, SeansEslestirmesi> SeansEslestirmeleri { get; set; } = new();
    /// <summary>"yyyy-MM-dd" veya "yyyy-MM-dd#slot" biçiminde kilitli gün/seanslar.</summary>
    public HashSet<string> KilitliSeanslar { get; set; } = new();
    /// <summary>Odağı ve yoğunluğu korunacak oyuncular.</summary>
    public HashSet<int> KilitliOyuncular { get; set; } = new();
    /// <summary>Önerilerden tamamen hariç tutulan oyuncular.</summary>
    public HashSet<int> HaricOyuncular { get; set; } = new();

    public bool SlotKilitli(string tarih, int slot) => KilitliSeanslar.Contains(tarih) || KilitliSeanslar.Contains($"{tarih}#{slot}");
}
