using Asistan.Core.Veri;

namespace Asistan.Core.Uygulama;

public enum Yetenek { Okunabiliyor, Yazilabiliyor, OyunIciDogrulandi, Kismen, Desteklenmiyor, Denenmedi }

public sealed record YetenekSatiri(string Alan, Yetenek Okuma, Yetenek Yazma, string Not);

/// <summary>"Okunabiliyor / yazılabiliyor / oyun içi doğrulandı / desteklenmiyor" matrisi. Tamamlanmadan tam otomasyon açılmaz.</summary>
public sealed class YetenekMatrisi
{
    public string? Surum { get; set; }
    public DateTimeOffset Zaman { get; set; }
    public List<YetenekSatiri> Satirlar { get; set; } = new();
    public bool YazmaDogrulandi { get; set; }
}

public static class YetenekTesti
{
    /// <summary>Yalnızca okur ve oyunun kendi kuru çalıştırmasını kullanır: oyunda hiçbir şey değişmez.</summary>
    public static async Task<YetenekMatrisi> OkumaTestiAsync(IOyunBaglantisi b, Ayarlar ayar, CancellationToken ct = default)
    {
        var m = new YetenekMatrisi { Zaman = DateTimeOffset.Now };
        var surum = await b.SurumAsync(ct).ConfigureAwait(false);
        m.Surum = surum.Basarili ? SurumSatiri(surum.Veri!) : null;
        m.Satirlar.Add(new("Oyun/köprü sürümü", surum.Basarili ? Yetenek.Okunabiliyor : Yetenek.Desteklenmiyor, Yetenek.Desteklenmiyor,
            surum.Basarili ? m.Surum ?? "" : surum.Hata ?? ""));

        var ant = await b.AntrenmanAsync(ct).ConfigureAwait(false);
        var tak = await b.TaktiklerAsync(ct).ConfigureAwait(false);
        var kad = await b.KadroAsync(true, ct).ConfigureAwait(false);

        if (!ant.Basarili)
        {
            m.Satirlar.Add(new("Antrenman (takvim, seanslar, oyuncular)", Yetenek.Desteklenmiyor, Yetenek.Denenmedi, ant.Hata ?? ""));
        }
        else
        {
            var t = ant.Veri!;
            var gunSayisi = t.Weeks.Sum(w => w.Days.Count);
            var duzenlenebilir = t.Weeks.SelectMany(w => w.Days).SelectMany(d => d.Sessions).FirstOrDefault(s => s.CanEdit == true);
            var anlik = Anlik.Olustur(t, tak, kad, m.Surum, ayar, DateTimeOffset.Now);
            var eslesmeyen = anlik.SeansTurleri.Values.Count(s => s.Amac == SeansAmaci.Bilinmiyor);
            var taktikAlanlari = Enum.GetValues<TaktikAlan>().Count(a => anlik.SeansTurleri.Values.Any(s => s.Alan == a));

            m.Satirlar.Add(new("Takvim (bu ve gelecek hafta)", gunSayisi > 0 ? Yetenek.Okunabiliyor : Yetenek.Kismen,
                await KuruSeans(b, t, duzenlenebilir, ct).ConfigureAwait(false), $"{t.Weeks.Count} hafta, {gunSayisi} gün, {t.Weeks.SelectMany(w => w.Days).Count(d => d.Match is not null)} maç."));
            m.Satirlar.Add(new("Seans türleri ve eşleştirme", t.SessionTypes.Count > 0 ? Yetenek.Okunabiliyor : Yetenek.Desteklenmiyor, Yetenek.Desteklenmiyor,
                $"{anlik.SeansTurleri.Count} seans; {eslesmeyen} tanesi sınıflandırılamadı; 8 taktik alanından {taktikAlanlari} tanesine seans eşleşti."));

            var oyuncu = t.Players.FirstOrDefault(p => p.IntensityLocked != true);
            m.Satirlar.Add(new("Bireysel yoğunluk", t.Players.Count > 0 ? Yetenek.Okunabiliyor : Yetenek.Kismen,
                await KuruOyuncu(b, t, oyuncu, odak: false, ct).ConfigureAwait(false), $"{t.Players.Count} oyuncu, {t.IntensityOptions.Count} yoğunluk seçeneği."));
            var odakOyuncu = t.Players.FirstOrDefault(p => p.FocusLocked != true);
            m.Satirlar.Add(new("Bireysel ek odak", t.FocusOptions.Count > 0 ? Yetenek.Okunabiliyor : Yetenek.Kismen,
                await KuruOyuncu(b, t, odakOyuncu, odak: true, ct).ConfigureAwait(false), $"{t.FocusOptions.Count} odak seçeneği."));
            m.Satirlar.Add(new("Oyuncu iş yükü / dinlenme günü", t.Players.Any(p => p.Workload is not null) ? Yetenek.Okunabiliyor : Yetenek.Kismen, Yetenek.Desteklenmiyor,
                "MCPFM get_training workload/restDays alanları."));

            var olcumler = anlik.Anlayis.Values.Count(o => o.Anlayis.VarMi);
            m.Satirlar.Add(new("Taktiksel anlayış alt boyutları", olcumler == 8 ? Yetenek.Okunabiliyor : olcumler > 0 ? Yetenek.Kismen : Yetenek.Desteklenmiyor,
                Yetenek.Desteklenmiyor, $"8 alandan {olcumler} tanesi okundu. Geçmiş ölçümler uygulama tarafından kaydedilir."));
        }

        m.Satirlar.Add(new("Aktif taktik ve talimatlar", tak.Basarili && tak.Veri!.Active is not null ? Yetenek.Okunabiliyor : Yetenek.Desteklenmiyor, Yetenek.Desteklenmiyor,
            tak.Basarili ? $"{tak.Veri!.Slots.Count} taktik yuvası; alternatif taktikler ayrı tutulur." : tak.Hata ?? ""));

        if (kad.Basarili)
        {
            var p = kad.Veri!.Players;
            m.Satirlar.Add(new("Kondisyon / sakatlık / yaş", p.Any(x => x.Condition is not null) ? Yetenek.Okunabiliyor : Yetenek.Kismen, Yetenek.Desteklenmiyor,
                $"{p.Count} oyuncu; {p.Count(x => x.Condition is null)} oyuncunun kondisyonu okunamadı."));
            m.Satirlar.Add(new("Görünür özellikler", p.Any(x => x.Attributes is { Count: > 0 }) ? Yetenek.Okunabiliyor : Yetenek.Kismen, Yetenek.Desteklenmiyor,
                "Gözlemci bilgisi (sis) geçerli: maskeli değerler okunmuş sayılmaz."));
        }
        else
        {
            m.Satirlar.Add(new("Kadro (kondisyon, sakatlık, özellikler)", Yetenek.Desteklenmiyor, Yetenek.Desteklenmiyor, kad.Hata ?? ""));
        }

        m.Satirlar.Add(new("Personel (fizyoterapist, antrenman sorumluluğu)", Yetenek.Desteklenmiyor, Yetenek.Desteklenmiyor,
            "MCPFM sağlık ekibini okumuyor. Ayarlar'daki kullanıcı beyanı kullanılır."));
        m.Satirlar.Add(new("Antrenman pozisyonu / rolü", Yetenek.Okunabiliyor, Yetenek.Desteklenmiyor,
            "MCPFM yazmayı desteklemiyor; ilk sürümde yalnızca okuma ve tavsiye."));
        m.Satirlar.Add(new("Rezerv / U19 takımlar", Yetenek.Desteklenmiyor, Yetenek.Desteklenmiyor, "İlk sürüm yalnızca as takım."));

        m.YazmaDogrulandi = m.Surum is not null && ayar.DogrulanmisSurumler.Any(s => m.Surum.Contains(s, StringComparison.OrdinalIgnoreCase));
        return m;
    }

    /// <summary>
    /// Oyun içi yazma testi: yoğunluk, ek odak ve seans için ayrı ayrı bir değişikliği yazar, geri okur, eski haline getirir ve
    /// onu da geri okur. Denenecek değer körlemesine seçilmez: oyunun kendi ön kontrolünden (kuru çalıştırma) geçen ilk aday
    /// kullanılır (ör. saha oyuncusuna kaleci odağı denenmez). Her tür ayrı doğrulanır; biri geçmezse diğerleri yine açılır.
    /// İlerleme <paramref name="ilerleme"/> ile anında bildirilir.
    /// </summary>
    public static async Task<(Dictionary<DegisiklikTuru, bool> Sonuc, List<string> Gunluk)> YazmaTestiAsync(
        IOyunBaglantisi b, Action<string>? ilerleme = null, CancellationToken ct = default)
    {
        var gunluk = new List<string>();
        void Yaz(string m) { gunluk.Add(m); ilerleme?.Invoke(m); }
        var sonuc = new Dictionary<DegisiklikTuru, bool> { [DegisiklikTuru.Seans] = false, [DegisiklikTuru.Odak] = false, [DegisiklikTuru.Yogunluk] = false };
        var ant = await b.AntrenmanAsync(ct).ConfigureAwait(false);

        if (!ant.Basarili)
        {
            Yaz("Antrenman okunamadı: " + ant.Hata);
            return (sonuc, gunluk);
        }

        var t = ant.Veri!;

        // Takım seansı önce: "Eksikleri gider" yalnızca buna ihtiyaç duyar.
        var seansAdaylari =
            from w in t.Weeks
            from d in w.Days
            where d.Match is null
            from s in d.Sessions
            where s.CanEdit == true
            from yeni in t.SessionTypes.SelectMany(c => c.Sessions)
            where yeni.Id != s.Type
            select new Degisiklik(DegisiklikTuru.Seans, "test", $"{d.Date} {s.Slot + 1}. seans", new Secenek(s.Type, s.Name), new Secenek(yeni.Id, yeni.Name), "test", d.Date, s.Slot);
        sonuc[DegisiklikTuru.Seans] = await TurDene(b, "Takım seansı", seansAdaylari, Yaz, ct).ConfigureAwait(false);

        var yogunlukAdaylari =
            from p in t.Players
            where p.IntensityLocked != true && p.Intensity is not null
            from o in t.IntensityOptions
            where o.Id is >= 0 and <= 2 && (p.IntensityIsAutomatic == true || o.Id != p.Intensity!.Id)
            select new Degisiklik(DegisiklikTuru.Yogunluk, "test", $"{p.Name} – yoğunluk",
                p.IntensityIsAutomatic == true ? new Secenek(-1, "Otomatik") : new Secenek(p.Intensity!.Id, p.Intensity.Name), new Secenek(o.Id, o.Name), "test", OyuncuId: p.PlayerId);
        sonuc[DegisiklikTuru.Yogunluk] = await TurDene(b, "Bireysel yoğunluk", yogunlukAdaylari, Yaz, ct).ConfigureAwait(false);

        var odakAdaylari =
            from p in t.Players
            where p.FocusLocked != true
            from o in t.FocusOptions
            where o.Id >= 0 && o.Id != (p.Focus?.Id ?? -1)
            select new Degisiklik(DegisiklikTuru.Odak, "test", $"{p.Name} – ek odak",
                p.Focus is null ? new Secenek(-1, "Yok") : new Secenek(p.Focus.Id, p.Focus.Name), new Secenek(o.Id, o.Name), "test", OyuncuId: p.PlayerId);
        sonuc[DegisiklikTuru.Odak] = await TurDene(b, "Ek odak", odakAdaylari, Yaz, ct).ConfigureAwait(false);

        return (sonuc, gunluk);
    }

    private static async Task<bool> TurDene(IOyunBaglantisi b, string ad, IEnumerable<Degisiklik> adaylar, Action<string> yaz, CancellationToken ct)
    {
        yaz($"{ad}: deneniyor…");
        string? sonRet = null;
        var denenen = 0;

        foreach (var d in adaylar.Take(25))
        {
            denenen++;
            var kuru = await b.AntrenmanYazAsync(d.Istek(d.Hedef), false, ct).ConfigureAwait(false);

            if (kuru.Basarili)
            {
                try
                {
                    return await GidipDon(b, d, d.Eski!, yaz, ct).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    yaz($"{ad}: beklenmeyen hata: {e.Message}");
                    return false;
                }
            }

            if (kuru.Sonuc != CagriSonucu.Reddedildi)
            {
                yaz($"{ad}: bağlantı hatası, test durdu: {kuru.Hata}");
                return false;
            }

            sonRet = kuru.Hata;
        }

        yaz(denenen == 0
            ? $"{ad}: denenecek uygun oyuncu/seans yok."
            : $"{ad}: oyun {denenen} adayın hepsini ön kontrolde reddetti. Son yanıt: {sonRet}");
        return false;
    }

    private static async Task<bool> GidipDon(IOyunBaglantisi b, Degisiklik d, Secenek eski, Action<string> yaz, CancellationToken ct)
    {
        var gitti = false;
        var dondu = false;

        try
        {
            var git = await b.AntrenmanYazAsync(d.Istek(d.Hedef), true, ct).ConfigureAwait(false);
            var oku = await b.AntrenmanAsync(ct).ConfigureAwait(false);
            gitti = oku.Basarili && d.Oku(oku.Veri!)?.Id == d.Hedef.Id;
            yaz($"{d.Baslik}: \"{d.Hedef.Ad}\" yazıldı → {(gitti ? "geri okundu ✓" : "doğrulanamadı ✗ " + (git.Hata ?? oku.Hata ?? "oyunda değer değişmedi"))}");
        }
        finally
        {
            // Ne olursa olsun eski değere dönmeyi dener; dönüş de ayrıca doğrulanır ve sonucu gizlenmez.
            var don = await b.AntrenmanYazAsync(d.Istek(eski), true, ct).ConfigureAwait(false);
            var oku2 = await b.AntrenmanAsync(ct).ConfigureAwait(false);
            dondu = oku2.Basarili && d.Oku(oku2.Veri!)?.Id == eski.Id;
            yaz($"{d.Baslik}: eski değere dönüş → {(dondu ? "doğrulandı ✓" : "DOĞRULANAMADI ✗ — oyunda elle kontrol edin. " + (don.Hata ?? oku2.Hata))}");
        }

        return gitti && dondu;
    }

    private static async Task<Yetenek> KuruSeans(IOyunBaglantisi b, AntrenmanDto t, SeansDto? s, CancellationToken ct)
    {
        if (s is null)
        {
            return Yetenek.Denenmedi;
        }

        var gun = t.Weeks.SelectMany(w => w.Days).First(d => d.Sessions.Contains(s));
        var baska = t.SessionTypes.SelectMany(c => c.Sessions).FirstOrDefault(x => x.Id != s.Type);

        if (baska is null)
        {
            return Yetenek.Denenmedi;
        }

        var c = await b.AntrenmanYazAsync(new AntrenmanIstegi { Sessions = { new SeansIstegi { Date = gun.Date, Slot = s.Slot, Session = (int)baska.Id } } }, false, ct).ConfigureAwait(false);
        return c.Basarili ? Yetenek.Yazilabiliyor : Yetenek.Desteklenmiyor;
    }

    private static async Task<Yetenek> KuruOyuncu(IOyunBaglantisi b, AntrenmanDto t, OyuncuAntrenmanDto? p, bool odak, CancellationToken ct)
    {
        if (p is null)
        {
            return Yetenek.Denenmedi;
        }

        var istek = new OyuncuIstegi { PlayerId = p.PlayerId };

        if (odak)
        {
            var o = t.FocusOptions.FirstOrDefault(x => x.Id >= 0 && x.Id != (p.Focus?.Id ?? -1));
            if (o is null) return Yetenek.Denenmedi;
            istek.Focus = o.Id;
        }
        else
        {
            var o = t.IntensityOptions.FirstOrDefault(x => x.Id is >= 0 and <= 2 && x.Id != p.Intensity?.Id);
            if (o is null) return Yetenek.Denenmedi;
            istek.Intensity = o.Id;
        }

        var c = await b.AntrenmanYazAsync(new AntrenmanIstegi { Players = { istek } }, false, ct).ConfigureAwait(false);
        return c.Basarili ? Yetenek.Yazilabiliyor : Yetenek.Desteklenmiyor;
    }

    /// <summary>"/status" metninden "product: ..." satırı; yoksa ilk satır.</summary>
    public static string SurumSatiri(string status)
    {
        var satirlar = status.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var urun = satirlar.FirstOrDefault(s => s.StartsWith("product:", StringComparison.OrdinalIgnoreCase));
        var kopru = satirlar.FirstOrDefault(s => s.StartsWith("bridge:", StringComparison.OrdinalIgnoreCase));
        return string.Join(" | ", new[] { urun?["product:".Length..].Trim(), kopru?["bridge:".Length..].Trim() }.Where(x => !string.IsNullOrEmpty(x)))
               is { Length: > 0 } s ? s : satirlar.FirstOrDefault() ?? status;
    }
}
