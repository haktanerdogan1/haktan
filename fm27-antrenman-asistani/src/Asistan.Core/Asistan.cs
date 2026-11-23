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
            return await YazmaTestiIcAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _is.Release();
        }
    }

    private async Task<bool> YazmaTestiIcAsync(CancellationToken ct)
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

    // ---------------- Tek tık ----------------

    /// <summary>
    /// "Eksikleri gider": oyunu yeniden okur, taktik eksiklerine göre iki haftalık programı üretir ve takım programındaki
    /// bütün önerilen değişiklikleri (istenirse oyuncu önerilerini de) tek seferde uygular. Bu sürümde yazma henüz
    /// doğrulanmadıysa önce yazma testini yapar. Kullanıcı onayı arayüzde bu tek tıklamayla alınır.
    /// </summary>
    /// <summary>"Eksikleri gider" penceresindeki seçimleri kaydeder ve planı önbellekteki veriden yeniden üretir (oyuna yazmaz).</summary>
    public void SecenekleriAyarla(int sureGun, ProgramYogunlugu yogunluk, IEnumerable<TaktikAlan> hedefDisi)
    {
        AyarDegistir(a =>
        {
            a.PlanSuresiGun = sureGun is 7 or 14 or 28 ? sureGun : 14;
            a.Yogunluk = yogunluk;
            a.HedefDisiAlanlar = hedefDisi.ToHashSet();
        });
    }

    public async Task<IslemRaporu?> EksikleriGiderAsync(bool oyuncularDahil, CancellationToken ct = default)
    {
        if (!await _is.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            if (!await AnalizIcAsync(ct).ConfigureAwait(false) || Plan is null)
            {
                return null;
            }

            if (!YazmaIzni.Acik && !await YazmaTestiIcAsync(ct).ConfigureAwait(false))
            {
                Durum = "Tek tık durdu: yazma testi geçmedi, oyunda hiçbir plan değişikliği yapılmadı. Bağlantı sekmesindeki günlüğe bakın.";
                return null;
            }

            var secilen = Degisiklikler()
                .Where(d => d.Tur == DegisiklikTuru.Seans || oyuncularDahil)
                .ToList();

            if (secilen.Count == 0)
            {
                Durum = "Program zaten eksiklere uygun: değiştirilecek seans yok.";
                return null;
            }

            // "1 ay": MCPFM yalnızca bu ve gelecek haftayı yazabildiği için kalan haftalar oyun ilerledikçe otomatik yazılır.
            if (Ayarlar.PlanSuresiGun > 14 && Anlik?.Kimlik.IlkDuzenlenebilirGun is { } ilk)
            {
                Ayarlar.OtomatikUygula = true;
                Ayarlar.OtomatikBitis = ilk.AddDays(Ayarlar.PlanSuresiGun - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                _depo.AyarlariYaz(Ayarlar);
            }

            Durum = $"Eksikler gideriliyor: {secilen.Count} değişiklik gönderiliyor…";
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

    /// <summary>
    /// Otomatik mod (Ayarlar.OtomatikUygula): oyun tarihi yeni haftaya geçtiğinde ya da aktif taktik değiştiğinde
    /// eksikleri giderme kendiliğinden yeniden çalışır. Eklenti bunu düzenli aralıklarla çağırır.
    /// </summary>
    public async Task OtomatikKontrolAsync(CancellationToken ct = default)
    {
        if (!Ayarlar.OtomatikUygula || Mesgul)
        {
            return;
        }

        if (Surum is null)
        {
            var s = await _baglanti.SurumAsync(ct).ConfigureAwait(false);
            Surum = s.Basarili ? YetenekTesti.SurumSatiri(s.Veri!) : null;
        }

        // Otomatik modda yazma testi kendiliğinden yapılmaz: sürüm önceden doğrulanmış olmalı.
        if (!YazmaIzni.Acik)
        {
            return;
        }

        var ant = await _baglanti.AntrenmanAsync(ct).ConfigureAwait(false);
        var tak = await _baglanti.TaktiklerAsync(ct).ConfigureAwait(false);

        if (!ant.Basarili || !tak.Basarili)
        {
            return;
        }

        var hafta = string.Join(",", ant.Veri!.Weeks.Select(w => w.StartDate ?? "?"));

        // Süreli otomatik mod (ör. "1 ay") bitiş gününü geçince kendiliğinden kapanır.
        var bugun = ant.Veri.Weeks.SelectMany(w => w.Days).FirstOrDefault(d => d.Sessions.Any(x => x.CanEdit == true))?.Date;

        if (Ayarlar.OtomatikBitis is { } bitis && bugun is not null && string.CompareOrdinal(bugun, bitis) > 0)
        {
            Ayarlar.OtomatikUygula = false;
            Ayarlar.OtomatikBitis = null;
            _depo.AyarlariYaz(Ayarlar);
            Durum = "Seçilen plan süresi doldu; otomatik mod kapandı.";
            return;
        }
        var taktik = tak.Veri!.Slots.FirstOrDefault(s => s.IsActive)?.Name;
        var imza = hafta + "|" + taktik;

        if (imza == _sonOtomatikImza)
        {
            return;
        }

        _sonOtomatikImza = imza;
        await EksikleriGiderAsync(oyuncularDahil: false, ct).ConfigureAwait(false);
    }

    private string? _sonOtomatikImza;

    // ---------------- Canlı bağlantı ----------------

    public enum BaglantiDurumu { Bekleniyor, Canli, OyunYok, ApiYok }

    public BaglantiDurumu Baglanti { get; private set; } = BaglantiDurumu.Bekleniyor;
    public DateTimeOffset? SonCanliOkuma { get; private set; }
    public string? BaglantiHatasi { get; private set; }
    private string? _canliImza;

    /// <summary>
    /// Oyunu düzenli aralıklarla yoklar (masaüstü uygulaması ve eklenti birkaç saniyede bir çağırır). Bağlantı durumu güncellenir;
    /// taktik, taktik anlayışı ya da takvim haftası değiştiyse analiz ve plan kendiliğinden yenilenir. Oyuna hiçbir şey yazmaz
    /// (otomatik mod açıksa o ayrıca çalışır).
    /// </summary>
    public async Task CanliKontrolAsync(CancellationToken ct = default)
    {
        if (Mesgul)
        {
            return;
        }

        var tak = await _baglanti.TaktiklerAsync(ct).ConfigureAwait(false);

        if (!tak.Basarili)
        {
            var surum = await _baglanti.SurumAsync(ct).ConfigureAwait(false);
            Baglanti = surum.Basarili ? BaglantiDurumu.ApiYok : BaglantiDurumu.OyunYok;
            BaglantiHatasi = surum.Basarili
                ? "Oyun açık ama MCPFM API'si (5120) yanıt vermiyor."
                : "Football Manager 27 ya da MCPFM köprüsü açık değil.";
            return;
        }

        Baglanti = BaglantiDurumu.Canli;
        BaglantiHatasi = null;
        SonCanliOkuma = DateTimeOffset.Now;

        var aktif = tak.Veri!.Slots.FirstOrDefault(s => s.IsActive);
        var ta = aktif?.Effectiveness?.TacticalUnderstanding;
        var imza = string.Join("|", aktif?.Slot, aktif?.Name, ta?.Overall, ta?.InPossessionShape?.Understanding, ta?.OutOfPossessionShape?.Understanding,
            ta?.BuildUpStrategy?.Understanding, ta?.ChanceCreation?.Understanding, ta?.PassingStyle?.Understanding, ta?.PressingIntensity?.Understanding,
            ta?.PressingTrap?.Understanding, ta?.LineOfEngagement?.Understanding, ta?.AreasAdvice?.Text);

        if (imza != _canliImza || Anlik is null)
        {
            _canliImza = imza;
            await AnalizEtAsync(ct).ConfigureAwait(false);
        }

        await OtomatikKontrolAsync(ct).ConfigureAwait(false);
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
            GenelAnlayis = a.GenelAnlayis, Fizyoterapist = fizyo, Uyarilar = a.Uyarilar,
            OyunTavsiyesi = a.OyunTavsiyesi, OyunTavsiyesiOlumlu = a.OyunTavsiyesiOlumlu,
            Verimlilik = a.Verimlilik, GenelVerimlilik = a.GenelVerimlilik
        };
    }

    // ---------------- Antrenör ekibi ----------------

    private string EkipAnahtari => Anlik?.Kimlik.KariyerAnahtari ?? "_varsayilan";

    public List<Personel.Antrenor> Antrenorler()
    {
        var liste = _depo.Antrenorler(EkipAnahtari);

        // Oyuna bağlanmadan girilen ekip, ilk bağlantıda o kariyere taşınır.
        if (liste.Count == 0 && EkipAnahtari != "_varsayilan")
        {
            var bekleyen = _depo.Antrenorler("_varsayilan");

            if (bekleyen.Count > 0)
            {
                _depo.AntrenorleriYaz(EkipAnahtari, bekleyen);
                _depo.AntrenorleriYaz("_varsayilan", new());
                return bekleyen;
            }
        }

        return liste;
    }

    public void AntrenorDegistir(Action<List<Personel.Antrenor>> degisiklik)
    {
        var liste = Antrenorler();
        degisiklik(liste);
        _depo.AntrenorleriYaz(EkipAnahtari, liste);
    }

    public Personel.EkipAnalizi EkipAnalizi() => Personel.AntrenorEkibi.Analiz(Antrenorler(), Plan);

    public List<IslemRaporu> Gecmis() => Anlik is null ? new() : _depo.Islemler(Anlik.Kimlik.KariyerAnahtari);

    public List<AnalizKaydi> AnalizGecmisi() => Anlik is null ? new() : _depo.AnalizGecmisi(Anlik.Kimlik.KariyerAnahtari);
}
