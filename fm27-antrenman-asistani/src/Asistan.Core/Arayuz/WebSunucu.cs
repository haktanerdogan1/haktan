using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asistan.Core.Veri;

namespace Asistan.Core.Arayuz;

/// <summary>
/// Paneli yalnızca bu bilgisayardan (localhost) sunar. Değişiklik yapan istekler kendi başlığını ister: tarayıcıda açık
/// başka bir sayfa bu başlığı gönderemez, dolayısıyla oyuna yazdıramaz.
/// </summary>
public sealed class WebSunucu : IDisposable
{
    public const string Baslik = "X-Antrenman-Asistani";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AntrenmanAsistani _asistan;
    private readonly HttpListener _dinleyici = new();
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _iptal = new();

    public WebSunucu(AntrenmanAsistani asistan, int port, Action<string>? log = null)
    {
        _asistan = asistan;
        _log = log;
        Port = port;
        _dinleyici.Prefixes.Add($"http://localhost:{port}/");
    }

    public int Port { get; }
    public string Adres => $"http://localhost:{Port}/";

    public void Baslat()
    {
        _dinleyici.Start();
        _ = Task.Run(DongüAsync);
        _log?.Invoke("Antrenman Asistanı paneli: " + Adres);
    }

    private async Task DongüAsync()
    {
        while (!_iptal.IsCancellationRequested && _dinleyici.IsListening)
        {
            HttpListenerContext ctx;

            try
            {
                ctx = await _dinleyici.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_iptal.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            _ = Task.Run(() => IsleAsync(ctx));
        }
    }

    private async Task IsleAsync(HttpListenerContext ctx)
    {
        var istek = ctx.Request;
        var yanit = ctx.Response;

        try
        {
            var yol = istek.Url?.AbsolutePath.TrimEnd('/') ?? "";

            if (istek.HttpMethod == "GET" && (yol is "" or "/index.html"))
            {
                await Gonder(yanit, 200, "text/html; charset=utf-8", Kaynak("index.html")).ConfigureAwait(false);
                return;
            }

            if (istek.HttpMethod == "GET" && yol == "/api/durum")
            {
                await JsonGonder(yanit, 200, Gorunum.Olustur(_asistan)).ConfigureAwait(false);
                return;
            }

            if (istek.HttpMethod != "POST" || istek.Headers[Baslik] != "1")
            {
                await JsonGonder(yanit, 404, new { hata = "Bulunamadı" }).ConfigureAwait(false);
                return;
            }

            using var okuyucu = new StreamReader(istek.InputStream, Encoding.UTF8);
            var govde = await okuyucu.ReadToEndAsync().ConfigureAwait(false);
            using var belge = JsonDocument.Parse(string.IsNullOrWhiteSpace(govde) ? "{}" : govde);
            var j = belge.RootElement;

            switch (yol)
            {
                case "/api/analiz":
                    await _asistan.AnalizEtAsync().ConfigureAwait(false);
                    break;
                case "/api/uygula":
                    var anahtarlar = j.TryGetProperty("anahtarlar", out var aa) ? aa.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>();
                    await _asistan.UygulaAsync(anahtarlar).ConfigureAwait(false);
                    break;
                case "/api/geri-al":
                    await _asistan.GeriAlAsync(j.GetProperty("id").GetString()!).ConfigureAwait(false);
                    break;
                case "/api/eksikleri-gider":
                    await _asistan.EksikleriGiderAsync(j.TryGetProperty("oyuncular", out var oy) && oy.ValueKind == JsonValueKind.True).ConfigureAwait(false);
                    break;
                case "/api/yetenek":
                    await _asistan.YetenekTestiAsync().ConfigureAwait(false);
                    break;
                case "/api/yazma-testi":
                    await _asistan.YazmaTestiAsync().ConfigureAwait(false);
                    break;
                case "/api/ayar":
                    AyarUygula(j);
                    break;
                default:
                    await JsonGonder(yanit, 404, new { hata = "Bilinmeyen işlem" }).ConfigureAwait(false);
                    return;
            }

            await JsonGonder(yanit, 200, Gorunum.Olustur(_asistan)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log?.Invoke("Panel isteği hatası: " + e);

            try
            {
                await JsonGonder(yanit, 500, new { hata = e.Message }).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Bağlantı kapanmışsa yapılacak bir şey yok.
            }
        }
    }

    private void AyarUygula(JsonElement j)
    {
        var islem = j.GetProperty("islem").GetString();

        _asistan.AyarDegistir(a =>
        {
            switch (islem)
            {
                case "fizyoterapist":
                    a.FizyoterapistVar = j.GetProperty("deger").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
                    break;
                case "seansKilidi":
                    Degistir(a.KilitliSeanslar, j.GetProperty("anahtar").GetString()!, j.GetProperty("deger").GetBoolean());
                    break;
                case "oyuncuKilidi":
                    Degistir(a.KilitliOyuncular, j.GetProperty("id").GetInt32(), j.GetProperty("deger").GetBoolean());
                    break;
                case "oyuncuHaric":
                    Degistir(a.HaricOyuncular, j.GetProperty("id").GetInt32(), j.GetProperty("deger").GetBoolean());
                    break;
                case "seansEslestir":
                    var id = j.GetProperty("id").GetInt32();
                    var amac = j.GetProperty("amac").GetString();
                    if (string.IsNullOrEmpty(amac))
                    {
                        a.SeansEslestirmeleri.Remove(id);
                    }
                    else
                    {
                        var alan = j.TryGetProperty("alan", out var al) && al.ValueKind == JsonValueKind.String && Enum.TryParse<TaktikAlan>(al.GetString(), out var x) ? x : (TaktikAlan?)null;
                        a.SeansEslestirmeleri[id] = new SeansEslestirmesi(Enum.Parse<SeansAmaci>(amac), alan);
                    }

                    break;
                case "esik":
                    var ad = j.GetProperty("ad").GetString();
                    var prop = typeof(Ayarlar).GetProperty(ad ?? "", BindingFlags.Public | BindingFlags.Instance);
                    if (prop is not null && (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(double)))
                    {
                        var d = j.GetProperty("deger").GetDouble();
                        prop.SetValue(a, prop.PropertyType == typeof(int) ? (object)(int)Math.Round(d) : d);
                    }

                    break;
                case "otomatik":
                    a.OtomatikUygula = j.GetProperty("deger").GetBoolean();
                    break;
                case "surumUnut":
                    a.DogrulanmisSurumler.Remove(j.GetProperty("surum").GetString()!);
                    break;
            }
        });
    }

    private static void Degistir<T>(ISet<T> kume, T deger, bool ekle)
    {
        if (ekle) kume.Add(deger);
        else kume.Remove(deger);
    }

    private static string Kaynak(string ad)
    {
        var asm = typeof(WebSunucu).Assembly;
        using var akis = asm.GetManifestResourceStream("Asistan.Core.Arayuz." + ad)
                         ?? throw new InvalidOperationException("Gömülü kaynak yok: " + ad);
        using var okuyucu = new StreamReader(akis, Encoding.UTF8);
        return okuyucu.ReadToEnd();
    }

    private static Task JsonGonder(HttpListenerResponse yanit, int kod, object veri) =>
        Gonder(yanit, kod, "application/json; charset=utf-8", JsonSerializer.Serialize(veri, Json));

    private static async Task Gonder(HttpListenerResponse yanit, int kod, string tur, string metin)
    {
        var bayt = Encoding.UTF8.GetBytes(metin);
        yanit.StatusCode = kod;
        yanit.ContentType = tur;
        yanit.Headers["Cache-Control"] = "no-store";
        yanit.ContentLength64 = bayt.Length;
        await yanit.OutputStream.WriteAsync(bayt).ConfigureAwait(false);
        yanit.OutputStream.Close();
    }

    public void Dispose()
    {
        _iptal.Cancel();

        try
        {
            _dinleyici.Stop();
            _dinleyici.Close();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
