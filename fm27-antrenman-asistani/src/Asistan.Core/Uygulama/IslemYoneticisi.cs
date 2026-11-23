using Asistan.Core.Veri;

namespace Asistan.Core.Uygulama;

/// <summary>Uygulamadan hemen önce yapılan kimlik okuması: kariyer, takım, tarih, taktik ve sürüm.</summary>
public sealed record GuncelDurum(Kimlik Kimlik, AntrenmanDto Antrenman);

/// <summary>
/// Oku → farkları göster → kullanıcı seçimi → ön kontrol → sıralı uygula → yeniden oku → sonuç raporu.
/// WRITE-01 tek yazma, WRITE-02 belirsiz durumda dur, WRITE-03 kayıt, WRITE-04 zaman aşımında sahte başarı yok,
/// WRITE-05 sonuç takım bazında.
/// </summary>
public sealed class IslemYoneticisi
{
    private readonly IOyunBaglantisi _baglanti;
    private readonly SemaphoreSlim _yazmaKilidi = new(1, 1);

    public IslemYoneticisi(IOyunBaglantisi baglanti) => _baglanti = baglanti;

    public bool YazmaSuruyor => _yazmaKilidi.CurrentCount == 0;

    /// <summary>Yazma bu oyun/bağlantı sürümünde açık mı (K11)?</summary>
    /// <summary>Doğrulanmış sürüm kaydı: "sürüm" (hepsi) ya da "sürüm#Seans", "sürüm#Odak", "sürüm#Yogunluk" (tek tür).</summary>
    public static bool TurDogrulandi(string? surum, Ayarlar ayar, DegisiklikTuru tur) =>
        !string.IsNullOrWhiteSpace(surum) && ayar.DogrulanmisSurumler.Any(s =>
        {
            var p = s.Split('#');
            return surum.Contains(p[0], StringComparison.OrdinalIgnoreCase) && (p.Length == 1 || p[1] == tur.ToString());
        });

    /// <summary>Yazma bu oyun/bağlantı sürümünde açık mı (K11)? En az bir tür doğrulanmışsa açık sayılır; tür bazında ayrıca bakılır.</summary>
    public static (bool Acik, string Neden) YazmaIzni(string? surum, Ayarlar ayar)
    {
        if (string.IsNullOrWhiteSpace(surum))
        {
            return (false, "Oyun sürümü okunamadı; sürüm doğrulanmadan oyuna yazılmaz.");
        }

        var turler = Enum.GetValues<DegisiklikTuru>().Where(t => TurDogrulandi(surum, ayar, t)).ToList();

        if (turler.Count == 3)
        {
            return (true, "Bu sürümde yazma, yazma testinde oyun içinde doğrulandı.");
        }

        if (turler.Count > 0)
        {
            var ad = new Dictionary<DegisiklikTuru, string> { [DegisiklikTuru.Seans] = "takım seansı", [DegisiklikTuru.Odak] = "ek odak", [DegisiklikTuru.Yogunluk] = "yoğunluk" };
            return (true, $"Doğrulanan: {string.Join(", ", turler.Select(t => ad[t]))}. Diğer türler yazılmaz; yazma testini tekrarlayarak açabilirsiniz.");
        }

        return (false, "Bu oyun/bağlantı sürümünde yazma henüz doğrulanmadı. Bağlantı ekranındaki \"Yazma testi\" ile doğrulayın; "
                       + "o zamana kadar yalnızca okuma ve öneri çalışır.");
    }

    public async Task<IslemRaporu> UygulaAsync(
        string planAnahtari,
        IReadOnlyList<Degisiklik> secilenler,
        Func<CancellationToken, Task<(GuncelDurum? Durum, string? Hata)>> guncelOku,
        string? surum,
        Ayarlar ayar,
        bool geriAlma = false,
        CancellationToken ct = default)
    {
        var rapor = new IslemRaporu
        {
            Id = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"),
            TakimAdi = "?",
            KariyerAnahtari = "?",
            PlanAnahtari = planAnahtari,
            Baslangic = DateTimeOffset.UtcNow,
            GeriAlma = geriAlma,
            Kalemler = secilenler.Select(d => new IslemKalemi { Degisiklik = d }).ToList()
        };

        // WRITE-01: aynı anda tek yazma.
        if (!await _yazmaKilidi.WaitAsync(0, ct).ConfigureAwait(false))
        {
            rapor.Durum = IslemDurumu.Durduruldu;
            rapor.Notlar.Add("Başka bir uygulama işlemi sürüyor; bitmesini bekleyin.");
            return rapor;
        }

        try
        {
            return await CalistirAsync(rapor, secilenler, guncelOku, surum, ayar, ct).ConfigureAwait(false);
        }
        finally
        {
            _yazmaKilidi.Release();
        }
    }

    private async Task<IslemRaporu> CalistirAsync(IslemRaporu rapor, IReadOnlyList<Degisiklik> secilenler,
        Func<CancellationToken, Task<(GuncelDurum? Durum, string? Hata)>> guncelOku, string? surum, Ayarlar ayar, CancellationToken ct)
    {
        if (secilenler.Count == 0)
        {
            rapor.Notlar.Add("Seçili değişiklik yok.");
            return rapor;
        }

        var (izin, neden) = YazmaIzni(surum, ayar);

        if (!izin)
        {
            rapor.Durum = IslemDurumu.Durduruldu;
            rapor.Notlar.Add(neden);
            return rapor;
        }

        // Ön kontrol 1: kimlik. Onay verilmiş olsa bile kariyer/takım/tarih/taktik değiştiyse eski plan gönderilmez (K02, K10).
        var (durum, hata) = await guncelOku(ct).ConfigureAwait(false);

        if (durum is null)
        {
            rapor.Durum = IslemDurumu.Durduruldu;
            rapor.Notlar.Add("Oyun okunamadı; takım kimliği belirsizken yazılmaz (WRITE-02). " + hata);
            return rapor;
        }

        rapor.Notlar.Add($"Takım: {durum.Kimlik.TakimAdi}");
        var raporTakim = new IslemRaporu
        {
            Id = rapor.Id, TakimAdi = durum.Kimlik.TakimAdi, KariyerAnahtari = durum.Kimlik.KariyerAnahtari, PlanAnahtari = rapor.PlanAnahtari,
            Baslangic = rapor.Baslangic, GeriAlma = rapor.GeriAlma, Kalemler = rapor.Kalemler, Notlar = rapor.Notlar
        };
        rapor = raporTakim;

        if (durum.Kimlik.PlanAnahtari != rapor.PlanAnahtari)
        {
            rapor.Durum = IslemDurumu.Durduruldu;
            rapor.Notlar.Insert(0, "Bekleyen plan durduruldu: plan yapıldıktan sonra kariyer, takım, takvim tarihi veya aktif taktik değişti. Yeniden analiz edin.");
            foreach (var k in rapor.Kalemler)
            {
                k.Durum = KalemDurumu.Atlandi;
            }

            return rapor;
        }

        // Ön kontrol 2: her kalemin güncel değeri. Zaten hedefteyse gönderilmez (K09); oyunda değiştiyse üzerine yazılmaz.
        var gonderilecek = new List<IslemKalemi>();

        foreach (var k in rapor.Kalemler)
        {
            if (!TurDogrulandi(surum, ayar, k.Degisiklik.Tur))
            {
                k.Durum = KalemDurumu.Atlandi;
                k.Hata = "Bu değişiklik türünde yazma bu sürümde doğrulanmadı; gönderilmedi.";
                continue;
            }

            var guncel = k.Degisiklik.Oku(durum.Antrenman);
            k.OncekiOkunan = guncel;

            if (guncel is null)
            {
                k.Durum = KalemDurumu.Atlandi;
                k.Hata = "Oyunda artık bulunamadı (oyuncu ayrıldı ya da gün takvimden çıktı).";
            }
            else if (guncel.Id == k.Degisiklik.Hedef.Id)
            {
                k.Durum = KalemDurumu.ZatenUygulanmis;
                k.SonOkunan = guncel;
            }
            else if (k.Degisiklik.Eski is { } eski && guncel.Id != eski.Id)
            {
                k.Durum = KalemDurumu.Cakisma;
                k.Hata = $"Plan yapıldığında \"{eski.Ad}\" idi, şimdi oyunda \"{guncel.Ad}\". Sizin değişikliğinizin üzerine yazılmadı.";
            }
            else if (!k.Degisiklik.Duzenlenebilir(durum.Antrenman))
            {
                k.Durum = KalemDurumu.Atlandi;
                k.Hata = "Oyun artık bu alanın değiştirilmesine izin vermiyor (geçmiş gün, maç ya da sağlık/ekip kilidi).";
            }
            else
            {
                gonderilecek.Add(k);
            }
        }

        if (gonderilecek.Count > 0)
        {
            // Ön kontrol 3: oyunun kendi kuru çalıştırması (dry run). Reddedilen kalem gönderilmez.
            foreach (var k in gonderilecek.ToList())
            {
                var kuru = await _baglanti.AntrenmanYazAsync(k.Degisiklik.Istek(k.Degisiklik.Hedef), uygula: false, ct).ConfigureAwait(false);

                if (!kuru.Basarili)
                {
                    k.Durum = kuru.Sonuc == CagriSonucu.Reddedildi ? KalemDurumu.Basarisiz : KalemDurumu.Atlandi;
                    k.Hata = "Ön kontrolde reddedildi: " + kuru.Hata;
                    gonderilecek.Remove(k);

                    if (kuru.Sonuc != CagriSonucu.Reddedildi)
                    {
                        rapor.Durum = IslemDurumu.Durduruldu;
                        rapor.Notlar.Add("Ön kontrol sırasında bağlantı kesildi; hiçbir şey gönderilmedi.");
                        foreach (var kalan in gonderilecek)
                        {
                            kalan.Durum = KalemDurumu.Atlandi;
                        }

                        return rapor;
                    }
                }
            }
        }

        // Sıralı uygulama: bir seferde tek değişiklik. Belirsiz sonuçta durulur, kör tekrar yapılmaz.
        rapor.Durum = IslemDurumu.Uygulaniyor;
        var durdur = false;

        foreach (var k in gonderilecek)
        {
            if (durdur)
            {
                k.Durum = KalemDurumu.Atlandi;
                k.Hata = "Önceki işlemin sonucu belirsiz olduğu için gönderilmedi.";
                continue;
            }

            k.Durum = KalemDurumu.Uygulaniyor;
            k.Zaman = DateTimeOffset.UtcNow;
            var cevap = await _baglanti.AntrenmanYazAsync(k.Degisiklik.Istek(k.Degisiklik.Hedef), uygula: true, ct).ConfigureAwait(false);

            switch (cevap.Sonuc)
            {
                case CagriSonucu.Tamam:
                    // MCPFM'in "done" bilgisi tek başına yeterli sayılmaz; sonuç aşağıda geri okunarak belirlenir.
                    break;
                case CagriSonucu.Reddedildi:
                    k.Durum = KalemDurumu.Basarisiz;
                    k.Hata = "Oyun reddetti: " + cevap.Hata;
                    if (cevap.Hata?.Contains("match", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        durdur = true;
                        rapor.Notlar.Add("Maç oynanıyor görünüyor; kalan değişiklikler gönderilmedi (WRITE-02).");
                    }
                    break;
                default:
                    k.Durum = KalemDurumu.Belirsiz;
                    k.Hata = cevap.Hata;
                    durdur = true;
                    break;
            }
        }

        // Yeniden okuma: "doğrulandı" yalnızca oyundan geri okunan değer hedefle eşleşirse (WRITE-04).
        var son = await _baglanti.AntrenmanAsync(ct).ConfigureAwait(false);

        foreach (var k in gonderilecek)
        {
            if (k.Durum == KalemDurumu.Atlandi)
            {
                continue;
            }

            if (!son.Basarili)
            {
                if (k.Durum != KalemDurumu.Basarisiz)
                {
                    k.Durum = KalemDurumu.Belirsiz;
                    k.Hata = (k.Hata is null ? "" : k.Hata + " ") + "Geri okuma yapılamadı: sonuç bilinmiyor.";
                }

                continue;
            }

            var okunan = k.Degisiklik.Oku(son.Veri!);
            k.SonOkunan = okunan;

            if (okunan?.Id == k.Degisiklik.Hedef.Id)
            {
                k.Durum = KalemDurumu.Dogrulandi;
                k.Hata = k.Hata is null ? null : k.Hata + " (Geri okumada hedef değer görüldü.)";
            }
            else if (k.Durum is KalemDurumu.Uygulaniyor or KalemDurumu.Belirsiz)
            {
                k.Durum = KalemDurumu.Basarisiz;
                k.Hata = (k.Hata is null ? "" : k.Hata + " ") + $"Geri okunan değer hedefle eşleşmedi ({okunan?.Ad ?? "okunamadı"}).";
            }
        }

        var tamam = rapor.Kalemler.Count(k => k.Durum is KalemDurumu.Dogrulandi or KalemDurumu.ZatenUygulanmis);
        rapor.Durum = tamam == rapor.Kalemler.Count ? IslemDurumu.Dogrulandi
            : tamam > 0 ? IslemDurumu.KismenUygulandi
            : IslemDurumu.BasarisizBelirsiz;

        return rapor;
    }

    /// <summary>
    /// Geri alma (K12): yalnızca doğrulanmış ve oyunda hâlâ bizim yazdığımız değerde duran kalemler, aynı kariyer ve
    /// geçerli tarih koşulunda eski değerine döndürülür ve her biri ayrıca doğrulanır. Kalanı raporlanır, gizlenmez.
    /// </summary>
    public async Task<IslemRaporu> GeriAlAsync(IslemRaporu onceki, Func<CancellationToken, Task<(GuncelDurum? Durum, string? Hata)>> guncelOku,
        string? surum, Ayarlar ayar, CancellationToken ct = default)
    {
        var (durum, hata) = await guncelOku(ct).ConfigureAwait(false);
        var notlar = new List<string>();

        if (durum is null)
        {
            return new IslemRaporu
            {
                Id = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"), TakimAdi = onceki.TakimAdi, KariyerAnahtari = onceki.KariyerAnahtari,
                PlanAnahtari = onceki.PlanAnahtari, Baslangic = DateTimeOffset.UtcNow, GeriAlma = true, Durum = IslemDurumu.Durduruldu,
                Notlar = { "Oyun okunamadı; geri alma yapılmadı. " + hata }
            };
        }

        if (durum.Kimlik.KariyerAnahtari != onceki.KariyerAnahtari)
        {
            return new IslemRaporu
            {
                Id = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"), TakimAdi = durum.Kimlik.TakimAdi, KariyerAnahtari = durum.Kimlik.KariyerAnahtari,
                PlanAnahtari = onceki.PlanAnahtari, Baslangic = DateTimeOffset.UtcNow, GeriAlma = true, Durum = IslemDurumu.Durduruldu,
                Notlar = { "Farklı bir kariyer/takım yüklü; geri alma yapılmadı." }
            };
        }

        var geriAlinacak = new List<Degisiklik>();

        foreach (var k in onceki.Kalemler.Where(k => k.Durum == KalemDurumu.Dogrulandi && k.OncekiOkunan is not null))
        {
            var guncel = k.Degisiklik.Oku(durum.Antrenman);

            if (guncel?.Id != k.Degisiklik.Hedef.Id)
            {
                notlar.Add($"{k.Degisiklik.Baslik}: oyunda artık \"{guncel?.Ad ?? "yok"}\"; geri alınmadı.");
            }
            else if (!k.Degisiklik.Duzenlenebilir(durum.Antrenman))
            {
                notlar.Add($"{k.Degisiklik.Baslik}: gün geçti veya kilitlendi; geri alınamaz.");
            }
            else
            {
                geriAlinacak.Add(k.Degisiklik with { Eski = k.Degisiklik.Hedef, Hedef = k.OncekiOkunan!, Gerekce = "Geri alma" });
            }
        }

        foreach (var k in onceki.Kalemler.Where(k => k.Durum != KalemDurumu.Dogrulandi))
        {
            notlar.Add($"{k.Degisiklik.Baslik}: uygulanmış olarak doğrulanmadığı için geri alma kapsamında değil.");
        }

        // Geri alma, güncel plan anahtarıyla çalışır (tarih ilerlediyse anahtar değişmiştir ve kalemler zaten düzenlenemez).
        var rapor = await UygulaAsync(durum.Kimlik.PlanAnahtari, geriAlinacak, guncelOku, surum, ayar, geriAlma: true, ct).ConfigureAwait(false);
        rapor.Notlar.AddRange(notlar);

        if (geriAlinacak.Count == 0)
        {
            rapor.Durum = IslemDurumu.Durduruldu;
            rapor.Notlar.Insert(0, "Geri alınabilecek kalem kalmadı.");
        }

        return rapor;
    }
}
