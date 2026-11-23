namespace Asistan.Core.Veri;

// MCPFM API'sinin (http://localhost:5120/api/v1) JSON yanıtlarının birebir karşılıkları.
// Alan adları MCPFM 0.13.x kaynak kodundaki Response sınıflarından alınmıştır; JSON camelCase gelir.
// Bilinmeyen alanlar yok sayılır, eksik alanlar null kalır: null "okunamadı" demektir, sıfır değildir.

public sealed class SecenekDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class SeansDto
{
    public int Slot { get; set; }
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public int Category { get; set; }
    public bool? CanEdit { get; set; }
}

public sealed class GunDto
{
    public string Date { get; set; } = "";
    public List<SeansDto> Sessions { get; set; } = new();
    public string? Match { get; set; }
}

public sealed class HaftaDto
{
    public string? StartDate { get; set; }
    public List<GunDto> Days { get; set; } = new();
}

public sealed class SeansKategorisiDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<SecenekDto> Sessions { get; set; } = new();
}

public sealed class OyuncuAntrenmanDto
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = "";
    public string? Position { get; set; }
    public string? RoleKey { get; set; }
    public string? RoleName { get; set; }
    public bool FollowsMatchRole { get; set; }
    public SecenekDto? Focus { get; set; }
    public SecenekDto? Intensity { get; set; }
    public bool? IntensityIsAutomatic { get; set; }
    public int? RestDays { get; set; }
    public double? Rating { get; set; }
    public string? Workload { get; set; }
    public bool? FocusLocked { get; set; }
    public bool? IntensityLocked { get; set; }
}

public sealed class AntrenmanDto
{
    public string? Schedule { get; set; }
    public List<HaftaDto> Weeks { get; set; } = new();
    public List<SeansKategorisiDto> SessionTypes { get; set; } = new();
    public List<SecenekDto> FocusOptions { get; set; } = new();
    public List<SecenekDto> IntensityOptions { get; set; } = new();
    public List<OyuncuAntrenmanDto> Players { get; set; } = new();
}

public sealed class OyuncuDurumDto
{
    public string? Key { get; set; }
    public string? Label { get; set; }
    public string? Detail { get; set; }
}

public sealed class OyuncuDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? Age { get; set; }
    public string? Positions { get; set; }
    public string? BestRole { get; set; }
    public string? Morale { get; set; }
    public int? Condition { get; set; }
    public int? MatchSharpness { get; set; }
    public bool IsInjured { get; set; }
    public double? AverageRating { get; set; }
    public List<OyuncuDurumDto>? Statuses { get; set; }

    /// <summary>Kategori ("technical", "mental"...) → özellik ("Passing") → "12", "11-14" ya da maskeliyse null.</summary>
    public Dictionary<string, Dictionary<string, string?>>? Attributes { get; set; }
}

public sealed class KadroDto
{
    public string TeamName { get; set; } = "";
    public List<OyuncuDto> Players { get; set; } = new();
}

public sealed class TaktikAlanDto
{
    public string? Understanding { get; set; }
    public string? MatchExposure { get; set; }
    public string? TrainingContribution { get; set; }
}

public sealed class TavsiyeDto
{
    public string? Text { get; set; }
    public bool IsPositive { get; set; }
}

public sealed class YetenekPuaniDto
{
    public string? Overall { get; set; }
    public string? RoleSuitability { get; set; }
    public string? Capability { get; set; }
    public TavsiyeDto? Advice { get; set; }
}

public sealed class BenimsemeDto
{
    public string? Overall { get; set; }
    public string? ManagerSupport { get; set; }
    public string? Relationships { get; set; }
    public TavsiyeDto? Advice { get; set; }
}

public sealed class SonrakiMacDto
{
    public string? Overall { get; set; }
    public TavsiyeDto? Advice { get; set; }
}

public sealed class KarmasiklikDto
{
    public string? Overall { get; set; }
}

public sealed class TaktikAnlayisDto
{
    public string? Overall { get; set; }
    public string? Comprehension { get; set; }
    public TavsiyeDto? ComprehensionAdvice { get; set; }
    public KarmasiklikDto? Complexity { get; set; }
    public TaktikAlanDto? InPossessionShape { get; set; }
    public TaktikAlanDto? OutOfPossessionShape { get; set; }
    public TaktikAlanDto? BuildUpStrategy { get; set; }
    public TaktikAlanDto? ChanceCreation { get; set; }
    public TaktikAlanDto? PassingStyle { get; set; }
    public TaktikAlanDto? PressingIntensity { get; set; }
    public TaktikAlanDto? PressingTrap { get; set; }
    public TaktikAlanDto? LineOfEngagement { get; set; }
    public TavsiyeDto? AreasAdvice { get; set; }
}

public sealed class EtkinlikDto
{
    public string? Overall { get; set; }
    public YetenekPuaniDto? PlayerAbility { get; set; }
    public BenimsemeDto? PlayerBuyIn { get; set; }
    public TaktikAnlayisDto? TacticalUnderstanding { get; set; }
    public SonrakiMacDto? NextMatch { get; set; }
    public List<string>? Warnings { get; set; }
}

public sealed class TaktikSlotDto
{
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public EtkinlikDto? Effectiveness { get; set; }
}

public sealed class AktifTaktikDto
{
    public string? InPossessionFormation { get; set; }
    public string? OutOfPossessionFormation { get; set; }
    public string? InPossessionFormationKey { get; set; }
    public string? OutOfPossessionFormationKey { get; set; }
    public string? MentalityKey { get; set; }
    public string? StyleKey { get; set; }
    public Dictionary<string, string?> TeamInstructions { get; set; } = new();
}

public sealed class TaktiklerDto
{
    public string TeamName { get; set; } = "";
    public List<TaktikSlotDto> Slots { get; set; } = new();
    public AktifTaktikDto? Active { get; set; }
}

// ---- set_training ----

public sealed class OyuncuIstegi
{
    public int PlayerId { get; set; }
    public long? Focus { get; set; }
    public long? Intensity { get; set; }
}

public sealed class SeansIstegi
{
    public string Date { get; set; } = "";
    public int Slot { get; set; }
    public int Session { get; set; }
}

public sealed class AntrenmanIstegi
{
    public List<OyuncuIstegi> Players { get; set; } = new();
    public List<SeansIstegi> Sessions { get; set; } = new();
}

public sealed class SecenekDegisimiDto
{
    public SecenekDto? From { get; set; }
    public SecenekDto? To { get; set; }
    public bool? Done { get; set; }
}

public sealed class OyuncuDegisimiDto
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = "";
    public SecenekDegisimiDto? Focus { get; set; }
    public SecenekDegisimiDto? Intensity { get; set; }
}

public sealed class SeansDegisimiDto
{
    public string Date { get; set; } = "";
    public int Slot { get; set; }
    public SecenekDto? From { get; set; }
    public SecenekDto? To { get; set; }
    public bool? Done { get; set; }
}

public sealed class AntrenmanDegisimleriDto
{
    public bool Applied { get; set; }
    public List<OyuncuDegisimiDto> Players { get; set; } = new();
    public List<SeansDegisimiDto> Sessions { get; set; } = new();
    public List<string>? Notes { get; set; }
}
