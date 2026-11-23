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
    /// Oyun içi yazma testi: bir oyuncunun yoğunluğunu ve ek odağını, bir seansı değiştirir, geri okur, eski haline getirir
    /// ve onu da geri okur. Kullanıcı açıkça başlatır. Hepsi doğrulanırsa sürüm "yazma doğrulandı" listesine eklenir.
    /// </summary>
    public static async Task<(bool Basarili, List<string> Gunluk)> YazmaTestiAsync(IOyunBaglantisi b, CancellationToken ct = default)
    {
        var gunluk = new List<string>();
        var ant = await b.AntrenmanAsync(ct).ConfigureAwait(false);

        if (!ant.Basarili)
        {
            gunluk.Add("Antrenman okunamadı: " + ant.Hata);
            return (false, gunluk);
        }

        var t = ant.Veri!;
        var hepsi = true;

        // 1) Yoğunluk
        var oyuncu = t.Players.FirstOrDefault(p => p.IntensityLocked != true && p.Intensity is not null);
        var yeniYogunluk = oyuncu is null ? null : t.IntensityOptions.FirstOrDefault(o => o.Id is >= 0 and <= 2 && o.Id != oyuncu.Intensity!.Id);

        if (oyuncu is null || yeniYogunluk is null)
        {
            gunluk.Add("Yoğunluk: denenecek uygun oyuncu/seçenek yok.");
            hepsi = false;
        }
        else
        {
            var eski = oyuncu.IntensityIsAutomatic == true ? -1 : oyuncu.Intensity!.Id;
            var d = new Degisiklik(DegisiklikTuru.Yogunluk, "test", $"{oyuncu.Name} yoğunluk", null, new Secenek(yeniYogunluk.Id, yeniYogunluk.Name), "test", OyuncuId: oyuncu.PlayerId);
            hepsi &= await GidipDon(b, d, new Secenek(eski, "eski"), gunluk, ct).ConfigureAwait(false);
        }

        // 2) Ek odak
        var odakOyuncu = t.Players.FirstOrDefault(p => p.FocusLocked != true);
        var yeniOdak = odakOyuncu is null ? null : t.FocusOptions.FirstOrDefault(o => o.Id >= 0 && o.Id != (odakOyuncu.Focus?.Id ?? -1));

        if (odakOyuncu is null || yeniOdak is null)
        {
            gunluk.Add("Ek odak: denenecek uygun oyuncu/seçenek yok.");
            hepsi = false;
        }
        else
        {
            var d = new Degisiklik(DegisiklikTuru.Odak, "test", $"{odakOyuncu.Name} odak", null, new Secenek(yeniOdak.Id, yeniOdak.Name), "test", OyuncuId: odakOyuncu.PlayerId);
            hepsi &= await GidipDon(b, d, new Secenek(odakOyuncu.Focus?.Id ?? -1, "eski"), gunluk, ct).ConfigureAwait(false);
        }

        // 3) Seans
        var gun = t.Weeks.SelectMany(w => w.Days).FirstOrDefault(x => x.Sessions.Any(s => s.CanEdit == true));
        var seans = gun?.Sessions.First(s => s.CanEdit == true);
        var yeniSeans = seans is null ? null : t.SessionTypes.SelectMany(c => c.Sessions).FirstOrDefault(s => s.Id != seans.Type);

        if (gun is null || seans is null || yeniSeans is null)
        {
            gunluk.Add("Seans: düzenlenebilir seans yok.");
            hepsi = false;
        }
        else
        {
            var d = new Degisiklik(DegisiklikTuru.Seans, "test", $"{gun.Date} {seans.Slot + 1}. seans", null, new Secenek(yeniSeans.Id, yeniSeans.Name), "test", gun.Date, seans.Slot);
            hepsi &= await GidipDon(b, d, new Secenek(seans.Type, seans.Name), gunluk, ct).ConfigureAwait(false);
        }

        return (hepsi, gunluk);
    }

    private static async Task<bool> GidipDon(IOyunBaglantisi b, Degisiklik d, Secenek eski, List<string> gunluk, CancellationToken ct)
    {
        var git = await b.AntrenmanYazAsync(d.Istek(d.Hedef), true, ct).ConfigureAwait(false);
        var oku = await b.AntrenmanAsync(ct).ConfigureAwait(false);
        var gitti = oku.Basarili && d.Oku(oku.Veri!)?.Id == d.Hedef.Id;
        gunluk.Add($"{d.Baslik}: \"{d.Hedef.Ad}\" yazıldı → {(gitti ? "geri okundu ✓" : "doğrulanamadı ✗ " + (git.Hata ?? oku.Hata))}");

        // Belirsiz sonuçta bile eski değere dönmeyi dener; dönüş de ayrıca doğrulanır ve sonucu gizlenmez.
        var don = await b.AntrenmanYazAsync(d.Istek(eski), true, ct).ConfigureAwait(false);
        var oku2 = await b.AntrenmanAsync(ct).ConfigureAwait(false);
        var dondu = oku2.Basarili && d.Oku(oku2.Veri!)?.Id == eski.Id;
        gunluk.Add($"{d.Baslik}: eski değere dönüş → {(dondu ? "doğrulandı ✓" : "DOĞRULANAMADI ✗ — oyunda elle kontrol edin. " + (don.Hata ?? oku2.Hata))}");
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
