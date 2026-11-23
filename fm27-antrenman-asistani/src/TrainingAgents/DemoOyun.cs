using Asistan.Core.Veri;
using Asistan.Tests;

namespace TrainingAgents;

/// <summary>"TrainingAgents.exe --demo": oyun açmadan arayüzü denemek için sahte bir kariyer.</summary>
internal static class DemoOyun
{
    public static IOyunBaglantisi Olustur()
    {
        var antrenman = Senaryo.Antrenman(new[] { 5, 9, 12 });

        foreach (var s in antrenman.SessionTypes.SelectMany(c => c.Sessions))
        {
            s.Name = s.Id switch { 11 => "Oyun Kurulum", 12 => "Pozisyon Üretimi", _ => s.Name };
        }

        var taktik = Senaryo.Taktik("52-58");
        taktik.Slots[0].Name = "Demo 4-2-3-1";
        var ef = taktik.Slots[0].Effectiveness!;
        ef.Overall = "61";
        ef.PlayerAbility = new YetenekPuaniDto { Overall = "58", Advice = new TavsiyeDto { Text = "Bazı oyuncular rollerinde yeterince rahat değil.", IsPositive = false } };
        ef.TacticalUnderstanding!.LineOfEngagement = new TaktikAlanDto { Understanding = "48-55", MatchExposure = "45", TrainingContribution = "20" };
        ef.TacticalUnderstanding.ChanceCreation = new TaktikAlanDto { Understanding = "70", MatchExposure = "60", TrainingContribution = "35" };
        ef.TacticalUnderstanding.AreasAdvice = new TavsiyeDto { Text = "Antrenmanlarda Baskı Hattı kurgularına daha fazla zaman ayırmamız gerekiyor.", IsPositive = false };
        taktik.Active!.TeamInstructions["LineOfEngagement"] = "HigherLineOfEngagement";
        return new SahteOyun(antrenman, taktik, Senaryo.Kadro());
    }
}
