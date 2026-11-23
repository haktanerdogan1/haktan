using System.Globalization;
using System.Text.Json;
using Asistan.Core.Veri;

namespace Asistan.Tests;

/// <summary>
/// MCPFM'in davranışını taklit eden bellek içi oyun: kuru çalıştırma, seçenek kontrolü, düzenlenemez seans, kilitli oyuncu,
/// maç sırasında ret, zaman aşımı ve yazıldığı halde zaman aşımı (belirsiz) durumları ayarlanabilir.
/// </summary>
public sealed class SahteOyun : IOyunBaglantisi
{
    public AntrenmanDto Antrenman { get; set; }
    public TaktiklerDto? Taktikler { get; set; }
    public KadroDto? Kadro { get; set; }
    public string? Surum { get; set; } = "bridge: MCPFM Bridge 0.13.1\nproduct: Football Manager 27 27.1.0\nscene: Main";

    public bool MacOynaniyor { get; set; }
    /// <summary>Bu sıradaki uygulama (apply=true) çağrısı zaman aşımına düşer; Yazildi ise değişiklik yine de oyuna geçer.</summary>
    public int? ZamanAsimiCagrisi { get; set; }
    public bool ZamanAsimindaYazildi { get; set; }
    public bool GeriOkumaBozuk { get; set; }

    public int UygulamaSayisi { get; private set; }
    public int KuruSayisi { get; private set; }
    public List<string> Gunluk { get; } = new();

    public SahteOyun(AntrenmanDto antrenman, TaktiklerDto? taktikler, KadroDto? kadro)
    {
        Antrenman = antrenman;
        Taktikler = taktikler;
        Kadro = kadro;
    }

    public Task<Cevap<string>> SurumAsync(CancellationToken ct = default) =>
        Task.FromResult(Surum is null ? Cevap<string>.Hatali(CagriSonucu.Ulasilamadi, "yok") : Cevap<string>.Tamam(Surum));

    public Task<Cevap<AntrenmanDto>> AntrenmanAsync(CancellationToken ct = default) =>
        Task.FromResult(GeriOkumaBozuk && UygulamaSayisi > 0
            ? Cevap<AntrenmanDto>.Hatali(CagriSonucu.Ulasilamadi, "okunamadı")
            : Cevap<AntrenmanDto>.Tamam(Kopya(Antrenman)));

    public Task<Cevap<TaktiklerDto>> TaktiklerAsync(CancellationToken ct = default) =>
        Task.FromResult(Taktikler is null ? Cevap<TaktiklerDto>.Hatali(CagriSonucu.Ulasilamadi, "taktik yok") : Cevap<TaktiklerDto>.Tamam(Kopya(Taktikler)));

    public Task<Cevap<KadroDto>> KadroAsync(bool ozellikler, CancellationToken ct = default) =>
        Task.FromResult(Kadro is null ? Cevap<KadroDto>.Hatali(CagriSonucu.Ulasilamadi, "kadro yok") : Cevap<KadroDto>.Tamam(Kopya(Kadro)));

    public Task<Cevap<AntrenmanDegisimleriDto>> AntrenmanYazAsync(AntrenmanIstegi istek, bool uygula, CancellationToken ct = default)
    {
        if (!uygula)
        {
            KuruSayisi++;
        }

        if (MacOynaniyor)
        {
            return Task.FromResult(Cevap<AntrenmanDegisimleriDto>.Hatali(CagriSonucu.Reddedildi, "409: A match is being played; no changes."));
        }

        // Kontrol: MCPFM TrainingService.Plan ile aynı kurallar.
        foreach (var s in istek.Sessions)
        {
            var mevcut = Antrenman.Weeks.SelectMany(w => w.Days).FirstOrDefault(d => d.Date == s.Date)?.Sessions.FirstOrDefault(x => x.Slot == s.Slot);
            if (mevcut is null) return Ret($"{s.Date} slot {s.Slot} isn't a session of this week or the next");
            if (mevcut.CanEdit != true) return Ret($"{s.Date} slot {s.Slot} can't be changed.");
            if (!Antrenman.SessionTypes.SelectMany(c => c.Sessions).Any(x => x.Id == s.Session)) return Ret($"There's no session {s.Session}");
        }

        foreach (var p in istek.Players)
        {
            var o = Antrenman.Players.FirstOrDefault(x => x.PlayerId == p.PlayerId);
            if (o is null) return Ret($"No player {p.PlayerId}");
            if (p.Focus is not null && o.FocusLocked == true) return Ret($"{o.Name}'s focus is set by the staff");
            if (p.Intensity is not null && o.IntensityLocked == true) return Ret($"{o.Name}'s intensity is set by the medical staff while he's injured.");
            if (p.Focus is { } f && f != -1 && Antrenman.FocusOptions.All(x => x.Id != f)) return Ret("bad focus");
            if (p.Intensity is { } i && i != -1 && Antrenman.IntensityOptions.All(x => x.Id != i)) return Ret("bad intensity");
        }

        if (!uygula)
        {
            return Task.FromResult(Cevap<AntrenmanDegisimleriDto>.Tamam(new AntrenmanDegisimleriDto { Applied = false }));
        }

        UygulamaSayisi++;

        if (ZamanAsimiCagrisi == UygulamaSayisi)
        {
            if (ZamanAsimindaYazildi)
            {
                Yaz(istek);
            }

            Gunluk.Add("zaman aşımı");
            return Task.FromResult(Cevap<AntrenmanDegisimleriDto>.Hatali(CagriSonucu.Belirsiz, "Zaman aşımı."));
        }

        Yaz(istek);
        return Task.FromResult(Cevap<AntrenmanDegisimleriDto>.Tamam(new AntrenmanDegisimleriDto { Applied = true }));

        static Task<Cevap<AntrenmanDegisimleriDto>> Ret(string m) => Task.FromResult(Cevap<AntrenmanDegisimleriDto>.Hatali(CagriSonucu.Reddedildi, "400: " + m));
    }

    private void Yaz(AntrenmanIstegi istek)
    {
        foreach (var s in istek.Sessions)
        {
            var gun = Antrenman.Weeks.SelectMany(w => w.Days).First(d => d.Date == s.Date);
            var eski = gun.Sessions.First(x => x.Slot == s.Slot);
            var tur = Antrenman.SessionTypes.SelectMany(c => c.Sessions.Select(x => (c, x))).First(t => t.x.Id == s.Session);
            gun.Sessions[gun.Sessions.IndexOf(eski)] = new SeansDto { Slot = s.Slot, Type = s.Session, Name = tur.x.Name, Category = tur.c.Id, CanEdit = true };
            Gunluk.Add($"seans {s.Date}#{s.Slot}={s.Session}");
        }

        foreach (var p in istek.Players)
        {
            var o = Antrenman.Players.First(x => x.PlayerId == p.PlayerId);

            if (p.Focus is { } f)
            {
                o.Focus = f == -1 ? null : Antrenman.FocusOptions.First(x => x.Id == f);
                Gunluk.Add($"odak {o.PlayerId}={f}");
            }

            if (p.Intensity is { } i)
            {
                o.IntensityIsAutomatic = i == -1 ? true : null;
                o.Intensity = i == -1 ? o.Intensity : Antrenman.IntensityOptions.First(x => x.Id == i);
                Gunluk.Add($"yogunluk {o.PlayerId}={i}");
            }
        }
    }

    private static T Kopya<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x))!;
}

/// <summary>Senaryo kurucu: Türkçe seans adlarıyla iki haftalık takvim, kadro ve taktik.</summary>
public static class Senaryo
{
    public static readonly DateOnly Pazartesi = new(2026, 11, 23);

    public static readonly (int Id, string Ad, int Kat)[] SeansListesi =
    {
        (1, "Dinlenme", 1), (2, "Rejenerasyon", 1), (3, "Maç Önizleme", 2), (4, "Maç Provası", 2),
        (10, "Pas Stili", 3), (11, "Oyun Kurma", 3), (12, "Fırsat Yaratma", 3), (13, "Topa Sahipken Diziliş", 3),
        (14, "Baskı Hattı", 3), (15, "Pres Yoğunluğu", 3), (16, "Pres Tuzağı", 3), (17, "Top Rakipteyken Diziliş", 3),
        (20, "Dayanıklılık", 4), (21, "Sürat", 4), (30, "Atak", 5), (31, "Savunma", 5),
        (40, "Duran Toplar – Atak", 6), (50, "Takım Kaynaşması", 7)
    };

    public static AntrenmanDto Antrenman(IEnumerable<int> macGunleri, int duzenlenebilirIlkGun = 0, int haftaSayisi = 2)
    {
        var maclar = macGunleri.ToHashSet();
        var a = new AntrenmanDto
        {
            Schedule = "Sezon",
            SessionTypes = SeansListesi.GroupBy(s => s.Kat).Select(g => new SeansKategorisiDto
            {
                Id = g.Key, Name = "Kategori " + g.Key,
                Sessions = g.Select(s => new SecenekDto { Id = s.Id, Name = s.Ad }).ToList()
            }).ToList(),
            FocusOptions =
            {
                new() { Id = -1, Name = "Yok" }, new() { Id = 1, Name = "Bitiricilik" }, new() { Id = 2, Name = "Pas" },
                new() { Id = 3, Name = "Top Kapma" }, new() { Id = 4, Name = "Dayanıklılık" }, new() { Id = 5, Name = "Topsuz Alan" },
                new() { Id = 6, Name = "Kafa Vuruşu" }, new() { Id = 7, Name = "Markaj" }
            },
            IntensityOptions =
            {
                new() { Id = -1, Name = "Otomatik" }, new() { Id = 0, Name = "Yarım" }, new() { Id = 1, Name = "Ortalama" }, new() { Id = 2, Name = "Çift" },
                new() { Id = 3, Name = "1 hafta dinlenme" }
            }
        };

        for (var h = 0; h < haftaSayisi; h++)
        {
            var hafta = new HaftaDto { StartDate = Tarih(h * 7) };

            for (var g = 0; g < 7; g++)
            {
                var gunNo = h * 7 + g;
                var mac = maclar.Contains(gunNo);
                var duz = gunNo >= duzenlenebilirIlkGun;
                hafta.Days.Add(new GunDto
                {
                    Date = Tarih(gunNo),
                    Match = mac ? "Rakip FK (D)" : null,
                    Sessions = mac
                        ? new() { S(0, 3, duz), new SeansDto { Slot = 1, Type = 99, Name = "Maç", Category = 9, CanEdit = null }, S(2, 1, duz) }
                        : new() { S(0, 30, duz), S(1, 31, duz), S(2, 1, duz) }
                });
            }

            a.Weeks.Add(hafta);
        }

        var odakAdlari = a.FocusOptions.ToDictionary(o => o.Id, o => o.Name);
        a.Players.AddRange(new[]
        {
            Oyuncu(1, "Arda Forvet", "AdvancedForward", "ST", focus: null),
            Oyuncu(2, "Burak Stoper", "CentreBack", "D (M)", focus: 3),
            Oyuncu(3, "Can Orta", "BoxToBoxMidfielder", "M (M)", focus: 2),
            Oyuncu(4, "Deniz Genç", "Winger", "OOS (Sğ)", focus: null),
            Oyuncu(5, "Emre Sakat", "FullBack", "D (Sğ)", focus: null, yogunlukKilitli: true),
            Oyuncu(6, "Ferhat Yorgun", "DefensiveMidfielder", "DM", focus: 3)
        });

        foreach (var p in a.Players.Where(p => p.Focus is not null))
        {
            p.Focus!.Name = odakAdlari[p.Focus.Id];
        }

        return a;

        static SeansDto S(int slot, int tur, bool duz) => new()
        {
            Slot = slot, Type = tur, Name = SeansListesi.First(s => s.Id == tur).Ad, Category = SeansListesi.First(s => s.Id == tur).Kat, CanEdit = duz ? true : null
        };
    }

    public static string Tarih(int gun) => Pazartesi.AddDays(gun).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static OyuncuAntrenmanDto Oyuncu(int id, string ad, string rol, string poz, long? focus, bool yogunlukKilitli = false) => new()
    {
        PlayerId = id, Name = ad, RoleKey = rol, RoleName = rol, Position = poz,
        Focus = focus is null ? null : new SecenekDto { Id = focus.Value, Name = "?" },
        Intensity = new SecenekDto { Id = 1, Name = "Ortalama" },
        IntensityIsAutomatic = null,
        Workload = "Ortalama",
        IntensityLocked = yogunlukKilitli ? true : null
    };

    public static KadroDto Kadro() => new()
    {
        TeamName = "Deneme SK",
        Players =
        {
            K(1, 24, 95, false, ("Finishing", "15"), ("Movement", "11"), ("Composure", "13"), ("FirstTouch", "13"), ("Anticipation", "14"), ("Heading", "12"), ("Strength", "13")),
            K(2, 27, 96, false, ("Tackling", "14"), ("Marking", "13"), ("Heading", "15"), ("Positioning", "14"), ("JumpingReach", "15"), ("Strength", "16"), ("Concentration", "12")),
            K(3, 25, 90, false, ("Passing", "13"), ("Vision", "12"), ("Decisions", "12"), ("FirstTouch", "13"), ("Stamina", "15"), ("WorkRate", "16"), ("Teamwork", "14")),
            K(4, 19, 97, false, ("Dribbling", "12"), ("Crossing", "11"), ("Acceleration", "15"), ("Pace", "15"), ("Technique", "12"), ("FirstTouch", "11"), ("Movement", "10")),
            K(5, 28, 60, true),
            K(6, 30, 65, false, ("Tackling", "14"), ("Positioning", "13"), ("Passing", "12"))
        }
    };

    private static OyuncuDto K(int id, int yas, int kon, bool sakat, params (string, string)[] oz) => new()
    {
        Id = id, Name = "?", Age = yas, Condition = kon, IsInjured = sakat,
        Attributes = oz.Length == 0 ? null : new() { ["all"] = oz.ToDictionary(x => x.Item1, x => (string?)x.Item2) }
    };

    public static TaktiklerDto Taktik(string presYogunlugu = "40", string? digerleri = null, string pres = "MuchMoreUrgent", int slot = 1, string stil = "Gegenpress") => new()
    {
        TeamName = "Deneme SK",
        Slots =
        {
            new TaktikSlotDto
            {
                Slot = slot, Name = "Gegenpress 4-3-3", IsActive = true,
                Effectiveness = new EtkinlikDto
                {
                    TacticalUnderstanding = new TaktikAnlayisDto
                    {
                        Overall = "62",
                        PressingIntensity = new TaktikAlanDto { Understanding = presYogunlugu, MatchExposure = "60", TrainingContribution = "40" },
                        PassingStyle = A(digerleri ?? "78"), BuildUpStrategy = A(digerleri ?? "76"), ChanceCreation = A(digerleri ?? "70"),
                        InPossessionShape = A(digerleri ?? "80"), OutOfPossessionShape = A(digerleri ?? "77"), PressingTrap = A(digerleri ?? "79"),
                        LineOfEngagement = A(digerleri ?? "78")
                    }
                }
            }
        },
        Active = new AktifTaktikDto { InPossessionFormation = "4-3-3", StyleKey = stil, MentalityKey = "Positive", TeamInstructions = { ["PressingIntensity"] = pres, ["Tempo"] = "HigherTempo" } }
    };

    private static TaktikAlanDto A(string u) => new() { Understanding = u, MatchExposure = "60", TrainingContribution = "40" };
}
