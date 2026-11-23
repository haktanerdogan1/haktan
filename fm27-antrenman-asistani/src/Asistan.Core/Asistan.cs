using System.Globalization;
using Asistan.Core.Analiz;
using Asistan.Core.Depo;
using Asistan.Core.Planlama;
using Asistan.Core.Uygulama;
using Asistan.Core.Veri;

namespace Asistan.Core;

/// <summary>Arayüzün kullandığı tek giriş noktası. Bir seferde tek işlem yürütür; sonuçları özellik olarak yayınlar.</summary>
public sealed class AntrenmanAsistani
{
    private readonly IOyunBaglantisi _baglanti;
    private readonly YerelDepo _depo;
    private readonly IslemYoneticisi _islem;
    private readonly SemaphoreSlim _is = new(1, 1);

    public AntrenmanAsistani(IOyunBaglantisi baglanti, YerelDepo depo)
    {
        _baglanti = baglanti;
        _depo = depo;
        _islem = new IslemYoneticisi(baglanti);
        Ayarlar = depo.AyarlariOku();
        Matris = depo.YetenekMatrisiOku();
    }

    public Ayarlar Ayarlar { get; private set; }
    public string? Surum { get; private set; }
    public Anlik? Anlik { get; private set; }
    public AnalizSonucu? Analiz { get; private set; }
    public TakimPlani? Plan { get; private set; }
    public IReadOnlyList<OyuncuOnerisi> Bireysel { get; private set; } = Array.Empty<OyuncuOnerisi>();
    public IslemRaporu? SonIslem { get; private set; }
    public YetenekMatrisi? Matris { get; private set; }
    public List<string> YazmaTestiGunlugu { get; } = new();

    /// <summary>Planın neden geçersizleştiği (kariyer/taktik/tarih değişimi). Null ise plan günceldir.</summary>
    public string? PlanUyarisi { get; private set; }
    public bool Mesgul => _is.CurrentCount == 0;
    public string Durum { get; private set; } = "Oyuna bağlanmadı.";
    public YerelDepo Depo => _depo;

    public (bool Acik, string Neden) YazmaIzni => IslemYoneticisi.YazmaIzni(Surum, Ayarlar);

    // ---------------- Bağlan + analiz et + planla ----------------

    public async Task<bool> AnalizEtAsync(CancellationToken ct = default)
    {
        if (!await _is.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            return await AnalizIcAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _is.Release();
        }
    }

    private async Task<bool> AnalizIcAsync(CancellationToken ct)
    {
        try
        {
            Durum = "Oyundan okunuyor…";
            var surum = await _baglanti.SurumAsync(ct).ConfigureAwait(false);
            Surum = surum.Basarili ? YetenekTesti.SurumSatiri(surum.Veri!) : null;

            var ant = await _baglanti.AntrenmanAsync(ct).ConfigureAwait(false);

            if (!ant.Basarili)
            {
                Durum = "Antrenman okunamadı: " + ant.Hata;
                return false;
            }

            var tak = await _baglanti.TaktiklerAsync(ct).ConfigureAwait(false);
            var kad = await _baglanti.KadroAsync(true, ct).ConfigureAwait(false);
            var yeni = Anlik.Olustur(ant.Veri!, tak, kad, Surum, Ayarlar, DateTimeOffset.Now);

            // K02/K10: kimlik değiştiyse önceki analiz ve bekleyen plan geçersizdir; kullanıcıya söylenir.
            PlanUyarisi = null;

            if (Anlik is { } eski)
            {
                if (eski.Kimlik.KariyerAnahtari != yeni.Kimlik.KariyerAnahtari)
                {
                    PlanUyarisi = $"Kariyer/takım değişti ({eski.Kimlik.TakimAdi} → {yeni.Kimlik.TakimAdi}): önceki analiz ve plan geçersiz sayıldı.";
                }
                else if (eski.Kimlik.TaktikParmakIzi != yeni.Kimlik.TaktikParmakIzi)
                {
                    PlanUyarisi = "Aktif taktik değişti: eski analiz geçersiz, yeni taktik temel alındı.";
                }
                else if (eski.Kimlik.HaftaAnahtari != yeni.Kimlik.HaftaAnahtari)
                {
                    PlanUyarisi = "Oyun tarihi ilerledi: plan güncel takvimle yeniden üretildi.";
                }
            }

            Anlik = yeni;
            Durum = "Analiz ediliyor…";
            Yenidenplanla();

            _depo.AnalizEkle(yeni.Kimlik.KariyerAnahtari, new AnalizKaydi(
                yeni.OkunmaZamani,
                yeni.Kimlik.IlkDuzenlenebilirGun?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                yeni.Kimlik.TaktikParmakIzi,
                yeni.Anlayis.ToDictionary(k => k.Key.ToString(), k => k.Value.Anlayis.VarMi ? k.Value.Anlayis.Deger!.Orta : (double?)null),
                yeni.GenelAnlayis.VarMi ? yeni.GenelAnlayis.Deger!.Orta : null));

            Durum = $"Hazır: plan üretildi, oyunda hiçbir değişiklik yapılmadı. ({DateTime.Now:HH:mm})";
            return true;
        }
        catch (Exception e)
        {
            Durum = "Beklenmeyen hata: " + e.Message;
            return false;
        }
    }

    /// <summary>Okunmuş veriden (önbellek) yeniden plan: ayar/kilit değişince oyuna gitmeden çalışır.</summary>
    public void Yenidenplanla()
    {
        if (Anlik is not { } a)
        {
            return;
        }

        // Kullanıcı eşleştirmesi değiştiyse seans türleri yeniden sınıflandırılmalı: Anlik, ayarlarla yeniden kurulur.
        Analiz = TaktikAnalizi.Calistir(a, Ayarlar);
        Plan = TakimPlanlayici.Olustur(a, Analiz, Ayarlar);
        Bireysel = BireyselPlanlayici.Olustur(a, Plan, Ayarlar, _depo.OdakDegisimleri(a.Kimlik.KariyerAnahtari));
    }

    public IReadOnlyList<Degisiklik> Degisiklikler()
    {
        var liste = new List<Degisiklik>();

        if (Plan is not null)
        {
            liste.AddRange(Plan.Degisiklikler.Where(h => h.Onerilen is not null).Select(Degisiklik.SeansDegisikligi));
        }

        foreach (var o in Bireysel.Where(o => o.Karar == OyuncuKarari.Oneri))
        {
            if (o.OdakDegisiyor) liste.Add(Degisiklik.OdakDegisikligi(o));
            if (o.YogunlukDegisiyor) liste.Add(Degisiklik.YogunlukDegisikligi(o));
        }

        return liste;
    }

    // ---------------- Uygula / geri al ----------------

    public async Task<IslemRaporu?> UygulaAsync(IReadOnlyCollection<string> secilenAnahtarlar, CancellationToken ct = default)
    {
        if (Plan is null || !await _is.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            Durum = "Uygulanıyor: işlemler gönderiliyor, tamamlanma henüz doğrulanmadı.";
            var secilen = Degisiklikler().Where(d => secilenAnahtarlar.Contains(d.Anahtar)).ToList();
            var rapor = await _islem.UygulaAsync(Plan.PlanAnahtari, secilen, GuncelOkuAsync, Surum, Ayarlar, ct: ct).ConfigureAwait(false);
            Kaydet(rapor);
            await SonrasindaYenileAsync(rapor, ct).ConfigureAwait(false);
            return rapor;
        }
        finally
        {
            _is.Release();
        }
    }

    public async Task<IslemRaporu?> GeriAlAsync(string islemId, CancellationToken ct = default)
    {
        var kariyer = Anlik?.Kimlik.KariyerAnahtari;
        var onceki = kariyer is null ? null : _depo.Islemler(kariyer, 200).FirstOrDefault(r => r.Id == islemId);

        if (onceki is null || !await _is.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            Durum = "Geri alınıyor…";
            var rapor = await _islem.GeriAlAsync(onceki, GuncelOkuAsync, Surum, Ayarlar, ct).ConfigureAwait(false);
            Kaydet(rapor);
            await SonrasindaYenileAsync(rapor, ct).ConfigureAwait(false);
            Durum = "Geri alma sonucu: " + rapor.Ozet;
            return rapor;
        }
        finally
        {
            _is.Release();
        }
    }

    // Oyunda bir şey değiştiyse farklar artık eskidir: oyun yeniden okunur ve plan güncel durumdan yeniden üretilir.
    private async Task SonrasindaYenileAsync(IslemRaporu rapor, CancellationToken ct)
    {
        if (rapor.Kalemler.Any(k => k.Durum is KalemDurumu.Dogrulandi or KalemDurumu.Belirsiz or KalemDurumu.Basarisiz))
        {
            await AnalizIcAsync(ct).ConfigureAwait(false);
        }

        Durum = rapor.Ozet;
    }

    private void Kaydet(IslemRaporu rapor)
    {
        SonIslem = rapor;

        if (rapor.KariyerAnahtari != "?")
        {
            _depo.IslemKaydet(rapor);
            var gun = Anlik?.Kimlik.IlkDuzenlenebilirGun ?? DateOnly.FromDateTime(DateTime.Today);

            foreach (var k in rapor.Kalemler.Where(k => k.Durum == KalemDurumu.Dogrulandi && k.Degisiklik.Tur == DegisiklikTuru.Odak))
            {
                _depo.OdakDegisimiKaydet(rapor.KariyerAnahtari, k.Degisiklik.OyuncuId, gun);
            }
        }
    }

    private async Task<(GuncelDurum?, string?)> GuncelOkuAsync(CancellationToken ct)
    {
        var ant = await _baglanti.AntrenmanAsync(ct).ConfigureAwait(false);

        if (!ant.Basarili)
        {
            return (null, ant.Hata);
        }

        var tak = await _baglanti.TaktiklerAsync(ct).ConfigureAwait(false);
        var kad = await _baglanti.KadroAsync(false, ct).ConfigureAwait(false);

        if (!tak.Basarili || !kad.Basarili)
        {
            return (null, "Takım kimliği doğrulanamadı: " + (tak.Hata ?? kad.Hata));
        }

        var a = Anlik.Olustur(ant.Veri!, tak, kad, Surum, Ayarlar, DateTimeOffset.Now);
        return (new GuncelDurum(a.Kimlik, ant.Veri!), null);
    }

    // ---------------- Yetenek matrisi ----------------

    public async Task<YetenekMatrisi?> YetenekTestiAsync(CancellationToken ct = default)
    {
        if (!await _is.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            Durum = "Yetenek testi (yalnızca okuma ve ön kontrol)…";
            Matris = await YetenekTesti.OkumaTestiAsync(_baglanti, Ayarlar, ct).ConfigureAwait(false);
            Surum = Matris.Surum ?? Surum;
            _depo.YetenekMatrisiYaz(Matris);
            Durum = "Yetenek matrisi güncellendi.";
            return Matris;
        }
        finally
        {
            _is.Release();
        }
    }

    public async Task<bool> YazmaTestiAsync(CancellationToken ct = default)
    {
        if (!await _is.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            Durum = "Oyun içi yazma testi: değiştir → geri oku → eski haline getir → geri oku…";
            var surum = await _baglanti.SurumAsync(ct).ConfigureAwait(false);

            if (!surum.Basarili)
            {
                Durum = "Sürüm okunamadı; yazma testi yapılmadı.";
                return false;
            }

            Surum = YetenekTesti.SurumSatiri(surum.Veri!);
            var (ok, gunluk) = await YetenekTesti.YazmaTestiAsync(_baglanti, ct).ConfigureAwait(false);
            YazmaTestiGunlugu.Clear();
            YazmaTestiGunlugu.AddRange(gunluk);

            if (ok && !Ayarlar.DogrulanmisSurumler.Contains(Surum))
            {
                Ayarlar.DogrulanmisSurumler.Add(Surum);
                _depo.AyarlariYaz(Ayarlar);
            }

            Durum = ok ? "Yazma bu sürümde oyun içinde doğrulandı." : "Yazma testi tamamlanamadı; yazma kapalı kalıyor. Günlüğe bakın.";
            return ok;
        }
        finally
        {
            _is.Release();
        }
    }

    // ---------------- Ayarlar ----------------

    public void AyarDegistir(Action<Ayarlar> degisiklik)
    {
        degisiklik(Ayarlar);
        _depo.AyarlariYaz(Ayarlar);

        // Seans eşleştirmesi ve personel beyanı Anlik'e işlenir; diğerleri yalnızca planı etkiler.
        if (Anlik is { } a)
        {
            Anlik = YenidenSiniflandir(a, Ayarlar);
        }

        Yenidenplanla();
    }

    private static Anlik YenidenSiniflandir(Anlik a, Ayarlar ayar)
    {
        var turler = a.SeansTurleri.Values.ToDictionary(t => t.Id, t =>
        {
            if (ayar.SeansEslestirmeleri.TryGetValue(t.Id, out var e))
            {
                return t with { Amac = e.Amac, Alan = e.Alan, KullaniciEslestirdi = true };
            }

            if (!t.KullaniciEslestirdi)
            {
                return t;
            }

            var (amac, alan) = Seanslar.Siniflandir(t.Ad, t.KategoriAdi);
            return t with { Amac = amac, Alan = alan, KullaniciEslestirdi = false };
        });

        var fizyo = ayar.FizyoterapistVar is { } beyan
            ? Okuma<bool>.Var(beyan, "Kullanıcı beyanı (Ayarlar)", DateTimeOffset.Now)
            : Okuma<bool>.Yok("MCPFM (personel okunmuyor)", DateTimeOffset.Now, VeriDurumu.Desteklenmiyor);

        return new Anlik
        {
            Kimlik = a.Kimlik, OkunmaZamani = a.OkunmaZamani, Gunler = a.Gunler, SeansTurleri = turler, Oyuncular = a.Oyuncular,
            OdakSecenekleri = a.OdakSecenekleri, YogunlukSecenekleri = a.YogunlukSecenekleri, Taktik = a.Taktik, Anlayis = a.Anlayis,
            GenelAnlayis = a.GenelAnlayis, Fizyoterapist = fizyo, Uyarilar = a.Uyarilar
        };
    }

    public List<IslemRaporu> Gecmis() => Anlik is null ? new() : _depo.Islemler(Anlik.Kimlik.KariyerAnahtari);

    public List<AnalizKaydi> AnalizGecmisi() => Anlik is null ? new() : _depo.AnalizGecmisi(Anlik.Kimlik.KariyerAnahtari);
}
