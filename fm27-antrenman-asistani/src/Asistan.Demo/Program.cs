using Asistan.Core;
using Asistan.Core.Arayuz;
using Asistan.Core.Depo;
using Asistan.Tests;

// Sahte bir kariyer: bu hafta Cumartesi, gelecek hafta Çarşamba ve Cumartesi maç; pres anlayışı düşük.
var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5130;
var klasor = Path.Combine(Path.GetTempPath(), "antrenman-asistani-demo");
var oyun = new SahteOyun(Senaryo.Antrenman(new[] { 5, 9, 12 }), Senaryo.Taktik("41-49", digerleri: null), Senaryo.Kadro());
oyun.Taktikler!.Slots[0].Effectiveness!.TacticalUnderstanding!.LineOfEngagement = new() { Understanding = "58", MatchExposure = "55", TrainingContribution = "30" };
oyun.Taktikler.Slots[0].Effectiveness!.TacticalUnderstanding!.ChanceCreation = new() { Understanding = "66", MatchExposure = "60", TrainingContribution = "35" };
oyun.Taktikler.Active!.TeamInstructions["LineOfEngagement"] = "HigherLineOfEngagement";

var depo = new YerelDepo(klasor);
var ayar = depo.AyarlariOku();
if (!ayar.DogrulanmisSurumler.Any()) { ayar.DogrulanmisSurumler.Add("27.1.0"); depo.AyarlariYaz(ayar); }

var asistan = new AntrenmanAsistani(oyun, depo);
using var sunucu = new WebSunucu(asistan, port, Console.WriteLine);
sunucu.Baslat();
Console.WriteLine("Demo çalışıyor. Kapatmak için Ctrl+C.");
await Task.Delay(Timeout.Infinite);
