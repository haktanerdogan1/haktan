using System.Text.Json;
using System.Text.Json.Serialization;
using Asistan.Core.Uygulama;
using Asistan.Core.Veri;

namespace Asistan.Core.Depo;

/// <summary>Bir analiz anındaki taktik anlayış değerleri: izleme ve önce/sonra karşılaştırması için.</summary>
public sealed record AnalizKaydi(DateTimeOffset Zaman, string? IlkGun, string TaktikParmakIzi, Dictionary<string, double?> Anlayis, double? Genel);

/// <summary>
/// Yerel kayıt: ayarlar, analiz geçmişi, odak değişim tarihleri ve işlem günlüğü. Kariyerler ayrı klasörlerde tutulur.
/// Dosya tabanlıdır (JSON): oyunun içinde ek yerel kütüphane gerektirmez, kullanıcı dosyaları açıp okuyabilir.
/// </summary>
public sealed class YerelDepo
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _kok;
    private readonly object _kilit = new();

    public YerelDepo(string kokKlasor)
    {
        _kok = kokKlasor;
        Directory.CreateDirectory(_kok);
    }

    public string Kok => _kok;

    public Ayarlar AyarlariOku() => Oku<Ayarlar>(Path.Combine(_kok, "ayarlar.json")) ?? new Ayarlar();

    public void AyarlariYaz(Ayarlar ayar) => Yaz(Path.Combine(_kok, "ayarlar.json"), ayar);

    public Dictionary<int, DateOnly> OdakDegisimleri(string kariyer) =>
        (Oku<Dictionary<int, string>>(Path.Combine(Klasor(kariyer), "odak-degisimleri.json")) ?? new())
        .Where(k => DateOnly.TryParse(k.Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
        .ToDictionary(k => k.Key, k => DateOnly.Parse(k.Value, System.Globalization.CultureInfo.InvariantCulture));

    public void OdakDegisimiKaydet(string kariyer, int oyuncuId, DateOnly tarih)
    {
        lock (_kilit)
        {
            var yol = Path.Combine(Klasor(kariyer), "odak-degisimleri.json");
            var d = Oku<Dictionary<int, string>>(yol) ?? new();
            d[oyuncuId] = tarih.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Yaz(yol, d);
        }
    }

    public void AnalizEkle(string kariyer, AnalizKaydi kayit)
    {
        lock (_kilit)
        {
            File.AppendAllText(Path.Combine(Klasor(kariyer), "analiz-gecmisi.jsonl"),
                JsonSerializer.Serialize(kayit, new JsonSerializerOptions(Json) { WriteIndented = false }) + Environment.NewLine);
        }
    }

    public List<AnalizKaydi> AnalizGecmisi(string kariyer, int son = 30)
    {
        var yol = Path.Combine(Klasor(kariyer), "analiz-gecmisi.jsonl");

        if (!File.Exists(yol))
        {
            return new();
        }

        return File.ReadAllLines(yol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => { try { return JsonSerializer.Deserialize<AnalizKaydi>(s, Json); } catch (JsonException) { return null; } })
            .Where(k => k is not null)
            .Select(k => k!)
            .TakeLast(son)
            .ToList();
    }

    public void IslemKaydet(IslemRaporu rapor) => Yaz(Path.Combine(Klasor(rapor.KariyerAnahtari), "islemler", rapor.Id + ".json"), rapor);

    public List<IslemRaporu> Islemler(string kariyer, int son = 20)
    {
        var klasor = Path.Combine(Klasor(kariyer), "islemler");

        if (!Directory.Exists(klasor))
        {
            return new();
        }

        return Directory.GetFiles(klasor, "*.json")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Take(son)
            .Select(Oku<IslemRaporu>)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }

    public void YetenekMatrisiYaz(YetenekMatrisi m) => Yaz(Path.Combine(_kok, "yetenek-matrisi.json"), m);

    public YetenekMatrisi? YetenekMatrisiOku() => Oku<YetenekMatrisi>(Path.Combine(_kok, "yetenek-matrisi.json"));

    private string Klasor(string kariyer)
    {
        var k = Path.Combine(_kok, "kariyerler", kariyer);
        Directory.CreateDirectory(k);
        return k;
    }

    private T? Oku<T>(string yol)
    {
        lock (_kilit)
        {
            if (!File.Exists(yol))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(yol), Json);
            }
            catch (JsonException)
            {
                // Bozuk dosya sessizce yutulmaz: yanına yedeklenir, varsayılanla devam edilir.
                File.Copy(yol, yol + ".bozuk", overwrite: true);
                return default;
            }
        }
    }

    private void Yaz<T>(string yol, T veri)
    {
        lock (_kilit)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
            var gecici = yol + ".tmp";
            File.WriteAllText(gecici, JsonSerializer.Serialize(veri, Json));
            File.Move(gecici, yol, overwrite: true);
        }
    }
}
