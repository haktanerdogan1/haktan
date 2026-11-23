using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Asistan.Core;
using Asistan.Core.Arayuz;
using Asistan.Core.Depo;
using Asistan.Core.Veri;
using System.Diagnostics;
using Photino.NET;

namespace TrainingAgents;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var tekKopya = new Mutex(true, "TrainingAgents.TekKopya", out var ilk);

        if (!ilk)
        {
            return; // İkinci kopya açılmaz; ilk pencere zaten oyunu izliyor.
        }

        var klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrainingAgents");
        Directory.CreateDirectory(klasor);
        var ayar = BaglantiAyari.Oku(klasor);
        var demo = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);

        IOyunBaglantisi baglanti = demo ? DemoOyun.Olustur() : new McpfmBaglantisi(ayar.McpfmApi, ayar.McpfmKopru);
        var depo = new YerelDepo(demo ? Path.Combine(klasor, "demo") : klasor);
        var asistan = new AntrenmanAsistani(baglanti, depo);

        var port = BosPort(ayar.PanelPortu);
        using var sunucu = new WebSunucu(asistan, port, m => Gunluk.Yaz(klasor, m));
        sunucu.Baslat();

        using var iptal = new CancellationTokenSource();
        _ = Task.Run(() => CanliDonguAsync(asistan, klasor, iptal.Token));

        Pencere(sunucu.Adres, demo, klasor);
        iptal.Cancel();
    }

    private static void Pencere(string adres, bool demo, string klasor)
    {
        var simge = Path.Combine(klasor, "TrainingAgents.ico");

        try
        {
            using var akis = typeof(Program).Assembly.GetManifestResourceStream("TrainingAgents.TrainingAgents.ico");
            if (akis is not null)
            {
                using var dosya = File.Create(simge);
                akis.CopyTo(dosya);
            }
        }
        catch (IOException)
        {
        }

        try
        {
            var pencere = new PhotinoWindow()
                .SetTitle(demo ? "TrainingAgents — demo" : "TrainingAgents")
                .SetUseOsDefaultSize(false)
                .SetSize(new System.Drawing.Size(1440, 900))
                .SetMinSize(1100, 700)
                .Center()
                .SetResizable(true)
                .SetDevToolsEnabled(false)
                .SetContextMenuEnabled(false)
                .SetIconFile(simge)
                .Load(new Uri(adres));
            pencere.WaitForClose();
        }
        catch (Exception e)
        {
            // WebView2 yoksa ya da pencere açılamadıysa panel varsayılan tarayıcıda açılır; uygulama oyunu izlemeye devam eder.
            Gunluk.Yaz(klasor, "Pencere açılamadı, tarayıcıya geçildi: " + e.Message);
            Process.Start(new ProcessStartInfo(adres) { UseShellExecute = true });
            Thread.Sleep(Timeout.Infinite);
        }
    }

    // Oyunu birkaç saniyede bir yoklar: bağlantı durumu, taktik/anlayış değişimi, otomatik mod.
    private static async Task CanliDonguAsync(AntrenmanAsistani asistan, string klasor, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await asistan.CanliKontrolAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Gunluk.Yaz(klasor, "Canlı kontrol: " + e.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(asistan.Baglanti == AntrenmanAsistani.BaglantiDurumu.Canli ? 5 : 3), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static int BosPort(int tercih)
    {
        for (var p = tercih; p < tercih + 20; p++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, p);
                l.Start();
                l.Stop();
                return p;
            }
            catch (SocketException)
            {
            }
        }

        return tercih;
    }
}

/// <summary>%APPDATA%\TrainingAgents\baglanti.json — MCPFM adresleri ve panel portu.</summary>
internal sealed class BaglantiAyari
{
    public string McpfmApi { get; set; } = "http://localhost:5120";
    public string McpfmKopru { get; set; } = "http://localhost:5121";
    public int PanelPortu { get; set; } = 5131;

    public static BaglantiAyari Oku(string klasor)
    {
        var yol = Path.Combine(klasor, "baglanti.json");

        try
        {
            if (File.Exists(yol))
            {
                return JsonSerializer.Deserialize<BaglantiAyari>(File.ReadAllText(yol)) ?? new BaglantiAyari();
            }

            File.WriteAllText(yol, JsonSerializer.Serialize(new BaglantiAyari(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Okunamayan ayar dosyası varsayılanlarla geçilir.
        }

        return new BaglantiAyari();
    }
}

internal static class Gunluk
{
    private static readonly object Kilit = new();

    public static void Yaz(string klasor, string metin)
    {
        lock (Kilit)
        {
            try
            {
                File.AppendAllText(Path.Combine(klasor, "trainingagents.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {metin}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }
    }
}
