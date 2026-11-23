using System.Globalization;
using Asistan.Core.Analiz;
using Asistan.Core.Planlama;
using Asistan.Core.Uygulama;
using Asistan.Core.Veri;

namespace Asistan.Core.Arayuz;

/// <summary>Arayüzlerin (oyun içi panel ve web paneli) gösterdiği düz veri. Tüm metinler Türkçe, hazır biçimlenmiş.</summary>
public static class Gorunum
{
    private static readonly CultureInfo Tr = new("tr-TR");

    public static object Olustur(AntrenmanAsistani a)
    {
        var an = a.Anlik;
        var degisiklikler = a.Degisiklikler();
        var anahtarlar = degisiklikler.ToDictionary(d => d.Anahtar);

        return new
        {
            durum = a.Durum,
            baglanti = new
            {
                durum = a.Baglanti.ToString(),
                sonOkuma = a.SonCanliOkuma?.ToLocalTime().ToString("HH:mm:ss", Tr),
                saniyeOnce = a.SonCanliOkuma is { } so ? (int)(DateTimeOffset.Now - so).TotalSeconds : (int?)null,
                hata = a.BaglantiHatasi
            },
            mesgul = a.Mesgul,
            surum = a.Surum,
            yazmaIzni = new { acik = a.YazmaIzni.Acik, neden = a.YazmaIzni.Neden },
            planUyarisi = a.PlanUyarisi,
            kuralSurumu = Ayarlar.KuralSurumu,
            takim = an?.Kimlik.TakimAdi,
            okunma = an?.OkunmaZamani.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Tr),
            taktik = an is null ? null : an.Taktik.VarMi ? $"{an.Taktik.Deger!.Ad} ({an.Taktik.Deger.Bicim})" : "okunamadı",
            genelAnlayis = an?.GenelAnlayis.ToString(),
            oyunTavsiyesi = an?.OyunTavsiyesi,
            oyunTavsiyesiOlumlu = an?.OyunTavsiyesiOlumlu ?? false,
            otomatik = a.Ayarlar.OtomatikUygula,
            otomatikBitis = a.Ayarlar.OtomatikBitis,
            genelVerimlilik = an?.GenelVerimlilik,
            verimlilik = an?.Verimlilik.Select(v => new { baslik = v.Baslik, puan = v.Puan, metin = v.Metin, olumlu = v.Olumlu, antrenman = v.AntrenmanlaDuzelir, ne = v.Ne }).ToList(),
            secenekler = new
            {
                sure = a.Ayarlar.PlanSuresiGun,
                yogunluk = a.Ayarlar.Yogunluk.ToString(),
                haricAlanlar = a.Ayarlar.HedefDisiAlanlar.Select(x => x.ToString()).ToList()
            },
            onizleme = Onizleme(a),
            fizyoterapist = an is null ? null : new { deger = an.Fizyoterapist.Deger, durum = an.Fizyoterapist.Durum.ToString(), kaynak = an.Fizyoterapist.Kaynak },
            uyarilar = an?.Uyarilar ?? (IReadOnlyList<string>)Array.Empty<string>(),
            analizNotlari = a.Analiz?.GenelNotlar ?? (IReadOnlyList<string>)Array.Empty<string>(),
            planNotlari = a.Plan?.Notlar ?? (IReadOnlyList<string>)Array.Empty<string>(),
            kartlar = a.Analiz?.Kartlar.Select(k => Kart(k, an)).ToList(),
            haftalar = a.Plan?.Haftalar.Select(h => new
            {
                no = h.No,
                baslangic = h.Baslangic.ToString("dd MMMM", Tr),
                tip = h.Tip switch { HaftaTipi.CiftMacli => "Çift maçlı", HaftaTipi.TekMacli => "Tek maçlı", _ => "Maçsız" },
                macSayisi = h.MacSayisi,
                yuk = h.TahminiYuk,
                sinir = h.YukSiniri,
                notlar = h.Notlar
            }).ToList(),
            gunler = a.Plan?.Gunler.Select(g => new
            {
                tarih = g.Tarih.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                gun = g.Tarih.ToString("ddd d MMM", Tr),
                mac = g.Mac,
                baglam = g.Baglam,
                yuk = g.TahminiYuk,
                kilitli = a.Ayarlar.KilitliSeanslar.Contains(g.Tarih.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                hucreler = g.Hucreler.Select(h => new
                {
                    slot = h.Slot,
                    mevcut = h.Mevcut?.Ad,
                    onerilen = h.Onerilen?.Ad,
                    durum = h.Durum.ToString(),
                    amac = h.Amac,
                    gerekce = h.Gerekce,
                    yuk = YukAdi(h.TahminiYuk),
                    uyarilar = h.Uyarilar,
                    anahtar = h.Anahtar,
                    degisiklik = anahtarlar.ContainsKey("seans:" + h.Anahtar) ? "seans:" + h.Anahtar : null
                }).ToList()
            }).ToList(),
            oyuncular = a.Bireysel.Select(o => new
            {
                id = o.Oyuncu.Id,
                ad = o.Oyuncu.Ad,
                rol = o.Oyuncu.RolAdi ?? o.Oyuncu.RolAnahtari,
                pozisyon = o.Oyuncu.Pozisyon,
                yas = o.Oyuncu.Yas.ToString(),
                kondisyon = o.Oyuncu.Kondisyon.VarMi ? $"%{o.Oyuncu.Kondisyon.Deger}" : "okunamadı",
                sakat = o.Oyuncu.Sakat.VarMi && o.Oyuncu.Sakat.Deger,
                isYuku = o.Oyuncu.IsYuku.ToString(),
                odak = o.Oyuncu.Odak?.Ad ?? "Yok",
                yogunluk = o.Oyuncu.YogunlukOtomatik ? "Otomatik" : o.Oyuncu.Yogunluk?.Ad ?? "?",
                onerilenOdak = o.OdakDegisiyor ? o.OnerilenOdak!.Ad : null,
                onerilenYogunluk = o.YogunlukDegisiyor ? o.OnerilenYogunluk!.Ad : null,
                karar = o.Karar.ToString(),
                kararMetni = KararAdi(o.Karar),
                odakGerekcesi = o.OdakGerekcesi,
                yogunlukGerekcesi = o.YogunlukGerekcesi,
                uyarilar = o.Uyarilar,
                guven = (int)Math.Round(o.Guven * 100),
                kilitli = a.Ayarlar.KilitliOyuncular.Contains(o.Oyuncu.Id),
                haric = a.Ayarlar.HaricOyuncular.Contains(o.Oyuncu.Id),
                degisiklikler = new[] { $"odak:{o.Oyuncu.Id}", $"yogunluk:{o.Oyuncu.Id}" }.Where(anahtarlar.ContainsKey).ToList()
            }).ToList(),
            gelisim = an is null ? null : BireyselGelisim.Olustur(an).Select(r =>
            {
                var oneri = a.Bireysel.FirstOrDefault(b => b.Oyuncu.Id == r.Oyuncu.Id);
                object Rol(RolDegerlendirmesi d) => new
                {
                    rol = d.Rol?.Ad ?? d.RolAdi,
                    tanindi = d.Tanindi,
                    uyum = d.Uyum is { } u ? Math.Round(u, 1) : (double?)null,
                    ozellikler = d.Ozellikler.Select(z => new { ad = z.Ad, kod = z.Anahtar, deger = z.Deger, oncelikli = z.Oncelikli, zayif = z.Zayif }).ToList(),
                    alternatifler = d.Alternatifler.Select(x => new { rol = x.Rol.Ad, uyum = Math.Round(x.Uyum, 1) }).ToList()
                };
                return new
                {
                    id = r.Oyuncu.Id,
                    ad = r.Oyuncu.Ad,
                    pozisyon = r.Oyuncu.Pozisyon,
                    yas = r.Oyuncu.Yas.ToString(),
                    odak = r.Oyuncu.Odak?.Ad ?? "Yok",
                    ip = Rol(r.TopaSahipken),
                    oop = Rol(r.TopRakipteyken),
                    enZayif = r.EnZayif.Select(z => new { ad = z.Ad, deger = z.Deger }).ToList(),
                    odaklar = r.Odaklar.Select(x => new { ad = x.Secenek.Ad, grup = x.Grup.Ad, puan = Math.Round(x.Puan, 1), gerekce = x.Gerekce }).ToList(),
                    evre = r.GelisimEvresi,
                    karakteristikler = r.Karakteristikler,
                    notlar = r.Notlar,
                    oneriOdak = oneri?.OdakDegisiyor == true ? oneri.OnerilenOdak!.Ad : null,
                    oneriGerekce = oneri?.OdakGerekcesi,
                    degisiklik = anahtarlar.ContainsKey($"odak:{r.Oyuncu.Id}") ? $"odak:{r.Oyuncu.Id}" : null
                };
            }).ToList(),
            degisiklikler = degisiklikler.Select(d => new
            {
                anahtar = d.Anahtar,
                tur = d.Tur switch { DegisiklikTuru.Seans => "Takım seansı", DegisiklikTuru.Odak => "Ek odak", _ => "Yoğunluk" },
                baslik = d.Baslik,
                eski = d.Eski?.Ad ?? "—",
                hedef = d.Hedef.Ad,
                gerekce = d.Gerekce
            }).ToList(),
            sonIslem = a.SonIslem is null ? null : Rapor(a.SonIslem),
            gecmis = a.Gecmis().Select(r => new
            {
                id = r.Id,
                zaman = r.Baslangic.ToLocalTime().ToString("dd.MM HH:mm", Tr),
                durum = DurumAdi(r.Durum),
                ozet = r.Ozet,
                geriAlma = r.GeriAlma,
                geriAlinabilir = !r.GeriAlma && r.Kalemler.Any(k => k.Durum == KalemDurumu.Dogrulandi)
            }).ToList(),
            analizGecmisi = a.AnalizGecmisi().Select(k => new
            {
                zaman = k.Zaman.ToLocalTime().ToString("dd.MM HH:mm", Tr),
                ilkGun = k.IlkGun,
                genel = k.Genel,
                anlayis = k.Anlayis
            }).ToList(),
            matris = a.Matris is null ? null : new
            {
                surum = a.Matris.Surum,
                zaman = a.Matris.Zaman.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Tr),
                yazmaDogrulandi = a.Matris.YazmaDogrulandi,
                satirlar = a.Matris.Satirlar.Select(s => new { alan = s.Alan, okuma = YetenekAdi(s.Okuma), yazma = YetenekAdi(s.Yazma), not = s.Not }).ToList()
            },
            yazmaTestiGunlugu = a.YazmaTestiGunlugu,
            seansTurleri = an?.SeansTurleri.Values.OrderBy(t => t.KategoriId).ThenBy(t => t.Id).Select(t => new
            {
                id = t.Id,
                ad = t.Ad,
                kategori = t.KategoriAdi,
                amac = t.Amac.ToString(),
                amacAdi = Seanslar.AmacAdi(t.Amac),
                alan = t.Alan?.ToString(),
                kullanici = t.KullaniciEslestirdi
            }).ToList(),
            ayarlar = new
            {
                a.Ayarlar.HedefAnlayis,
                a.Ayarlar.GunlukYukSiniri,
                a.Ayarlar.CiftMacHaftasiGunlukYukSiniri,
                a.Ayarlar.HaftalikYukSiniri,
                a.Ayarlar.AyniSeansHaftalikSinir,
                a.Ayarlar.DusukKondisyonEsigi,
                a.Ayarlar.KritikKondisyonEsigi,
                a.Ayarlar.YuksekYogunlukKondisyonEsigi,
                a.Ayarlar.YuksekYogunlukAzamiYas,
                a.Ayarlar.OdakAsgariGun,
                a.Ayarlar.FizyoterapistVar,
                dogrulanmisSurumler = a.Ayarlar.DogrulanmisSurumler
            },
            amaclar = Enum.GetValues<SeansAmaci>().Select(x => new { kod = x.ToString(), ad = Seanslar.AmacAdi(x) }).ToList(),
            alanlar = Enum.GetValues<TaktikAlan>().Select(x => new { kod = x.ToString(), ad = Seanslar.AlanAdi(x) }).ToList()
        };
    }

    // Seçilen süre ve yoğunlukla programın ne yapacağı: alan başına taktik seans sayısı, haftalık yük, değişiklik sayısı.
    private static object? Onizleme(AntrenmanAsistani a)
    {
        if (a.Plan is not { } plan)
        {
            return null;
        }

        var hucreler = plan.Gunler.SelectMany(g => g.Hucreler).Where(h => h.Durum != HucreDurumu.SureDisi).ToList();
        var taktik = hucreler.Where(h => h.Onerilen?.Alan is not null).GroupBy(h => h.Onerilen!.Alan!.Value)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());
        var gunSayisi = Math.Max(1, plan.Gunler.Count(g => g.Hucreler.Any(h => h.Durum != HucreDurumu.SureDisi)));

        return new
        {
            taktikSeans = taktik,
            toplamTaktik = taktik.Values.Sum(),
            haftalikTaktik = Math.Round(taktik.Values.Sum() * 7.0 / gunSayisi, 1),
            calismaSeansi = hucreler.Count(h => h.Onerilen is { } o && o.Amac is not (SeansAmaci.Dinlenme or SeansAmaci.Mac or SeansAmaci.Bilinmiyor)),
            dinlenme = hucreler.Count(h => h.Onerilen?.Amac == SeansAmaci.Dinlenme),
            degisiklik = plan.Degisiklikler.Count(),
            haftalar = plan.Haftalar.Select(h => new { no = h.No, yuk = h.TahminiYuk, sinir = h.YukSiniri }).ToList(),
            sureNotu = a.Ayarlar.PlanSuresiGun > 14
                ? "Oyun takvimi aylık, ama MCPFM köprüsü yalnızca bu ve gelecek haftayı yazabiliyor. İlk iki hafta şimdi yazılır; kalan haftalar oyun ilerledikçe otomatik mod tarafından yazılır."
                : null
        };
    }

    public static object Kart(SorunKarti k, Anlik? an = null) => new
    {
        mac = an is not null && an.Anlayis.TryGetValue(k.Alan, out var o1) && o1.MacMaruziyeti.VarMi ? o1.MacMaruziyeti.Deger!.Orta : (double?)null,
        antrenman = an is not null && an.Anlayis.TryGetValue(k.Alan, out var o2) && o2.AntrenmanKatkisi.VarMi ? o2.AntrenmanKatkisi.Deger!.Orta : (double?)null,
        deger = k.Anlayis.VarMi ? k.Anlayis.Deger!.Orta : (double?)null,
        oyunIstiyor = k.Gozlem.Contains("Oyunun kendi tavsiyesi"),
        alan = k.AlanAdi,
        kod = k.Alan.ToString(),
        oncelik = OncelikAdi(k.Oncelik),
        oncelikKod = k.Oncelik.ToString(),
        anlayis = k.Anlayis.ToString(),
        veriDurumu = k.Anlayis.Durum.ToString(),
        skor = Math.Round(k.Skor, 3),
        guven = (int)Math.Round(k.Guven * 100),
        onem = k.Onem,
        gozlem = k.Gozlem,
        talimat = k.IlgiliTalimat,
        oneri = k.Oneri,
        sinirlar = k.Sinirlar,
        seanslar = k.UygunSeanslar.Select(s => s.Ad).ToList()
    };

    public static object Rapor(IslemRaporu r) => new
    {
        id = r.Id,
        takim = r.TakimAdi,
        durum = DurumAdi(r.Durum),
        durumKod = r.Durum.ToString(),
        ozet = r.Ozet,
        geriAlma = r.GeriAlma,
        notlar = r.Notlar,
        kalemler = r.Kalemler.Select(k => new
        {
            baslik = k.Degisiklik.Baslik,
            eski = k.OncekiOkunan?.Ad ?? k.Degisiklik.Eski?.Ad ?? "—",
            hedef = k.Degisiklik.Hedef.Ad,
            okunan = k.SonOkunan?.Ad,
            durum = KalemAdi(k.Durum),
            durumKod = k.Durum.ToString(),
            hata = k.Hata
        }).ToList()
    };

    public static string OncelikAdi(Oncelik o) => o switch
    {
        Oncelik.Yuksek => "Yüksek öncelik",
        Oncelik.Orta => "Orta öncelik",
        Oncelik.Dusuk => "Düşük öncelik",
        Oncelik.Yok => "Sorun yok",
        _ => "Veri yok"
    };

    public static string KararAdi(OyuncuKarari k) => k switch
    {
        OyuncuKarari.Oneri => "Öneri var",
        OyuncuKarari.DegisiklikYok => "Değişiklik gerekmiyor",
        OyuncuKarari.OyunKilidi => "Oyun/sağlık kilidi",
        OyuncuKarari.KullaniciKilidi => "Korunuyor",
        _ => "Hariç tutuldu"
    };

    public static string DurumAdi(IslemDurumu d) => d switch
    {
        IslemDurumu.Hazir => "Hazır",
        IslemDurumu.Uygulaniyor => "Uygulanıyor",
        IslemDurumu.Dogrulandi => "Doğrulandı",
        IslemDurumu.KismenUygulandi => "Kısmen uygulandı",
        IslemDurumu.Durduruldu => "Durduruldu",
        _ => "Başarısız / belirsiz"
    };

    public static string KalemAdi(KalemDurumu d) => d switch
    {
        KalemDurumu.Hazir => "Gönderilmedi",
        KalemDurumu.Uygulaniyor => "Gönderildi, doğrulanmadı",
        KalemDurumu.Dogrulandi => "Doğrulandı",
        KalemDurumu.ZatenUygulanmis => "Zaten uygulanmış",
        KalemDurumu.Cakisma => "Oyunda değişmiş, dokunulmadı",
        KalemDurumu.Atlandi => "Atlandı",
        KalemDurumu.Basarisiz => "Başarısız",
        _ => "Belirsiz"
    };

    public static string YukAdi(YukSinifi y) => y switch
    {
        YukSinifi.Yok => "yok",
        YukSinifi.Dusuk => "düşük",
        YukSinifi.Orta => "orta",
        _ => "yüksek"
    };

    public static string YetenekAdi(Yetenek y) => y switch
    {
        Yetenek.Okunabiliyor => "Okunabiliyor",
        Yetenek.Yazilabiliyor => "Yazılabiliyor (ön kontrol)",
        Yetenek.OyunIciDogrulandi => "Oyun içi doğrulandı",
        Yetenek.Kismen => "Kısmen",
        Yetenek.Desteklenmiyor => "Desteklenmiyor",
        _ => "Denenmedi"
    };
}
