using Asistan.Core;
using Asistan.Core.Analiz;
using Asistan.Core.Depo;
using Asistan.Core.Planlama;
using Asistan.Core.Uygulama;
using Asistan.Core.Veri;
using Xunit;

namespace Asistan.Tests;

/// <summary>Şartname bölüm 08: K01–K12 kabul senaryoları.</summary>
public class KabulTestleri : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "asistan-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_klasor))
        {
            Directory.Delete(_klasor, true);
        }
    }

    private AntrenmanAsistani Kur(SahteOyun oyun, bool yazmaDogrulanmis = true, bool? fizyo = true)
    {
        var depo = new YerelDepo(_klasor);
        var ayar = new Ayarlar { FizyoterapistVar = fizyo };
        if (yazmaDogrulanmis) ayar.DogrulanmisSurumler.Add("27.1.0");
        depo.AyarlariYaz(ayar);
        return new AntrenmanAsistani(oyun, depo);
    }

    private static SahteOyun TekMacliOyun(string pres = "40") => new(Senaryo.Antrenman(new[] { 5, 12 }), Senaryo.Taktik(pres), Senaryo.Kadro());

    // ---------------- K01 ----------------

    [Fact]
    public async Task K01_DusukPresAnlayisi_GerekceyleOnerilir()
    {
        var a = Kur(TekMacliOyun("40"));
        Assert.True(await a.AnalizEtAsync());

        var kart = a.Analiz!.Kartlar.First(k => k.Alan == TaktikAlan.PresYogunlugu);
        Assert.Equal(Oncelik.Yuksek, kart.Oncelik);
        Assert.Contains("PressingIntensity", kart.IlgiliTalimat);
        Assert.Contains(kart.UygunSeanslar, s => s.Id == 15);
        Assert.Contains(a.Plan!.Gunler.SelectMany(g => g.Hucreler), h => h.Onerilen?.Id == 15 && h.Gerekce.Contains("Pres yoğunluğu"));
    }

    [Fact]
    public async Task K01_VeriEksikligi_GercekDusukDegerdenAyrilir()
    {
        var taktik = Senaryo.Taktik("40");
        taktik.Slots[0].Effectiveness!.TacticalUnderstanding!.PressingIntensity = new TaktikAlanDto(); // okunamadı
        var a = Kur(new SahteOyun(Senaryo.Antrenman(new[] { 5 }), taktik, Senaryo.Kadro()));
        await a.AnalizEtAsync();

        var kart = a.Analiz!.Kartlar.First(k => k.Alan == TaktikAlan.PresYogunlugu);
        Assert.Equal(Oncelik.Belirsiz, kart.Oncelik);
        Assert.Equal(VeriDurumu.Eksik, kart.Anlayis.Durum);
        Assert.False(kart.SeansOnerilir);
        Assert.DoesNotContain(a.Plan!.Gunler.SelectMany(g => g.Hucreler), h => h.Gerekce.Contains("Pres yoğunluğu anlayışı"));
    }

    // ---------------- K02 ----------------

    [Fact]
    public async Task K02_YeniTaktik_EskiAnalizGecersizlesir_VeBekleyenPlanGonderilmez()
    {
        var oyun = TekMacliOyun("40");
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var anahtarlar = a.Degisiklikler().Select(d => d.Anahtar).ToList();
        Assert.NotEmpty(anahtarlar);

        oyun.Taktikler = Senaryo.Taktik("40", pres: "LessUrgent", slot: 2, stil: "Control"); // kullanıcı başka taktiğe geçti
        var rapor = await a.UygulaAsync(anahtarlar);

        Assert.Equal(IslemDurumu.Durduruldu, rapor!.Durum);
        Assert.Equal(0, oyun.UygulamaSayisi);

        await a.AnalizEtAsync();
        Assert.Contains("taktik değişti", a.PlanUyarisi);
        var kart = a.Analiz!.Kartlar.First(k => k.Alan == TaktikAlan.PresYogunlugu);
        Assert.True(kart.Onem < 1, "yeni taktik daha az pres istiyor: önem düşmeli");
        Assert.Contains("LessUrgent", kart.IlgiliTalimat);
    }

    // ---------------- K03 ----------------

    [Fact]
    public async Task K03_CiftMacHaftasi_MaclarKorunur_YukSiniriIhlalEdilmez()
    {
        var a = Kur(new SahteOyun(Senaryo.Antrenman(new[] { 2, 5, 12 }), Senaryo.Taktik("40"), Senaryo.Kadro()));
        await a.AnalizEtAsync();
        var plan = a.Plan!;
        var ayar = a.Ayarlar;

        Assert.Equal(HaftaTipi.CiftMacli, plan.Haftalar[0].Tip);
        // Maç seansları dokunulmaz.
        Assert.All(plan.Gunler.Where(g => g.Mac is not null).SelectMany(g => g.Hucreler).Where(h => h.Mevcut?.Id == 99), h => Assert.Equal(HucreDurumu.Mac, h.Durum));
        // Çift maçlı haftada fiziksel seans yok, günlük yük sınırı aşılmıyor.
        var ilkHafta = plan.Gunler.Where(g => g.Tarih < Senaryo.Pazartesi.AddDays(7) && g.Mac is null).ToList();
        Assert.DoesNotContain(ilkHafta.SelectMany(g => g.Hucreler), h => h.Durum == HucreDurumu.Degisiyor && h.Onerilen!.Amac == SeansAmaci.Fiziksel);
        Assert.All(ilkHafta, g => Assert.True(g.TahminiYuk <= ayar.CiftMacHaftasiGunlukYukSiniri, $"{g.Tarih}: {g.TahminiYuk}"));
        // Maç sonrası toparlanma, maç öncesi hazırlık açıklanır.
        var macSonrasi = plan.Gunler.First(g => g.Tarih == Senaryo.Pazartesi.AddDays(3));
        Assert.Equal(SeansAmaci.Toparlanma, macSonrasi.Hucreler[0].Onerilen!.Amac);
        var macOncesi = plan.Gunler.First(g => g.Tarih == Senaryo.Pazartesi.AddDays(4));
        Assert.Equal(SeansAmaci.MacHazirligi, macOncesi.Hucreler[0].Onerilen!.Amac);
        Assert.Contains("bir gün önce", macOncesi.Hucreler[0].Gerekce);
    }

    // ---------------- K04 ----------------

    [Fact]
    public async Task K04_MacErtelenmesi_GelecekGunlerYenidenDegerlendirilir_KilitliGunlereDokunulmaz()
    {
        var oyun = TekMacliOyun();
        var a = Kur(oyun);
        a.AyarDegistir(s => s.KilitliSeanslar.Add(Senaryo.Tarih(4))); // maç öncesi günü kullanıcı kilitledi
        await a.AnalizEtAsync();
        var once = a.Plan!.Gunler.First(g => g.Tarih == Senaryo.Pazartesi.AddDays(3));

        // Maç 5. günden 9. güne ertelendi.
        oyun.Antrenman = Senaryo.Antrenman(new[] { 9, 12 });
        await a.AnalizEtAsync();
        var plan = a.Plan!;

        Assert.All(plan.Gunler.First(g => g.Tarih == Senaryo.Pazartesi.AddDays(4)).Hucreler, h => Assert.Equal(HucreDurumu.Kilitli, h.Durum));
        var sonra = plan.Gunler.First(g => g.Tarih == Senaryo.Pazartesi.AddDays(8));
        Assert.Equal(SeansAmaci.MacHazirligi, sonra.Hucreler[0].Onerilen!.Amac);
        Assert.NotEqual(once.Baglam, plan.Gunler.First(g => g.Tarih == Senaryo.Pazartesi.AddDays(3)).Baglam);
    }

    // ---------------- K05 ----------------

    [Fact]
    public async Task K05_EksikFizyoterapist_ToparlanmaYerineAlternatifAciklanir()
    {
        var a = Kur(TekMacliOyun(), fizyo: false);
        await a.AnalizEtAsync();
        var macSonrasi = a.Plan!.Gunler.First(g => g.Tarih == Senaryo.Pazartesi.AddDays(6)).Hucreler[0];

        Assert.Equal(SeansAmaci.Dinlenme, macSonrasi.Onerilen!.Amac);
        Assert.Contains("PLAN-04", macSonrasi.Gerekce);
        Assert.Contains(a.Plan.Notlar, n => n.Contains("Fizyoterapist yok"));
        Assert.DoesNotContain(a.Plan.Gunler.SelectMany(g => g.Hucreler), h => h.Durum == HucreDurumu.Degisiyor && h.Onerilen!.Amac == SeansAmaci.Toparlanma);
    }

    [Fact]
    public async Task K05_FizyoterapistBilinmiyorsa_DegerUydurulmaz()
    {
        var a = Kur(TekMacliOyun(), fizyo: null);
        await a.AnalizEtAsync();
        Assert.Equal(VeriDurumu.Desteklenmiyor, a.Anlik!.Fizyoterapist.Durum);
        Assert.Contains(a.Plan!.Notlar, n => n.Contains("okunamıyor"));
    }

    // ---------------- K06 ----------------

    [Fact]
    public async Task K06_SakatKilitliOyuncu_KilitAsilmaz_NedeniGorunur()
    {
        var a = Kur(TekMacliOyun());
        await a.AnalizEtAsync();
        var emre = a.Bireysel.First(o => o.Oyuncu.Id == 5);

        Assert.Null(emre.OnerilenYogunluk);
        Assert.Contains(emre.YogunlukGerekcesi, g => g.Contains("Sağlık durumu nedeniyle değiştirilmedi"));
        Assert.DoesNotContain(a.Degisiklikler(), d => d.Anahtar == "yogunluk:5");
    }

    [Fact]
    public async Task K06_KullaniciKilidi_OneriUygulanmaz()
    {
        var a = Kur(TekMacliOyun());
        a.AyarDegistir(s => s.KilitliOyuncular.Add(4));
        await a.AnalizEtAsync();
        Assert.Equal(OyuncuKarari.KullaniciKilidi, a.Bireysel.First(o => o.Oyuncu.Id == 4).Karar);
        Assert.DoesNotContain(a.Degisiklikler(), d => d.OyuncuId == 4);
    }

    // ---------------- K07 ----------------

    [Fact]
    public async Task K07_KismiSonuc_TakimBazindaRaporlanir_DogrulananKaybolmaz()
    {
        var oyun = TekMacliOyun();
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var secim = a.Degisiklikler().Take(3).ToList();
        // İkinci kalemi oyun reddedecek: o günü düzenlenemez yap (ör. erişim hatası).
        var hedefGun = secim.First(d => d.Tur == DegisiklikTuru.Seans);
        var rapor = await a.UygulaAsync(secim.Select(d => d.Anahtar).ToList());

        Assert.Equal("Deneme SK", rapor!.TakimAdi);
        Assert.Equal(IslemDurumu.Dogrulandi, rapor.Durum);

        // Şimdi bir kalemi bozarak kısmi durumu üret.
        await a.AnalizEtAsync();
        var yeni = a.Degisiklikler().Where(d => d.Tur == DegisiklikTuru.Seans).Take(2).ToList();
        if (yeni.Count == 2)
        {
            var gun = oyun.Antrenman.Weeks.SelectMany(w => w.Days).First(d => d.Date == yeni[1].Tarih);
            oyun.Antrenman.SessionTypes.SelectMany(c => c.Sessions).ToList(); // değişmez
            gun.Sessions.First(s => s.Slot == yeni[1].Slot).CanEdit = null;
            var kismi = await a.UygulaAsync(yeni.Select(d => d.Anahtar).ToList());
            Assert.Equal(IslemDurumu.KismenUygulandi, kismi!.Durum);
            Assert.Contains(kismi.Kalemler, k => k.Durum == KalemDurumu.Dogrulandi);
            Assert.Contains("1/2", kismi.Ozet);
        }

        Assert.NotNull(hedefGun);
    }

    // ---------------- K08 ----------------

    [Fact]
    public async Task K08_ZamanAsimi_YenidenOkunur_KorTekrarVeSahteBasariYok()
    {
        var oyun = TekMacliOyun();
        oyun.ZamanAsimiCagrisi = 1;
        oyun.ZamanAsimindaYazildi = false;
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var secim = a.Degisiklikler().Take(3).Select(d => d.Anahtar).ToList();
        var rapor = await a.UygulaAsync(secim);

        Assert.Equal(1, oyun.UygulamaSayisi); // ilk belirsiz işlemden sonra durdu, kalanlar gönderilmedi
        Assert.NotEqual(IslemDurumu.Dogrulandi, rapor!.Durum);
        Assert.Equal(KalemDurumu.Basarisiz, rapor.Kalemler[0].Durum); // geri okumada hedef görülmedi
        Assert.All(rapor.Kalemler.Skip(1), k => Assert.Equal(KalemDurumu.Atlandi, k.Durum));
        Assert.DoesNotContain("Doğrulandı:", rapor.Ozet);
    }

    [Fact]
    public async Task K08_ZamanAsimiAmaYazildi_GeriOkumaylaDogrulanir()
    {
        var oyun = TekMacliOyun();
        oyun.ZamanAsimiCagrisi = 1;
        oyun.ZamanAsimindaYazildi = true;
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var rapor = await a.UygulaAsync(a.Degisiklikler().Take(1).Select(d => d.Anahtar).ToList());
        Assert.Equal(KalemDurumu.Dogrulandi, rapor!.Kalemler[0].Durum);
    }

    [Fact]
    public async Task K08_ZamanAsimiVeGeriOkumaYok_Belirsiz()
    {
        var oyun = TekMacliOyun();
        oyun.ZamanAsimiCagrisi = 1;
        oyun.GeriOkumaBozuk = true;
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var rapor = await a.UygulaAsync(a.Degisiklikler().Take(2).Select(d => d.Anahtar).ToList());
        Assert.Equal(KalemDurumu.Belirsiz, rapor!.Kalemler[0].Durum);
        Assert.Equal(IslemDurumu.BasarisizBelirsiz, rapor.Durum);
        Assert.Contains("Belirsiz işlemler otomatik tekrar gönderilmedi", rapor.Ozet);
    }

    // ---------------- K09 ----------------

    [Fact]
    public async Task K09_AyniPlaniYenidenGonderme_YeniDegisiklikYapilmaz()
    {
        var oyun = TekMacliOyun();
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var plan = a.Degisiklikler().ToList();
        var planAnahtari = a.Plan!.PlanAnahtari;
        var ilk = await a.UygulaAsync(plan.Select(d => d.Anahtar).ToList());
        Assert.Equal(IslemDurumu.Dogrulandi, ilk!.Durum);
        var yazilan = oyun.UygulamaSayisi;

        // Uygulamadan sonra plan oyundan yeniden okunur: artık fark kalmaz.
        Assert.Empty(a.Degisiklikler());

        // Aynı (eski) plan doğrudan tekrar gönderilse bile hiçbir şey yazılmaz.
        var yonetici = new IslemYoneticisi(oyun);
        var ikinci = await yonetici.UygulaAsync(planAnahtari, plan, async ct =>
        {
            var ant = await oyun.AntrenmanAsync(ct);
            var an = Anlik.Olustur(ant.Veri!, await oyun.TaktiklerAsync(ct), await oyun.KadroAsync(false, ct), null, a.Ayarlar, DateTimeOffset.Now);
            return (new GuncelDurum(an.Kimlik, ant.Veri!), null);
        }, "Football Manager 27 27.1.0", a.Ayarlar);
        Assert.Equal(yazilan, oyun.UygulamaSayisi);
        Assert.All(ikinci.Kalemler, k => Assert.Equal(KalemDurumu.ZatenUygulanmis, k.Durum));
        Assert.Equal(IslemDurumu.Dogrulandi, ikinci.Durum);
    }

    // ---------------- K10 ----------------

    [Fact]
    public async Task K10_KariyerVeTarihDegisimi_BekleyenPlanDurur()
    {
        var oyun = TekMacliOyun();
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var secim = a.Degisiklikler().Select(d => d.Anahtar).ToList();

        // Oyun bir hafta ilerledi.
        var ileri = Senaryo.Antrenman(new[] { 5, 12 });
        foreach (var (h, i) in ileri.Weeks.Select((h, i) => (h, i))) h.StartDate = Senaryo.Tarih(7 * (i + 1));
        oyun.Antrenman = ileri;
        var r1 = await a.UygulaAsync(secim);
        Assert.Equal(IslemDurumu.Durduruldu, r1!.Durum);
        Assert.Contains("Yeniden analiz", r1.Notlar[0]);

        // Başka kariyer yüklendi.
        oyun.Antrenman = Senaryo.Antrenman(new[] { 5, 12 });
        oyun.Kadro = Senaryo.Kadro();
        oyun.Kadro.TeamName = "Başka FK";
        var r2 = await a.UygulaAsync(secim);
        Assert.Equal(IslemDurumu.Durduruldu, r2!.Durum);
        Assert.Equal(0, oyun.UygulamaSayisi);

        await a.AnalizEtAsync();
        Assert.Contains("Kariyer/takım değişti", a.PlanUyarisi);
    }

    // ---------------- K11 ----------------

    [Fact]
    public async Task K11_DogrulanmamisSurum_YazmaKapanir_OkumaCalisir()
    {
        var oyun = TekMacliOyun();
        oyun.Surum = "bridge: MCPFM Bridge 0.14.0\nproduct: Football Manager 27 27.2.0";
        var a = Kur(oyun);
        Assert.True(await a.AnalizEtAsync());
        Assert.NotEmpty(a.Degisiklikler());
        Assert.False(a.YazmaIzni.Acik);

        var rapor = await a.UygulaAsync(a.Degisiklikler().Select(d => d.Anahtar).ToList());
        Assert.Equal(IslemDurumu.Durduruldu, rapor!.Durum);
        Assert.Equal(0, oyun.UygulamaSayisi);

        var m = await a.YetenekTestiAsync();
        Assert.Contains(m!.Satirlar, s => s.Alan.StartsWith("Taktiksel anlayış") && s.Okuma == Yetenek.Okunabiliyor);
        Assert.Contains(m.Satirlar, s => s.Alan.StartsWith("Personel") && s.Okuma == Yetenek.Desteklenmiyor);
        Assert.False(m.YazmaDogrulandi);
    }

    [Fact]
    public async Task K11_YazmaTesti_SurumuDogrular_VeOyunuEskiHalineGetirir()
    {
        var oyun = TekMacliOyun();
        oyun.Surum = "bridge: MCPFM Bridge 0.14.0\nproduct: Football Manager 27 27.2.0";
        var onceki = System.Text.Json.JsonSerializer.Serialize(oyun.Antrenman);
        var a = Kur(oyun, yazmaDogrulanmis: false);

        Assert.True(await a.YazmaTestiAsync());
        Assert.True(a.YazmaIzni.Acik);
        Assert.Equal(onceki, System.Text.Json.JsonSerializer.Serialize(oyun.Antrenman));
    }

    // ---------------- K12 ----------------

    [Fact]
    public async Task K12_GeriAlma_GecerliDegerlerGeriOkunarakDogrulanir_KalaniRaporlanir()
    {
        var oyun = TekMacliOyun();
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var secim = a.Degisiklikler().Where(d => d.Tur == DegisiklikTuru.Seans).Take(2).ToList();
        var once = secim.Select(d => d.Eski!.Id).ToList();
        var rapor = await a.UygulaAsync(secim.Select(d => d.Anahtar).ToList());
        Assert.Equal(IslemDurumu.Dogrulandi, rapor!.Durum);

        // Kullanıcı oyunda ikinci seansı elle değiştirdi: o kalem geri alınmamalı.
        var ikinci = secim[1];
        var gun = oyun.Antrenman.Weeks.SelectMany(w => w.Days).First(d => d.Date == ikinci.Tarih);
        var s = gun.Sessions.First(x => x.Slot == ikinci.Slot);
        s.Type = 21; s.Name = "Sürat";

        var geri = await a.GeriAlAsync(rapor.Id);
        Assert.NotNull(geri);
        Assert.Single(geri!.Kalemler);
        Assert.Equal(KalemDurumu.Dogrulandi, geri.Kalemler[0].Durum);
        Assert.Equal(once[0], geri.Kalemler[0].SonOkunan!.Id);
        Assert.Contains(geri.Notlar, n => n.Contains("geri alınmadı"));
    }

    // ---------------- Ek kurallar ----------------

    [Fact]
    public async Task PLAN01_MacVeDuzenlenemezGuneYazilmaz()
    {
        var a = Kur(new SahteOyun(Senaryo.Antrenman(new[] { 5, 12 }, duzenlenebilirIlkGun: 3), Senaryo.Taktik("40"), Senaryo.Kadro()));
        await a.AnalizEtAsync();
        Assert.All(a.Plan!.Gunler.Where(g => g.Tarih < Senaryo.Pazartesi.AddDays(3)).SelectMany(g => g.Hucreler),
            h => Assert.Equal(HucreDurumu.Duzenlenemez, h.Durum));
        Assert.DoesNotContain(a.Degisiklikler(), d => d.Tur == DegisiklikTuru.Seans && string.CompareOrdinal(d.Tarih, Senaryo.Tarih(3)) < 0);
    }

    [Fact]
    public async Task PLAN05_AyniGuneIkinciToparlanmaKonmaz()
    {
        var a = Kur(TekMacliOyun());
        await a.AnalizEtAsync();
        Assert.All(a.Plan!.Gunler, g => Assert.True(g.Hucreler.Count(h => h.Onerilen?.Amac == SeansAmaci.Toparlanma) <= 1));
    }

    [Fact]
    public async Task Determinizm_AyniGirdiAyniPlan()
    {
        var a1 = Kur(TekMacliOyun());
        await a1.AnalizEtAsync();
        var p1 = string.Join("|", a1.Plan!.Gunler.SelectMany(g => g.Hucreler).Select(h => $"{h.Anahtar}:{h.Onerilen?.Id}:{h.Gerekce}"));
        var b1 = string.Join("|", a1.Degisiklikler().Select(d => d.Anahtar + d.Hedef.Id));
        var a2 = Kur(TekMacliOyun());
        await a2.AnalizEtAsync();
        var p2 = string.Join("|", a2.Plan!.Gunler.SelectMany(g => g.Hucreler).Select(h => $"{h.Anahtar}:{h.Onerilen?.Id}:{h.Gerekce}"));
        Assert.Equal(p1, p2);
        Assert.Equal(b1, string.Join("|", a2.Degisiklikler().Select(d => d.Anahtar + d.Hedef.Id)));
    }

    [Fact]
    public async Task Bireysel_GencFormdaOyuncu_YuksekYogunluk_YorgunaDusuk()
    {
        var a = Kur(TekMacliOyun());
        await a.AnalizEtAsync();
        var deniz = a.Bireysel.First(o => o.Oyuncu.Id == 4);
        var ferhat = a.Bireysel.First(o => o.Oyuncu.Id == 6);
        Assert.Equal(2, deniz.OnerilenYogunluk?.Id);
        Assert.Equal(0, ferhat.OnerilenYogunluk?.Id);
        Assert.Contains("kritik", string.Join(" ", ferhat.YogunlukGerekcesi));
    }

    [Fact]
    public async Task Bireysel_RolunEnZayifAnahtarOzelligineOdak()
    {
        var a = Kur(TekMacliOyun());
        await a.AnalizEtAsync();
        var arda = a.Bireysel.First(o => o.Oyuncu.Id == 1);
        Assert.Equal(5, arda.OnerilenOdak?.Id); // Topsuz Alan (Movement 11)
        Assert.Contains("Topsuz alan", string.Join(" ", arda.OdakGerekcesi));
    }

    [Fact]
    public async Task Bireysel_KondisyonOkunamazsa_YuksekYogunlukOnerilmez()
    {
        var oyun = TekMacliOyun();
        oyun.Kadro = null;
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        Assert.DoesNotContain(a.Bireysel, o => o.OnerilenYogunluk?.Id == 2);
        Assert.Contains(a.Anlik!.Uyarilar, u => u.Contains("Kadro okunamadı"));
    }

    [Fact]
    public async Task Bireysel_Istikrar_YakinZamandaDegisenOdakTekrarDegismez()
    {
        var oyun = TekMacliOyun();
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        a.Depo.OdakDegisimiKaydet(a.Anlik!.Kimlik.KariyerAnahtari, 1, Senaryo.Pazartesi.AddDays(-5));
        a.Yenidenplanla();
        var arda = a.Bireysel.First(o => o.Oyuncu.Id == 1);
        Assert.Null(arda.OnerilenOdak);
        Assert.Contains("istikrar", string.Join(" ", arda.OdakGerekcesi));
    }

    [Fact]
    public async Task MacOynanirken_Durur()
    {
        var oyun = TekMacliOyun();
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        oyun.MacOynaniyor = true;
        var rapor = await a.UygulaAsync(a.Degisiklikler().Take(2).Select(d => d.Anahtar).ToList());
        Assert.Equal(0, oyun.UygulamaSayisi);
        Assert.NotEqual(IslemDurumu.Dogrulandi, rapor!.Durum);
    }

    [Fact]
    public async Task SifirDogrulanan_AslaTamamlandiGosterilmez()
    {
        var oyun = TekMacliOyun();
        oyun.GeriOkumaBozuk = true;
        var a = Kur(oyun);
        await a.AnalizEtAsync();
        var rapor = await a.UygulaAsync(a.Degisiklikler().Take(2).Select(d => d.Anahtar).ToList());
        Assert.Equal(0, rapor!.Dogrulanan);
        Assert.DoesNotContain("tamamland", rapor.Ozet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0/2", rapor.Ozet);
    }

    // ---------------- FM27 Türkçe ekranı ve tek tık ----------------

    [Theory]
    [InlineData("Oyun Kurulum", TaktikAlan.OyunKurma)]
    [InlineData("Pozisyon Üretimi", TaktikAlan.FirsatYaratma)]
    [InlineData("Karşılama Çizgisi", TaktikAlan.BaskiHatti)]
    [InlineData("Baskı Hattı", TaktikAlan.BaskiHatti)]
    [InlineData("Top Rakipteyken Diziliş", TaktikAlan.TopRakipteyken)]
    public void FM27Turkce_AlanAdlariTaninir(string ad, TaktikAlan alan) => Assert.Equal(alan, Seanslar.Siniflandir(ad, "").Alan);

    [Fact]
    public async Task OyununTavsiyesi_AlaniYuksekOncelikYapar_VeProgramaKoyar()
    {
        // Ekran görüntüsündeki durum: "Antrenmanlarda Baskı Hattı kurgularına daha fazla zaman ayırmamız gerekiyor."
        var taktik = Senaryo.Taktik("72", digerleri: "78");
        var ta = taktik.Slots[0].Effectiveness!.TacticalUnderstanding!;
        ta.LineOfEngagement = new TaktikAlanDto { Understanding = "68", MatchExposure = "50", TrainingContribution = "20" };
        ta.AreasAdvice = new TavsiyeDto { Text = "Antrenmanlarda Baskı Hattı kurgularına daha fazla zaman ayırmamız gerekiyor.", IsPositive = false };
        var a = Kur(new SahteOyun(Senaryo.Antrenman(new[] { 5, 12 }), taktik, Senaryo.Kadro()));
        await a.AnalizEtAsync();

        var kart = a.Analiz!.Kartlar.First();
        Assert.Equal(TaktikAlan.BaskiHatti, kart.Alan);
        Assert.Equal(Oncelik.Yuksek, kart.Oncelik);
        Assert.Contains("Oyunun kendi tavsiyesi", kart.Gozlem);
        var baskiSeanslari = a.Plan!.Gunler.Where(g => g.Tarih < Senaryo.Pazartesi.AddDays(7)).SelectMany(g => g.Hucreler).Count(h => h.Onerilen?.Id == 14);
        Assert.True(baskiSeanslari >= 3, $"ilk haftada {baskiSeanslari} Baskı Hattı seansı");
    }

    [Fact]
    public async Task TekTik_YazmaTestiniYapar_SeanslariUygular_VeDogrular()
    {
        var oyun = TekMacliOyun("40");
        var a = Kur(oyun, yazmaDogrulanmis: false);
        var rapor = await a.EksikleriGiderAsync(oyuncularDahil: false);

        Assert.NotNull(rapor);
        Assert.True(a.YazmaIzni.Acik);
        Assert.Equal(IslemDurumu.Dogrulandi, rapor!.Durum);
        Assert.All(rapor.Kalemler, k => Assert.Equal(DegisiklikTuru.Seans, k.Degisiklik.Tur));
        Assert.Empty(a.Degisiklikler().Where(d => d.Tur == DegisiklikTuru.Seans));
        Assert.Contains(oyun.Antrenman.Weeks.SelectMany(w => w.Days).SelectMany(d => d.Sessions), s => s.Type == 15);
    }

    [Fact]
    public async Task OtomatikMod_YeniHaftadaKendiligindenCalisir()
    {
        var oyun = TekMacliOyun("40");
        var a = Kur(oyun);
        a.AyarDegistir(s => s.OtomatikUygula = true);
        await a.OtomatikKontrolAsync();
        var ilk = oyun.UygulamaSayisi;
        Assert.True(ilk > 0);

        await a.OtomatikKontrolAsync(); // aynı hafta: tekrar yazmaz
        Assert.Equal(ilk, oyun.UygulamaSayisi);

        var yeni = Senaryo.Antrenman(new[] { 5, 12 });
        foreach (var (h, i) in yeni.Weeks.Select((h, i) => (h, i))) h.StartDate = Senaryo.Tarih(7 * (i + 1));
        oyun.Antrenman = yeni;
        await a.OtomatikKontrolAsync();
        Assert.True(oyun.UygulamaSayisi > ilk);
    }
}
