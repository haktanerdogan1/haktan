using Asistan.Core;
using Asistan.Core.Arayuz;
using Asistan.Core.Depo;
using Asistan.Core.Veri;
using Asistan.Tests;

// Sahte kariyer, FM27 Türkçe ekranındaki duruma benzer: Karşılama Çizgisi ve Pres Yoğunluğu zayıf, oyun
// "Antrenmanlarda Baskı Hattı kurgularına daha fazla zaman ayırmamız gerekiyor." diyor.
var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5130;
var klasor = Path.Combine(Path.GetTempPath(), "antrenman-asistani-demo");
var antrenman = Senaryo.Antrenman(new[] { 5, 9, 12 });
foreach (var s in antrenman.SessionTypes.SelectMany(c => c.Sessions))
{
    s.Name = s.Id switch { 11 => "Oyun Kurulum", 12 => "Pozisyon Üretimi", _ => s.Name };
}

var taktik = Senaryo.Taktik("52-58", digerleri: null);
taktik.Slots[0].Name = "GYR - BEAST 4231";
var ta = taktik.Slots[0].Effectiveness!.TacticalUnderstanding!;
ta.LineOfEngagement = new TaktikAlanDto { Understanding = "48-55", MatchExposure = "45", TrainingContribution = "20" };
ta.ChanceCreation = new TaktikAlanDto { Understanding = "70", MatchExposure = "60", TrainingContribution = "35" };
ta.AreasAdvice = new TavsiyeDto { Text = "Antrenmanlarda Baskı Hattı kurgularına daha fazla zaman ayırmamız gerekiyor.", IsPositive = false };
taktik.Active!.TeamInstructions["LineOfEngagement"] = "HigherLineOfEngagement";

var oyun = new SahteOyun(antrenman, taktik, Senaryo.Kadro());
var depo = new YerelDepo(klasor);
var asistan = new AntrenmanAsistani(oyun, depo);
using var sunucu = new WebSunucu(asistan, port, Console.WriteLine);
sunucu.Baslat();
Console.WriteLine("Demo çalışıyor. Kapatmak için Ctrl+C.");
await Task.Delay(Timeout.Infinite);
