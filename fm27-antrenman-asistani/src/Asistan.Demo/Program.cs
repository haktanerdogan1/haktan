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
taktik.Slots[0].Effectiveness!.Overall = "61";
taktik.Slots[0].Effectiveness!.PlayerAbility = new YetenekPuaniDto { Overall = "58", Advice = new TavsiyeDto { Text = "Bazı oyuncular rollerinde yeterince rahat değil.", IsPositive = false } };
taktik.Slots[0].Effectiveness!.PlayerBuyIn = new BenimsemeDto { Overall = "74", Advice = new TavsiyeDto { Text = "Oyuncular taktik yaklaşımımızı destekliyor.", IsPositive = true } };
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

// Masaüstü uygulamasıyla aynı canlı döngü: birkaç saniyede bir oyunu yoklar.
while (true)
{
    try { await asistan.CanliKontrolAsync(); } catch (Exception ex) { Console.WriteLine(ex.Message); }
    await Task.Delay(TimeSpan.FromSeconds(3));
}
