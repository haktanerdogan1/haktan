using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Asistan.Core.Veri;

public enum CagriSonucu
{
    Tamam,
    /// <summary>Oyun/bağlantı isteği reddetti (doğrulama hatası, maç oynanıyor...). Değişiklik yapılmadı.</summary>
    Reddedildi,
    /// <summary>Bağlantı yok: oyun veya MCPFM API kapalı.</summary>
    Ulasilamadi,
    /// <summary>Zaman aşımı ya da bağlantı koptu: işlemin oyuna ulaşıp ulaşmadığı bilinmiyor.</summary>
    Belirsiz
}

public sealed record Cevap<T>(CagriSonucu Sonuc, T? Veri, string? Hata)
{
    public bool Basarili => Sonuc == CagriSonucu.Tamam && Veri is not null;
    public static Cevap<T> Tamam(T veri) => new(CagriSonucu.Tamam, veri, null);
    public static Cevap<T> Hatali(CagriSonucu sonuc, string hata) => new(sonuc, default, hata);
}

/// <summary>Oyunla konuşan katman. Gerçek oyun için <see cref="McpfmBaglantisi"/>, testler için sahte bağlantı.</summary>
public interface IOyunBaglantisi
{
    /// <summary>Eklenti/oyun sürümü ("product: Football Manager 27 27.1.0"...). Okunamazsa null.</summary>
    Task<Cevap<string>> SurumAsync(CancellationToken ct = default);
    Task<Cevap<AntrenmanDto>> AntrenmanAsync(CancellationToken ct = default);
    Task<Cevap<TaktiklerDto>> TaktiklerAsync(CancellationToken ct = default);
    Task<Cevap<KadroDto>> KadroAsync(bool ozellikler, CancellationToken ct = default);

    /// <summary>uygula=false: oyunun kendi ön kontrolü (dry run), hiçbir şey değişmez.</summary>
    Task<Cevap<AntrenmanDegisimleriDto>> AntrenmanYazAsync(AntrenmanIstegi istek, bool uygula, CancellationToken ct = default);
}

/// <summary>
/// MCPFM API'si (varsayılan http://localhost:5120). MCPFM'in kendi güvenlikleri de geçerlidir: maç sırasında yazma reddedilir,
/// her yazma önce oyunun sunduğu seçeneklere karşı kontrol edilir ve gönderildikten sonra geri okunur.
/// </summary>
public sealed class McpfmBaglantisi : IOyunBaglantisi, IDisposable
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly HttpClient _kopru;

    public McpfmBaglantisi(string apiAdresi = "http://localhost:5120", string kopruAdresi = "http://localhost:5121", TimeSpan? zamanAsimi = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(apiAdresi.TrimEnd('/') + "/"), Timeout = zamanAsimi ?? TimeSpan.FromSeconds(30) };
        _kopru = new HttpClient { BaseAddress = new Uri(kopruAdresi.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<Cevap<string>> SurumAsync(CancellationToken ct = default)
    {
        try
        {
            var metin = await _kopru.GetStringAsync("status", ct).ConfigureAwait(false);
            return Cevap<string>.Tamam(metin);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return Cevap<string>.Hatali(CagriSonucu.Ulasilamadi, "Oyun içi köprü (5121) yanıt vermiyor: " + e.Message);
        }
    }

    public Task<Cevap<AntrenmanDto>> AntrenmanAsync(CancellationToken ct = default) => GetAsync<AntrenmanDto>("api/v1/training", ct);

    public Task<Cevap<TaktiklerDto>> TaktiklerAsync(CancellationToken ct = default) => GetAsync<TaktiklerDto>("api/v1/tactics", ct);

    public Task<Cevap<KadroDto>> KadroAsync(bool ozellikler, CancellationToken ct = default) =>
        GetAsync<KadroDto>("api/v1/squad" + (ozellikler ? "?includeAttributes=true" : ""), ct);

    public async Task<Cevap<AntrenmanDegisimleriDto>> AntrenmanYazAsync(AntrenmanIstegi istek, bool uygula, CancellationToken ct = default)
    {
        var govde = new StringContent(JsonSerializer.Serialize(istek, Json), Encoding.UTF8, "application/json");
        return await GonderAsync<AntrenmanDegisimleriDto>(() => _http.PostAsync("api/v1/training?apply=" + (uygula ? "true" : "false"), govde, ct), yazma: uygula).ConfigureAwait(false);
    }

    private Task<Cevap<T>> GetAsync<T>(string yol, CancellationToken ct) => GonderAsync<T>(() => _http.GetAsync(yol, ct), yazma: false);

    private static async Task<Cevap<T>> GonderAsync<T>(Func<Task<HttpResponseMessage>> istek, bool yazma)
    {
        HttpResponseMessage yanit;

        try
        {
            yanit = await istek().ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // Yazmada zaman aşımı: işlem oyuna ulaşmış olabilir. Asla "başarılı" sayılmaz, asla kör tekrar edilmez.
            return Cevap<T>.Hatali(yazma ? CagriSonucu.Belirsiz : CagriSonucu.Ulasilamadi, "Zaman aşımı.");
        }
        catch (HttpRequestException e)
        {
            return Cevap<T>.Hatali(yazma ? CagriSonucu.Belirsiz : CagriSonucu.Ulasilamadi,
                "MCPFM API'sine (5120) ulaşılamadı. Oyun ve MCPFM açık mı? " + e.Message);
        }

        using (yanit)
        {
            var metin = await yanit.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!yanit.IsSuccessStatusCode)
            {
                var sonuc = yanit.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                    ? CagriSonucu.Reddedildi
                    : (yazma ? CagriSonucu.Belirsiz : CagriSonucu.Ulasilamadi);
                return Cevap<T>.Hatali(sonuc, $"{(int)yanit.StatusCode}: {Kisalt(metin)}");
            }

            try
            {
                var veri = JsonSerializer.Deserialize<T>(metin, Json);
                return veri is null ? Cevap<T>.Hatali(CagriSonucu.Ulasilamadi, "Boş yanıt.") : Cevap<T>.Tamam(veri);
            }
            catch (JsonException e)
            {
                return Cevap<T>.Hatali(yazma ? CagriSonucu.Belirsiz : CagriSonucu.Ulasilamadi, "Yanıt çözülemedi: " + e.Message);
            }
        }
    }

    private static string Kisalt(string metin) => metin.Length > 400 ? metin[..400] + "…" : metin;

    public void Dispose()
    {
        _http.Dispose();
        _kopru.Dispose();
    }
}
