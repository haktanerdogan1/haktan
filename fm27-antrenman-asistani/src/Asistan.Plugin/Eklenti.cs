using Asistan.Core;
using Asistan.Core.Arayuz;
using Asistan.Core.Depo;
using Asistan.Core.Veri;
using BepInEx;
using BepInEx.Unity.IL2CPP;
#if IMGUI_PANEL
using Il2CppInterop.Runtime.Injection;
#endif

namespace Asistan.Plugin;

/// <summary>
/// FM27 içinde çalışan Antrenman Asistanı. Oyunla MCPFM üzerinden konuşur (MCPFM köprüsü ve API'si açık olmalı),
/// oyun içinde F8 ile açılan bir panel ve aynı işlevleri tarayıcıda sunan yerel bir panel açar.
/// </summary>
[BepInPlugin(Kimlik, Ad, Surum)]
[BepInDependency("mcpfm.bridge", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Eklenti : BasePlugin
{
    public const string Kimlik = "fm27.trainingagents";
    public const string Ad = "TrainingAgents";
    public const string Surum = "1.0.0";

    internal static AntrenmanAsistani? Asistan { get; private set; }
    internal static WebSunucu? Sunucu { get; private set; }
    internal static string Kisayol { get; private set; } = "F8";
    internal static Action<string>? Logla { get; private set; }

    private McpfmBaglantisi? _baglanti;
    private Timer? _otomatik;

    public override void Load()
    {
        var api = Config.Bind("Baglanti", "McpfmApi", "http://localhost:5120", "MCPFM API adresi (FootballManager.Api).").Value;
        var kopru = Config.Bind("Baglanti", "McpfmKopru", "http://localhost:5121", "MCPFM oyun içi köprü adresi (sürüm okumak için).").Value;
        var port = Config.Bind("Panel", "Port", 5130, "Yerel panelin portu (yalnızca bu bilgisayardan erişilir).").Value;
        Kisayol = Config.Bind("Panel", "Kisayol", "F8", "Oyun içi paneli açıp kapatan tuş (Unity KeyCode adı).").Value;

        Logla = m => Log.LogInfo(m);
        var klasor = Path.Combine(Paths.ConfigPath, "AntrenmanAsistani");
        _baglanti = new McpfmBaglantisi(api, kopru);
        Asistan = new AntrenmanAsistani(_baglanti, new YerelDepo(klasor));

        try
        {
            Sunucu = new WebSunucu(Asistan, port, m => Log.LogInfo(m));
            Sunucu.Baslat();
        }
        catch (Exception e)
        {
            Log.LogError($"Yerel panel {port} portunda açılamadı: {e.Message}. Ayarı BepInEx\\config\\{Kimlik}.cfg içinden değiştirin.");
        }

        // Otomatik mod (Ayarlar'dan açılır): her dakika oyunun haftası/taktiği değişti mi diye bakar.
        // Canlı bağlantı: birkaç saniyede bir oyunu yoklar; taktik ya da anlayış değişince analiz yenilenir, otomatik mod da buradan çalışır.
        _otomatik = new Timer(_ =>
        {
            var a = Asistan;
            if (a is null || a.Mesgul) return;
            _ = Task.Run(async () =>
            {
                try { await a.CanliKontrolAsync().ConfigureAwait(false); }
                catch (Exception e) { Log.LogWarning("Canlı kontrol: " + e.Message); }
            });
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

#if IMGUI_PANEL
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<OyunIciPanel>())
            {
                ClassInjector.RegisterTypeInIl2Cpp<OyunIciPanel>();
            }

            AddComponent<OyunIciPanel>();
            Log.LogInfo($"{Ad} {Surum} yüklendi. Oyun içi panel: {Kisayol}. Tarayıcı paneli: {Sunucu?.Adres}");
        }
        catch (Exception e)
        {
            Log.LogError("Oyun içi panel başlatılamadı (" + e.Message + "). Tarayıcı paneli kullanılabilir: " + Sunucu?.Adres);
        }
#else
        Log.LogInfo($"{Ad} {Surum} yüklendi (yalnızca tarayıcı paneli): {Sunucu?.Adres}");
#endif
    }

    public override bool Unload()
    {
        _otomatik?.Dispose();
        Sunucu?.Dispose();
        _baglanti?.Dispose();
        return true;
    }
}
