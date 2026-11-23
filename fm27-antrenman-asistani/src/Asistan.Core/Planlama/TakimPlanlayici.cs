using Asistan.Core.Analiz;
using Asistan.Core.Veri;

namespace Asistan.Core.Planlama;

public enum HucreDurumu
{
    /// <summary>Önerilen seans mevcutla aynı.</summary>
    Ayni,
    /// <summary>Değişiklik öneriliyor.</summary>
    Degisiyor,
    /// <summary>Maç seansı: üzerine yazılamaz (PLAN-01).</summary>
    Mac,
    /// <summary>Oyun bu seansın değiştirilmesine izin vermiyor ya da gün geçmişte (PLAN-01).</summary>
    Duzenlenemez,
    /// <summary>Kullanıcı kilitledi.</summary>
    Kilitli,
    /// <summary>Önerilecek uygun seans bulunamadı; mevcut korundu.</summary>
    Korundu
}

public enum HaftaTipi { Macsiz, TekMacli, CiftMacli }

public sealed record HucreOnerisi(
    DateOnly Tarih,
    int Slot,
    SeansTuru? Mevcut,
    SeansTuru? Onerilen,
    HucreDurumu Durum,
    string Amac,
    string Gerekce,
    YukSinifi TahminiYuk,
    IReadOnlyList<string> Uyarilar)
{
    public string Anahtar => $"{Tarih:yyyy-MM-dd}#{Slot}";
}

public sealed record GunPlani(DateOnly Tarih, string? Mac, string Baglam, IReadOnlyList<HucreOnerisi> Hucreler)
{
    public int TahminiYuk => Hucreler.Sum(h => (int)h.TahminiYuk);
}

public sealed record HaftaOzeti(int No, DateOnly Baslangic, HaftaTipi Tip, int MacSayisi, int TahminiYuk, int YukSiniri, IReadOnlyList<string> Notlar);

public sealed record TakimPlani(string PlanAnahtari, string KuralSurumu, IReadOnlyList<GunPlani> Gunler, IReadOnlyList<HaftaOzeti> Haftalar, IReadOnlyList<string> Notlar)
{
    public IEnumerable<HucreOnerisi> Degisiklikler => Gunler.SelectMany(g => g.Hucreler).Where(h => h.Durum == HucreDurumu.Degisiyor);
}

/// <summary>
/// Deterministik takım planlayıcısı. Sıra: maçlar ve kilitler sabitlenir → sağlık/personel kısıtları → maç çevresi hazırlık
/// ve toparlanma → kalan kapasite öncelikli taktik eksiklere → gelişim ve sosyal denge → tekrar ve toplam yük kontrolü.
/// Hazır bir şablon kopyalanmaz: her gün, maça uzaklığına ve haftanın maç sayısına göre ayrı hesaplanır.
/// </summary>
public static class TakimPlanlayici
{
    private enum Ihtiyac { Hazirlik, Toparlanma, Dinlenme, Taktik, DuranTop, Gelisim, Fiziksel, Sosyal }

    public static TakimPlani Olustur(Anlik a, AnalizSonucu analiz, Ayarlar ayar)
    {
        var notlar = new List<string>();
        var gunler = a.Gunler;

        if (gunler.Count == 0)
        {
            return new TakimPlani(a.Kimlik.PlanAnahtari, Ayarlar.KuralSurumu, Array.Empty<GunPlani>(), Array.Empty<HaftaOzeti>(),
                new[] { "Takvim okunamadı; plan üretilmedi." });
        }

        var maclar = gunler.Where(g => g.MacGunu).Select(g => g.Tarih).ToList();
        var ilk = gunler[0].Tarih;
        int HaftaNo(DateOnly t) => (t.DayNumber - ilk.DayNumber) / 7;
        var haftaMac = gunler.GroupBy(g => HaftaNo(g.Tarih)).ToDictionary(g => g.Key, g => g.Count(x => x.MacGunu));
        HaftaTipi Tip(int no) => haftaMac.GetValueOrDefault(no) switch { 0 => HaftaTipi.Macsiz, 1 => HaftaTipi.TekMacli, _ => HaftaTipi.CiftMacli };

        var secici = new SeansSecici(a);
        var taktikSirasi = analiz.Oncelikli.ToList();
        var haftalikSeansSayisi = new Dictionary<(int Hafta, int Seans), int>();
        var haftalikYuk = new Dictionary<int, int>();
        var sosyalKonuldu = new HashSet<int>();
        var fizikselKonuldu = new Dictionary<int, int>();
        var taktikImleci = 0;
        var gelisimImleci = 0;

        if (taktikSirasi.Count == 0)
        {
            notlar.Add(analiz.Kartlar.Any(k => k.Oncelik == Oncelik.Belirsiz)
                ? "Taktik anlayış verisi eksik olduğundan taktik seansları öncelik sırasıyla değil, dengeli dağıtıldı."
                : "Öncelikli taktik eksik yok; taktik kapasitesi genel gelişim ve dinlenmeye ayrıldı.");
        }

        if (!a.Fizyoterapist.VarMi)
        {
            notlar.Add("Fizyoterapist durumu oyundan okunamıyor (MCPFM personel bilgisi vermiyor). Toparlanma seansı yerine Dinlenme önerildi; "
                       + "Ayarlar'da fizyoterapistiniz olduğunu işaretlerseniz Toparlanma önerilir.");
        }
        else if (a.Fizyoterapist.Deger == false)
        {
            notlar.Add("Fizyoterapist yok (kullanıcı beyanı): Toparlanma seansları yerine Dinlenme önerildi.");
        }

        var gunPlanlari = new List<GunPlani>();

        foreach (var gun in gunler)
        {
            var hafta = HaftaNo(gun.Tarih);
            var tip = Tip(hafta);
            var sonrakiMac = maclar.Where(m => m > gun.Tarih).Select(m => (int?)(m.DayNumber - gun.Tarih.DayNumber)).FirstOrDefault();
            var oncekiMac = maclar.Where(m => m < gun.Tarih).Select(m => (int?)(gun.Tarih.DayNumber - m.DayNumber)).LastOrDefault();
            var (baglam, ihtiyaclar) = GunIhtiyaci(gun, tip, sonrakiMac, oncekiMac, maclar.Count == 0);
            var gunSiniri = tip == HaftaTipi.CiftMacli ? ayar.CiftMacHaftasiGunlukYukSiniri : ayar.GunlukYukSiniri;

            if (sonrakiMac == 1)
            {
                gunSiniri = Math.Min(gunSiniri, 3);
            }

            if (oncekiMac == 1)
            {
                gunSiniri = Math.Min(gunSiniri, 1);
            }

            // Önce değişmeyecek slotlar (maç, düzenlenemez, kilitli) yerleşir; yük ve tekrar hesabı onları da içerir.
            var hucreler = new HucreOnerisi?[gun.Slotlar.Count];
            var gunYuku = 0;
            var gunToparlanma = 0;

            for (var i = 0; i < gun.Slotlar.Count; i++)
            {
                var s = gun.Slotlar[i];
                var mevcut = a.Tur(s.SeansId);
                HucreDurumu? sabit = null;
                string? neden = null;

                if (mevcut?.Amac == SeansAmaci.Mac || (gun.MacGunu && !s.Duzenlenebilir))
                {
                    sabit = HucreDurumu.Mac;
                    neden = "Maç seansı; üzerine antrenman yazılmaz.";
                }
                else if (!s.Duzenlenebilir || (a.Kimlik.IlkDuzenlenebilirGun is { } ilkGun && gun.Tarih < ilkGun))
                {
                    sabit = HucreDurumu.Duzenlenemez;
                    neden = "Oyun bu seansın değiştirilmesine izin vermiyor (geçmiş gün veya sabit blok).";
                }
                else if (ayar.SlotKilitli(gun.TarihMetni, s.No))
                {
                    sabit = HucreDurumu.Kilitli;
                    neden = "Sizin kilidiniz; dokunulmadı.";
                }

                if (sabit is { } d)
                {
                    var yuk = mevcut?.Yuk ?? YukSinifi.Yok;
                    var amacAdi = Seanslar.AmacAdi(d == HucreDurumu.Mac ? SeansAmaci.Mac : mevcut?.Amac ?? SeansAmaci.Bilinmiyor);
                    hucreler[i] = new HucreOnerisi(gun.Tarih, s.No, mevcut, mevcut, d, amacAdi, neden!, yuk, Array.Empty<string>());
                    gunYuku += (int)yuk;
                    gunToparlanma += mevcut?.Amac == SeansAmaci.Toparlanma ? 1 : 0;
                    Say(haftalikSeansSayisi, hafta, mevcut);
                }
            }

            if (gunToparlanma > 1)
            {
                notlar.Add($"{gun.TarihMetni}: sabit/kilitli seanslarda aynı güne {gunToparlanma} toparlanma seansı var (PLAN-05). Değiştirilmedi, kontrol edin.");
            }

            // Sonra düzenlenebilir slotlar ihtiyaç sırasına göre doldurulur.
            var ihtiyacImleci = 0;

            for (var i = 0; i < gun.Slotlar.Count; i++)
            {
                if (hucreler[i] is not null)
                {
                    continue;
                }

                var s = gun.Slotlar[i];
                var mevcut = a.Tur(s.SeansId);
                var ihtiyac = ihtiyacImleci < ihtiyaclar.Count ? ihtiyaclar[ihtiyacImleci++] : Ihtiyac.Dinlenme;
                var uyarilar = new List<string>();
                SeansTuru? secim = null;
                string gerekce;

                switch (ihtiyac)
                {
                    case Ihtiyac.Toparlanma:
                        if (gunToparlanma >= 1)
                        {
                            secim = secici.Bul(SeansAmaci.Dinlenme);
                            gerekce = "Bu güne zaten bir toparlanma seansı var; ikincisi konmadı (PLAN-05).";
                        }
                        else if (a.Fizyoterapist.VarMi && a.Fizyoterapist.Deger == true && secici.Bul(SeansAmaci.Toparlanma) is { } top)
                        {
                            secim = top;
                            gerekce = baglam + " Maç yükünden toparlanma.";
                            gunToparlanma++;
                        }
                        else
                        {
                            secim = secici.Bul(SeansAmaci.Dinlenme);
                            gerekce = baglam + " Toparlanma gerekiyordu; " + (a.Fizyoterapist.VarMi ? "fizyoterapist yok" : "fizyoterapist durumu doğrulanamadı")
                                      + ", desteklenen alternatif olarak Dinlenme (PLAN-04).";
                            uyarilar.Add("Personel kısıtı nedeniyle alternatif seans.");
                        }
                        break;
                    case Ihtiyac.Hazirlik:
                        secim = secici.Bul(SeansAmaci.MacHazirligi);
                        gerekce = baglam + " Maç öncesi hazırlık; düşük yük.";
                        break;
                    case Ihtiyac.Taktik:
                        secim = TaktikSec(out gerekce);
                        break;
                    case Ihtiyac.DuranTop:
                        secim = secici.Bul(SeansAmaci.DuranTop);
                        gerekce = baglam + " Maça iki gün kala duran top çalışması; düşük yük.";
                        break;
                    case Ihtiyac.Sosyal when !sosyalKonuldu.Contains(hafta):
                        secim = secici.Bul(SeansAmaci.Sosyal);
                        gerekce = "Haftada bir takım kaynaşması: sosyal denge.";
                        if (secim is not null)
                        {
                            sosyalKonuldu.Add(hafta);
                        }
                        break;
                    case Ihtiyac.Fiziksel when tip != HaftaTipi.CiftMacli && fizikselKonuldu.GetValueOrDefault(hafta) < (tip == HaftaTipi.Macsiz ? 3 : 1):
                        secim = secici.Bul(SeansAmaci.Fiziksel, gelisimImleci++);
                        gerekce = tip == HaftaTipi.Macsiz
                            ? "Maçsız hafta: fiziksel yükleme için kapasite var."
                            : "Maçtan uzak gün: haftanın tek fiziksel seansı.";
                        if (secim is not null)
                        {
                            fizikselKonuldu[hafta] = fizikselKonuldu.GetValueOrDefault(hafta) + 1;
                        }
                        break;
                    case Ihtiyac.Gelisim:
                        secim = secici.Bul(SeansAmaci.Teknik, gelisimImleci++);
                        gerekce = "Bireysel ve teknik gelişim için kapasite.";
                        break;
                    case Ihtiyac.Fiziksel:
                    case Ihtiyac.Sosyal:
                        secim = secici.Bul(SeansAmaci.Teknik, gelisimImleci++);
                        gerekce = tip == HaftaTipi.CiftMacli && ihtiyac == Ihtiyac.Fiziksel
                            ? "Çift maçlı haftada fiziksel seans konmaz; teknik gelişim."
                            : $"Bu haftanın {IhtiyacAdi(ihtiyac).ToLowerInvariant()} kotası doldu; teknik gelişim.";
                        break;
                    default:
                        secim = secici.Bul(SeansAmaci.Dinlenme);
                        gerekce = ihtiyac == Ihtiyac.Dinlenme ? baglam + " Dinlenme." : "Bu hafta için ayrılan kota doldu; dinlenme.";
                        break;
                }

                // Yük kontrolü: sınır aşılıyorsa dinlenmeye düşürülür ve bu gösterilir.
                if (secim is not null && gunYuku + (int)secim.Yuk > gunSiniri && secim.Amac != SeansAmaci.Dinlenme)
                {
                    var dinlenme = secici.Bul(SeansAmaci.Dinlenme);
                    uyarilar.Add($"{secim.Ad} günlük yük sınırını ({gunSiniri}) aşacağı için konmadı.");
                    gerekce = $"Günlük tahmini yük sınırı ({gunSiniri}) doldu; dinlenme.";
                    secim = dinlenme;
                }

                var haftaHedefi = (int)Math.Floor(HaftaYukSiniri(tip, ayar) * ayar.HaftalikHedefOrani);

                if (secim is not null && haftalikYuk.GetValueOrDefault(hafta) + gunYuku + (int)secim.Yuk > haftaHedefi && secim.Yuk >= YukSinifi.Orta)
                {
                    uyarilar.Add($"Haftalık hedef yük ({haftaHedefi}) nedeniyle {secim.Ad} yerine dinlenme.");
                    gerekce = $"Haftalık hedef yüke ({haftaHedefi}) ulaşıldı; kalan pay bireysel yoğunluk için bırakıldı. Dinlenme.";
                    secim = secici.Bul(SeansAmaci.Dinlenme);
                }

                if (secim is null)
                {
                    hucreler[i] = new HucreOnerisi(gun.Tarih, s.No, mevcut, mevcut, HucreDurumu.Korundu, Seanslar.AmacAdi(mevcut?.Amac ?? SeansAmaci.Bilinmiyor),
                        $"\"{IhtiyacAdi(ihtiyac)}\" için oyunun seans listesinde eşleşen seans yok; mevcut seans korundu.", mevcut?.Yuk ?? YukSinifi.Yok,
                        new[] { "Seans eşleştirmesi eksik olabilir." });
                    gunYuku += (int)(mevcut?.Yuk ?? YukSinifi.Yok);
                    continue;
                }

                Say(haftalikSeansSayisi, hafta, secim);
                gunYuku += (int)secim.Yuk;
                var durum = mevcut?.Id == secim.Id ? HucreDurumu.Ayni : HucreDurumu.Degisiyor;
                var amac = secim.Alan is { } alan ? $"Taktik: {Seanslar.AlanAdi(alan)}" : Seanslar.AmacAdi(secim.Amac);
                hucreler[i] = new HucreOnerisi(gun.Tarih, s.No, mevcut, secim, durum, amac, gerekce, secim.Yuk, uyarilar);
            }

            haftalikYuk[hafta] = haftalikYuk.GetValueOrDefault(hafta) + gunYuku;
            gunPlanlari.Add(new GunPlani(gun.Tarih, gun.Mac, baglam, hucreler.Select(h => h!).ToList()));

            SeansTuru? TaktikSec(out string neden)
            {
                // Öncelikli kartlar sırayla döner; aynı seans haftada sınırı aşmaz (tekrar kontrolü).
                for (var deneme = 0; deneme < Math.Max(1, taktikSirasi.Count); deneme++)
                {
                    if (taktikSirasi.Count == 0)
                    {
                        break;
                    }

                    var kart = taktikSirasi[(taktikImleci + deneme) % taktikSirasi.Count];
                    var seans = kart.UygunSeanslar.FirstOrDefault(t => haftalikSeansSayisi.GetValueOrDefault((hafta, t.Id)) < ayar.AyniSeansHaftalikSinir);

                    if (seans is not null)
                    {
                        taktikImleci = (taktikImleci + deneme + 1) % taktikSirasi.Count;
                        var onc = kart.Oncelik == Oncelik.Yuksek ? "yüksek" : "orta";
                        neden = $"{kart.AlanAdi} anlayışı {kart.Anlayis.Deger} ({onc} öncelik). {kart.IlgiliTalimat}";
                        return seans;
                    }
                }

                // Öncelikli eksik yoksa ya da kotası dolduysa: en az kullanılan taktik seansı (dengeli dağılım).
                var yedek = secici.Hepsi(SeansAmaci.Taktik)
                    .Where(t => haftalikSeansSayisi.GetValueOrDefault((hafta, t.Id)) < ayar.AyniSeansHaftalikSinir)
                    .OrderBy(t => haftalikSeansSayisi.GetValueOrDefault((hafta, t.Id)))
                    .ThenBy(t => t.Id)
                    .FirstOrDefault();

                if (yedek is not null)
                {
                    neden = taktikSirasi.Count == 0
                        ? "Öncelikli eksik yok; taktik anlayışını korumak için dengeli dağıtılan seans."
                        : "Öncelikli seansların haftalık tekrar sınırı doldu; dengeli dağıtılan seans.";
                    return yedek;
                }

                neden = "Taktik seansı bulunamadı ya da tekrar sınırı doldu; gelişim seansı.";
                return secici.Bul(SeansAmaci.Teknik, gelisimImleci++);
            }
        }

        var haftalar = gunPlanlari
            .GroupBy(g => HaftaNo(g.Tarih))
            .Select(g =>
            {
                var tip = Tip(g.Key);
                var haftaNotlari = new List<string>
                {
                    tip switch
                    {
                        HaftaTipi.CiftMacli => "Çift maçlı hafta: fiziksel seans yok, günlük yük sınırı düşürüldü, maç çevresi toparlanma öncelikli.",
                        HaftaTipi.TekMacli => "Tek maçlı hafta: maç öncesi hazırlık ve maç sonrası toparlanma korunarak taktik çalışma.",
                        _ => "Maçsız hafta: fiziksel ve taktik yükleme için en geniş kapasite."
                    }
                };
                return new HaftaOzeti(g.Key + 1, g.First().Tarih, tip, haftaMac.GetValueOrDefault(g.Key), g.Sum(x => x.TahminiYuk), HaftaYukSiniri(tip, ayar), haftaNotlari);
            })
            .ToList();

        return new TakimPlani(a.Kimlik.PlanAnahtari, Ayarlar.KuralSurumu, gunPlanlari, haftalar, notlar);
    }

    private static int HaftaYukSiniri(HaftaTipi tip, Ayarlar ayar) => tip switch
    {
        HaftaTipi.Macsiz => ayar.HaftalikYukSiniri + 4,
        HaftaTipi.TekMacli => ayar.HaftalikYukSiniri,
        _ => ayar.HaftalikYukSiniri - 6
    };

    private static (string Baglam, List<Ihtiyac> Ihtiyaclar) GunIhtiyaci(Gun gun, HaftaTipi tip, int? sonraki, int? onceki, bool hicMacYok)
    {
        if (gun.MacGunu)
        {
            // Maç seansının kendisine dokunulmaz; günün düzenlenebilir diğer bölümleri yüksüz kalır.
            return ("Maç günü.", new() { Ihtiyac.Dinlenme, Ihtiyac.Dinlenme, Ihtiyac.Dinlenme });
        }

        if (onceki == 1 && sonraki == 1)
        {
            return ("Art arda maçların arası.", new() { Ihtiyac.Toparlanma, Ihtiyac.Hazirlik, Ihtiyac.Dinlenme });
        }

        if (onceki == 1)
        {
            return ("Maçtan bir gün sonra.", new() { Ihtiyac.Toparlanma, Ihtiyac.Dinlenme, Ihtiyac.Dinlenme });
        }

        if (sonraki == 1)
        {
            return ("Maçtan bir gün önce.", tip == HaftaTipi.CiftMacli
                ? new() { Ihtiyac.Hazirlik, Ihtiyac.Dinlenme, Ihtiyac.Dinlenme }
                : new() { Ihtiyac.Hazirlik, Ihtiyac.Taktik, Ihtiyac.Dinlenme });
        }

        if (sonraki == 2)
        {
            return ("Maça iki gün var.", tip == HaftaTipi.CiftMacli
                ? new() { Ihtiyac.Taktik, Ihtiyac.DuranTop, Ihtiyac.Dinlenme }
                : new() { Ihtiyac.Taktik, Ihtiyac.DuranTop, Ihtiyac.Taktik });
        }

        if (onceki == 2 && tip == HaftaTipi.CiftMacli)
        {
            return ("Yoğun haftada maçtan iki gün sonra.", new() { Ihtiyac.Taktik, Ihtiyac.Dinlenme, Ihtiyac.Dinlenme });
        }

        if (hicMacYok)
        {
            // Sezon öncesi/ara: hazır şablon değil, haftanın 7. günü dinlenme, diğer günler yükleme.
            return gun.Tarih.DayOfWeek == DayOfWeek.Sunday
                ? ("Maçsız dönem: haftalık dinlenme günü.", new() { Ihtiyac.Sosyal, Ihtiyac.Dinlenme, Ihtiyac.Dinlenme })
                : ("Maçsız dönem.", new() { Ihtiyac.Taktik, Ihtiyac.Fiziksel, Ihtiyac.Gelisim });
        }

        return ("Maçtan uzak gün.", new() { Ihtiyac.Taktik, Ihtiyac.Fiziksel, Ihtiyac.Sosyal, Ihtiyac.Gelisim });
    }

    private static string IhtiyacAdi(Ihtiyac i) => i switch
    {
        Ihtiyac.Hazirlik => "Maç hazırlığı",
        Ihtiyac.Toparlanma => "Toparlanma",
        Ihtiyac.Dinlenme => "Dinlenme",
        Ihtiyac.Taktik => "Taktik",
        Ihtiyac.DuranTop => "Duran top",
        Ihtiyac.Fiziksel => "Fiziksel",
        Ihtiyac.Sosyal => "Takım kaynaşması",
        _ => "Gelişim"
    };

    private static void Say(Dictionary<(int, int), int> sayac, int hafta, SeansTuru? tur)
    {
        if (tur is not null)
        {
            sayac[(hafta, tur.Id)] = sayac.GetValueOrDefault((hafta, tur.Id)) + 1;
        }
    }

    private sealed class SeansSecici
    {
        private readonly Anlik _a;

        public SeansSecici(Anlik a) => _a = a;

        // Aynı amaca birden fazla seans varsa önce tercih edilen adlar, sonra kimlik sırası: sonuç deterministik.
        private static readonly Dictionary<SeansAmaci, string[]> Tercih = new()
        {
            [SeansAmaci.MacHazirligi] = new[] { "önizleme", "preview", "hazırlık", "preparation", "practice", "prova" },
            [SeansAmaci.Dinlenme] = new[] { "dinlenme", "rest", "repos" }
        };

        public IReadOnlyList<SeansTuru> Hepsi(SeansAmaci amac)
        {
            var liste = _a.SeansTurleri.Values.Where(t => t.Amac == amac).OrderBy(t => t.Id).ToList();

            if (Tercih.TryGetValue(amac, out var tercihler))
            {
                liste = liste
                    .OrderBy(t =>
                    {
                        var ad = Seanslar.Kucuk(t.Ad);
                        var i = Array.FindIndex(tercihler, k => ad.Contains(Seanslar.Kucuk(k)));
                        return i < 0 ? int.MaxValue : i;
                    })
                    .ThenBy(t => t.Id)
                    .ToList();
            }

            return liste;
        }

        public SeansTuru? Bul(SeansAmaci amac, int sira = 0)
        {
            var liste = Hepsi(amac);
            return liste.Count == 0 ? null : liste[sira % liste.Count];
        }
    }
}
