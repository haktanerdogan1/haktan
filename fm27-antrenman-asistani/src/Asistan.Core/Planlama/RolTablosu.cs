namespace Asistan.Core.Planlama;

public enum RolEvresi { TopaSahipken, TopRakipteyken }

/// <summary>Bir FM27 rolü ve öncelikli/destekleyici özellikleri (MCPFM özellik anahtarlarıyla).</summary>
public sealed record RolTanimi(string Anahtar, string Ad, RolEvresi Evre, string[] MevkiGruplari, string[] Oncelikli, string[] Destekleyici)
{
    public IEnumerable<string> Hepsi => Oncelikli.Concat(Destekleyici);
}

/// <summary>
/// FM27'nin topa sahipken ve top rakipteyken rolleri. Özellik öncelikleri Touchline FM27 rol rehberinden (touchlinefm.com/tactics)
/// alınmıştır; rehberin kendi notuyla: "resmi FM27 özellik vurguları ya da asgari değerler değildir, bir koçluk yorumudur".
/// Rol anahtarı, oyunun İngilizce rol adındaki harflerdir (MCPFM'in RoleKey biçimi: "CentreForward").
/// </summary>
public static class RolTablosu
{
    public static readonly IReadOnlyList<RolTanimi> Hepsi = new RolTanimi[]
    {
        new("MidfieldPlaymaker", "Midfield Playmaker", RolEvresi.TopaSahipken, new[] { "Central midfield" }, new[] { "Passing", "Vision", "FirstTouch", "Decisions" }, new[] { "Technique", "Composure", "Teamwork" }),
        new("BallPlayingGoalkeeper", "Ball-Playing Goalkeeper", RolEvresi.TopaSahipken, new[] { "Goalkeeper" }, new[] { "Passing", "FirstTouch", "Decisions", "Composure" }, new[] { "Kicking", "Vision", "Technique" }),
        new("Goalkeeper", "Goalkeeper", RolEvresi.TopaSahipken, new[] { "Goalkeeper" }, new[] { "Decisions", "Kicking", "Throwing", "FirstTouch" }, new[] { "Passing", "Composure", "Technique" }),
        new("NoNonsenseGoalkeeper", "No-Nonsense Goalkeeper", RolEvresi.TopaSahipken, new[] { "Goalkeeper" }, new[] { "Kicking", "Decisions", "Concentration" }, new[] { "Throwing", "Handling", "Composure" }),
        new("Goalkeeper", "Goalkeeper", RolEvresi.TopRakipteyken, new[] { "Goalkeeper" }, new[] { "Reflexes", "Positioning", "Handling", "Decisions" }, new[] { "AerialReach", "OneOnOnes", "Communication" }),
        new("LineHoldingGoalkeeper", "Line-Holding Goalkeeper", RolEvresi.TopRakipteyken, new[] { "Goalkeeper" }, new[] { "Reflexes", "Positioning", "Handling", "Concentration" }, new[] { "OneOnOnes", "AerialReach", "Communication" }),
        new("SweeperKeeper", "Sweeper Keeper", RolEvresi.TopRakipteyken, new[] { "Goalkeeper" }, new[] { "RushingOut", "Anticipation", "Decisions", "Acceleration" }, new[] { "OneOnOnes", "Composure", "Concentration" }),
        new("AdvancedCentreBack", "Advanced Centre-Back", RolEvresi.TopaSahipken, new[] { "Centre-back", "Wide centre-back" }, new[] { "Dribbling", "Passing", "Decisions", "FirstTouch" }, new[] { "Composure", "Movement", "Anticipation" }),
        new("BallPlayingCentreBack", "Ball-Playing Centre-Back", RolEvresi.TopaSahipken, new[] { "Centre-back", "Wide centre-back" }, new[] { "Passing", "Vision", "Decisions", "Composure" }, new[] { "FirstTouch", "Technique", "Anticipation" }),
        new("CentreBack", "Centre-Back", RolEvresi.TopaSahipken, new[] { "Centre-back", "Wide centre-back" }, new[] { "Passing", "FirstTouch", "Decisions" }, new[] { "Composure", "Technique", "Teamwork" }),
        new("NoNonsenseCentreBack", "No-Nonsense Centre-Back", RolEvresi.TopaSahipken, new[] { "Centre-back", "Wide centre-back" }, new[] { "Decisions", "Heading", "Strength", "Concentration" }, new[] { "Positioning", "Teamwork", "Passing" }),
        new("CentreBack", "Centre-Back", RolEvresi.TopRakipteyken, new[] { "Centre-back" }, new[] { "Positioning", "Marking", "Tackling", "Heading" }, new[] { "Anticipation", "Concentration", "Strength" }),
        new("CoveringCentreBack", "Covering Centre-Back", RolEvresi.TopRakipteyken, new[] { "Centre-back" }, new[] { "Anticipation", "Positioning", "Pace", "Concentration" }, new[] { "Marking", "Decisions", "Tackling" }),
        new("StoppingCentreBack", "Stopping Centre-Back", RolEvresi.TopRakipteyken, new[] { "Centre-back" }, new[] { "Anticipation", "Tackling", "Decisions", "Strength" }, new[] { "Marking", "Aggression", "Heading" }),
        new("OverlappingCentreBack", "Overlapping Centre-Back", RolEvresi.TopaSahipken, new[] { "Wide centre-back" }, new[] { "Movement", "Stamina", "Passing", "Decisions" }, new[] { "Crossing", "Dribbling", "WorkRate" }),
        new("WideCentreBack", "Wide Centre-Back", RolEvresi.TopaSahipken, new[] { "Wide centre-back" }, new[] { "Passing", "FirstTouch", "Decisions", "Teamwork" }, new[] { "Composure", "Dribbling", "Stamina" }),
        new("CoveringWideCentreBack", "Covering Wide Centre-Back", RolEvresi.TopRakipteyken, new[] { "Wide centre-back" }, new[] { "Anticipation", "Positioning", "Pace", "Decisions" }, new[] { "Marking", "Concentration", "Tackling" }),
        new("StoppingWideCentreBack", "Stopping Wide Centre-Back", RolEvresi.TopRakipteyken, new[] { "Wide centre-back" }, new[] { "Anticipation", "Tackling", "Decisions", "Acceleration" }, new[] { "Strength", "Marking", "WorkRate" }),
        new("WideCentreBack", "Wide Centre-Back", RolEvresi.TopRakipteyken, new[] { "Wide centre-back" }, new[] { "Positioning", "Anticipation", "Marking", "Pace" }, new[] { "Tackling", "Concentration", "Strength" }),
        new("FullBack", "Full-Back", RolEvresi.TopaSahipken, new[] { "Full-back" }, new[] { "Passing", "Decisions", "Movement", "Crossing" }, new[] { "Stamina", "FirstTouch", "Teamwork" }),
        new("InsideFullBack", "Inside Full-Back", RolEvresi.TopaSahipken, new[] { "Full-back" }, new[] { "Decisions", "Passing", "FirstTouch", "Teamwork" }, new[] { "Composure", "Anticipation", "Technique" }),
        new("InsideWingBack", "Inside Wing-Back", RolEvresi.TopaSahipken, new[] { "Full-back", "Wing-back" }, new[] { "FirstTouch", "Passing", "Decisions", "Movement" }, new[] { "Composure", "Technique", "Teamwork" }),
        new("PlaymakingWingBack", "Playmaking Wing-Back", RolEvresi.TopaSahipken, new[] { "Full-back", "Wing-back" }, new[] { "Passing", "Vision", "Technique", "Decisions" }, new[] { "FirstTouch", "Composure", "Crossing" }),
        new("WingBack", "Wing-Back", RolEvresi.TopaSahipken, new[] { "Full-back", "Wing-back" }, new[] { "Stamina", "Crossing", "Movement", "WorkRate" }, new[] { "Acceleration", "Dribbling", "Decisions" }),
        new("FullBack", "Full-Back", RolEvresi.TopRakipteyken, new[] { "Full-back" }, new[] { "Positioning", "Marking", "Tackling", "Anticipation" }, new[] { "Pace", "Concentration", "Teamwork" }),
        new("HoldingFullBack", "Holding Full-Back", RolEvresi.TopRakipteyken, new[] { "Full-back" }, new[] { "Positioning", "Marking", "Concentration", "Tackling" }, new[] { "Anticipation", "Decisions", "Pace" }),
        new("PressingFullBack", "Pressing Full-Back", RolEvresi.TopRakipteyken, new[] { "Full-back" }, new[] { "WorkRate", "Anticipation", "Acceleration", "Decisions" }, new[] { "Stamina", "Tackling", "Teamwork" }),
        new("AdvancedWingBack", "Advanced Wing-Back", RolEvresi.TopaSahipken, new[] { "Wing-back" }, new[] { "Stamina", "Movement", "Crossing", "Acceleration" }, new[] { "Dribbling", "WorkRate", "Decisions" }),
        new("HoldingWingBack", "Holding Wing-Back", RolEvresi.TopRakipteyken, new[] { "Wing-back" }, new[] { "Positioning", "Anticipation", "Marking", "Concentration" }, new[] { "Tackling", "Decisions", "Pace" }),
        new("PressingWingBack", "Pressing Wing-Back", RolEvresi.TopRakipteyken, new[] { "Wing-back" }, new[] { "WorkRate", "Acceleration", "Anticipation", "Stamina" }, new[] { "Tackling", "Decisions", "Teamwork" }),
        new("WingBack", "Wing-Back", RolEvresi.TopRakipteyken, new[] { "Wing-back" }, new[] { "Positioning", "Stamina", "Marking", "Tackling" }, new[] { "WorkRate", "Anticipation", "Pace" }),
        new("BoxToBoxMidfielder", "Box-To-Box Midfielder", RolEvresi.TopaSahipken, new[] { "Defensive midfield" }, new[] { "Stamina", "Movement", "WorkRate", "FirstTouch" }, new[] { "Passing", "Anticipation", "Finishing" }),
        new("BoxToBoxPlaymaker", "Box-To-Box Playmaker", RolEvresi.TopaSahipken, new[] { "Defensive midfield" }, new[] { "Passing", "Vision", "Stamina", "Decisions" }, new[] { "FirstTouch", "Technique", "Movement" }),
        new("DeepLyingPlaymaker", "Deep-Lying Playmaker", RolEvresi.TopaSahipken, new[] { "Defensive midfield" }, new[] { "Passing", "Vision", "FirstTouch", "Decisions" }, new[] { "Composure", "Technique", "Teamwork" }),
        new("DefensiveMidfielder", "Defensive Midfielder", RolEvresi.TopaSahipken, new[] { "Defensive midfield" }, new[] { "Passing", "FirstTouch", "Decisions", "Teamwork" }, new[] { "Composure", "Technique", "Vision" }),
        new("HalfBack", "Half Back", RolEvresi.TopaSahipken, new[] { "Defensive midfield" }, new[] { "Decisions", "Passing", "FirstTouch", "Teamwork" }, new[] { "Composure", "Anticipation", "Technique" }),
        new("DefensiveMidfielder", "Defensive Midfielder", RolEvresi.TopRakipteyken, new[] { "Defensive midfield" }, new[] { "Positioning", "Anticipation", "Tackling", "Decisions" }, new[] { "Marking", "Concentration", "Teamwork" }),
        new("DroppingDefensiveMidfielder", "Dropping Defensive Midfielder", RolEvresi.TopRakipteyken, new[] { "Defensive midfield" }, new[] { "Positioning", "Anticipation", "Marking", "Concentration" }, new[] { "Heading", "Tackling", "Teamwork" }),
        new("PressingDefensiveMidfielder", "Pressing Defensive Midfielder", RolEvresi.TopRakipteyken, new[] { "Defensive midfield" }, new[] { "WorkRate", "Anticipation", "Stamina", "Decisions" }, new[] { "Tackling", "Aggression", "Teamwork" }),
        new("ScreeningDefensiveMidfielder", "Screening Defensive Midfielder", RolEvresi.TopRakipteyken, new[] { "Defensive midfield" }, new[] { "Positioning", "Anticipation", "Concentration", "Teamwork" }, new[] { "Marking", "Decisions", "Tackling" }),
        new("WideCoveringDefensiveMidfielder", "Wide Covering Defensive Midfielder", RolEvresi.TopRakipteyken, new[] { "Defensive midfield" }, new[] { "Positioning", "Anticipation", "Stamina", "Teamwork" }, new[] { "WorkRate", "Tackling", "Acceleration" }),
        new("CentralMidfielder", "Central Midfielder", RolEvresi.TopaSahipken, new[] { "Central midfield" }, new[] { "Passing", "FirstTouch", "Decisions", "Movement" }, new[] { "Teamwork", "Technique", "Composure" }),
        new("ChannelMidfielder", "Channel Midfielder", RolEvresi.TopaSahipken, new[] { "Central midfield", "Attacking midfield" }, new[] { "Movement", "Stamina", "Acceleration", "Passing" }, new[] { "FirstTouch", "Anticipation", "Crossing" }),
        new("WideCentralMidfielder", "Wide Central Midfielder", RolEvresi.TopaSahipken, new[] { "Central midfield" }, new[] { "Movement", "Passing", "Decisions", "Stamina" }, new[] { "FirstTouch", "Technique", "WorkRate" }),
        new("CentralMidfielder", "Central Midfielder", RolEvresi.TopRakipteyken, new[] { "Central midfield" }, new[] { "Positioning", "Anticipation", "Teamwork", "Tackling" }, new[] { "WorkRate", "Concentration", "Marking" }),
        new("PressingCentralMidfielder", "Pressing Central Midfielder", RolEvresi.TopRakipteyken, new[] { "Central midfield" }, new[] { "WorkRate", "Anticipation", "Acceleration", "Stamina" }, new[] { "Tackling", "Decisions", "Teamwork" }),
        new("ScreeningCentralMidfielder", "Screening Central Midfielder", RolEvresi.TopRakipteyken, new[] { "Central midfield" }, new[] { "Positioning", "Anticipation", "Concentration", "Teamwork" }, new[] { "Marking", "Decisions", "Tackling" }),
        new("WideCoveringCentralMidfielder", "Wide Covering Central Midfielder", RolEvresi.TopRakipteyken, new[] { "Central midfield" }, new[] { "Positioning", "Teamwork", "Stamina", "Anticipation" }, new[] { "WorkRate", "Marking", "Tackling" }),
        new("WideMidfielder", "Wide Midfielder", RolEvresi.TopaSahipken, new[] { "Wide midfield" }, new[] { "Crossing", "Passing", "Decisions", "FirstTouch" }, new[] { "Teamwork", "Movement", "Technique" }),
        new("TrackingWideMidfielder", "Tracking Wide Midfielder", RolEvresi.TopRakipteyken, new[] { "Wide midfield" }, new[] { "WorkRate", "Stamina", "Marking", "Positioning" }, new[] { "Teamwork", "Anticipation", "Acceleration" }),
        new("WideMidfielder", "Wide Midfielder", RolEvresi.TopRakipteyken, new[] { "Wide midfield" }, new[] { "Positioning", "Marking", "WorkRate", "Teamwork" }, new[] { "Stamina", "Anticipation", "Tackling" }),
        new("WideOutletWideMidfielder", "Wide Outlet Wide Midfielder", RolEvresi.TopRakipteyken, new[] { "Wide midfield" }, new[] { "Movement", "FirstTouch", "Acceleration", "Decisions" }, new[] { "Pace", "Passing", "Composure" }),
        new("AdvancedPlaymaker", "Advanced Playmaker", RolEvresi.TopaSahipken, new[] { "Attacking midfield", "Central midfield" }, new[] { "Vision", "Passing", "FirstTouch", "Movement" }, new[] { "Technique", "Composure", "Decisions" }),
        new("AttackingMidfielder", "Attacking Midfielder", RolEvresi.TopaSahipken, new[] { "Attacking midfield", "Central midfield" }, new[] { "Movement", "FirstTouch", "Decisions", "Passing" }, new[] { "Technique", "Anticipation", "Finishing" }),
        new("FreeRole", "Free Role", RolEvresi.TopaSahipken, new[] { "Attacking midfield" }, new[] { "Movement", "Vision", "Decisions", "Technique" }, new[] { "Dribbling", "Passing", "Flair" }),
        new("SecondStriker", "Second Striker", RolEvresi.TopaSahipken, new[] { "Attacking midfield" }, new[] { "Movement", "Anticipation", "Finishing", "Composure" }, new[] { "Acceleration", "FirstTouch", "Decisions" }),
        new("AttackingMidfielder", "Attacking Midfielder", RolEvresi.TopRakipteyken, new[] { "Attacking midfield" }, new[] { "Positioning", "Anticipation", "Teamwork", "WorkRate" }, new[] { "Marking", "Decisions", "Stamina" }),
        new("CentralOutletAttackingMidfielder", "Central Outlet Attacking Midfielder", RolEvresi.TopRakipteyken, new[] { "Attacking midfield" }, new[] { "Movement", "FirstTouch", "Composure", "Passing" }, new[] { "Vision", "Balance", "Decisions" }),
        new("SplittingOutletAttackingMidfielder", "Splitting Outlet Attacking Midfielder", RolEvresi.TopRakipteyken, new[] { "Attacking midfield" }, new[] { "Movement", "Anticipation", "Acceleration", "FirstTouch" }, new[] { "Pace", "Composure", "Finishing" }),
        new("TrackingAttackingMidfielder", "Tracking Attacking Midfielder", RolEvresi.TopRakipteyken, new[] { "Attacking midfield" }, new[] { "WorkRate", "Stamina", "Teamwork", "Positioning" }, new[] { "Marking", "Anticipation", "Decisions" }),
        new("InsideForward", "Inside Forward", RolEvresi.TopaSahipken, new[] { "Wide attack" }, new[] { "Movement", "Finishing", "Dribbling", "Composure" }, new[] { "Acceleration", "Anticipation", "Technique" }),
        new("InsideWinger", "Inside Winger", RolEvresi.TopaSahipken, new[] { "Wide attack", "Wide midfield" }, new[] { "Dribbling", "Passing", "FirstTouch", "Decisions" }, new[] { "Technique", "Vision", "Movement" }),
        new("PlaymakingWinger", "Playmaking Winger", RolEvresi.TopaSahipken, new[] { "Wide attack", "Wide midfield" }, new[] { "Passing", "Vision", "FirstTouch", "Decisions" }, new[] { "Technique", "Composure", "Movement" }),
        new("WideForward", "Wide Forward", RolEvresi.TopaSahipken, new[] { "Wide attack" }, new[] { "Movement", "Acceleration", "Finishing", "Anticipation" }, new[] { "Pace", "Dribbling", "Composure" }),
        new("Winger", "Winger", RolEvresi.TopaSahipken, new[] { "Wide attack", "Wide midfield" }, new[] { "Dribbling", "Acceleration", "Crossing", "Movement" }, new[] { "Technique", "Pace", "Decisions" }),
        new("InsideOutletWinger", "Inside Outlet Winger", RolEvresi.TopRakipteyken, new[] { "Wide attack" }, new[] { "Movement", "Anticipation", "Acceleration", "Finishing" }, new[] { "FirstTouch", "Pace", "Composure" }),
        new("TrackingWinger", "Tracking Winger", RolEvresi.TopRakipteyken, new[] { "Wide attack" }, new[] { "WorkRate", "Stamina", "Marking", "Teamwork" }, new[] { "Acceleration", "Positioning", "Anticipation" }),
        new("WideOutletWinger", "Wide Outlet Winger", RolEvresi.TopRakipteyken, new[] { "Wide attack" }, new[] { "Movement", "Acceleration", "FirstTouch", "Pace" }, new[] { "Dribbling", "Decisions", "Composure" }),
        new("Winger", "Winger", RolEvresi.TopRakipteyken, new[] { "Wide attack" }, new[] { "WorkRate", "Positioning", "Teamwork", "Anticipation" }, new[] { "Stamina", "Marking", "Acceleration" }),
        new("CentreForward", "Centre Forward", RolEvresi.TopaSahipken, new[] { "Striker" }, new[] { "Movement", "FirstTouch", "Finishing", "Decisions" }, new[] { "Composure", "Strength", "Anticipation" }),
        new("ChannelForward", "Channel Forward", RolEvresi.TopaSahipken, new[] { "Striker" }, new[] { "Movement", "Anticipation", "Acceleration", "Finishing" }, new[] { "Pace", "Composure", "FirstTouch" }),
        new("DeepLyingForward", "Deep-Lying Forward", RolEvresi.TopaSahipken, new[] { "Striker" }, new[] { "FirstTouch", "Passing", "Strength", "Decisions" }, new[] { "Teamwork", "Vision", "Composure" }),
        new("FalseNine", "False Nine", RolEvresi.TopaSahipken, new[] { "Striker" }, new[] { "Movement", "FirstTouch", "Passing", "Vision" }, new[] { "Dribbling", "Decisions", "Technique" }),
        new("Poacher", "Poacher", RolEvresi.TopaSahipken, new[] { "Striker" }, new[] { "Movement", "Anticipation", "Finishing", "Composure" }, new[] { "Acceleration", "FirstTouch", "Pace" }),
        new("TargetForward", "Target Forward", RolEvresi.TopaSahipken, new[] { "Striker" }, new[] { "JumpingReach", "Heading", "Strength", "FirstTouch" }, new[] { "Balance", "Teamwork", "Bravery" }),
        new("CentralOutletCentreForward", "Central Outlet Centre Forward", RolEvresi.TopRakipteyken, new[] { "Striker" }, new[] { "FirstTouch", "Strength", "Movement", "Balance" }, new[] { "Heading", "Composure", "Teamwork" }),
        new("CentreForward", "Centre Forward", RolEvresi.TopRakipteyken, new[] { "Striker" }, new[] { "WorkRate", "Anticipation", "Stamina", "Teamwork" }, new[] { "Acceleration", "Decisions", "Aggression" }),
        new("SplittingOutletCentreForward", "Splitting Outlet Centre Forward", RolEvresi.TopRakipteyken, new[] { "Striker" }, new[] { "Movement", "Acceleration", "Anticipation", "Pace" }, new[] { "Finishing", "FirstTouch", "Composure" }),
        new("TrackingCentreForward", "Tracking Centre Forward", RolEvresi.TopRakipteyken, new[] { "Striker" }, new[] { "WorkRate", "Stamina", "Teamwork", "Marking" }, new[] { "Positioning", "Anticipation", "Decisions" }),
    };

    private static string Sade(string s) => new string((s ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();

    /// <summary>Rol anahtarı ya da İngilizce adıyla rol bulur ("CentreForward", "Centre Forward", "Pressing Full-Back").</summary>
    public static RolTanimi? Bul(string? rol, RolEvresi evre)
    {
        if (string.IsNullOrWhiteSpace(rol))
        {
            return null;
        }

        var s = Sade(rol);
        return Hepsi.FirstOrDefault(r => r.Evre == evre && Sade(r.Anahtar) == s)
               ?? Hepsi.FirstOrDefault(r => r.Evre == evre && s.Length > 6 && Sade(r.Anahtar).EndsWith(s, StringComparison.Ordinal));
    }

    /// <summary>Aynı mevki grubundaki diğer roller (alternatif rol önerisi için).</summary>
    public static IEnumerable<RolTanimi> AyniMevki(RolTanimi rol) =>
        Hepsi.Where(r => r.Evre == rol.Evre && r.Anahtar != rol.Anahtar && r.MevkiGruplari.Intersect(rol.MevkiGruplari).Any());
}
