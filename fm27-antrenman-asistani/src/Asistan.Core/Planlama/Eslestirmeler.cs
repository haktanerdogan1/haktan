using Asistan.Core.Veri;

namespace Asistan.Core.Planlama;

/// <summary>
/// Rol → anahtar özellikler. Rol anahtarları oyunun İngilizce anahtarlarıdır (ör. "CentreForward"); aile kalıbıyla eşleşir.
/// Bu tablo bir başlangıç tasarımıdır, oyun motorunun ağırlıkları değildir.
/// </summary>
public static class RolOzellikleri
{
    private static readonly (string[] Kaliplar, string[] Ozellikler)[] Tablo =
    {
        (new[] { "Goalkeeper", "Keeper", "Sweeper" }, new[] { "Reflexes", "Handling", "OneOnOnes", "CommandOfArea", "Positioning", "Kicking", "Communication" }),
        (new[] { "WingBack", "FullBack", "Wingback", "Fullback" }, new[] { "Tackling", "Positioning", "Crossing", "Stamina", "Pace", "WorkRate", "Marking" }),
        (new[] { "CentreBack", "CentralDefender", "CenterBack", "Libero", "Stopper", "BallPlayingDefender", "CentreHalf", "Defender" },
            new[] { "Tackling", "Marking", "Heading", "Positioning", "JumpingReach", "Strength", "Concentration" }),
        (new[] { "DefensiveMidfielder", "Anchor", "HalfBack", "Regista", "Volante", "Screen", "DeepLying", "Holding" },
            new[] { "Tackling", "Positioning", "Passing", "Anticipation", "Teamwork", "WorkRate", "Decisions" }),
        (new[] { "AttackingMidfielder", "Enganche", "ShadowStriker", "Trequartista", "Number10", "Advanced Playmaker", "AdvancedPlaymaker" },
            new[] { "Passing", "Vision", "Technique", "FirstTouch", "Decisions", "Flair", "Movement" }),
        (new[] { "Winger", "InsideForward", "WideMidfielder", "Inverted", "Wide", "Raumdeuter", "Wingman" },
            new[] { "Dribbling", "Crossing", "Acceleration", "Pace", "Technique", "FirstTouch", "Movement" }),
        (new[] { "Forward", "Striker", "Poacher", "TargetMan", "Target", "FalseNine", "Pressing Forward" },
            new[] { "Finishing", "Movement", "Composure", "FirstTouch", "Anticipation", "Heading", "Strength" }),
        (new[] { "Midfielder", "Playmaker", "Mezzala", "BoxToBox", "Carrilero", "Roaming" },
            new[] { "Passing", "Vision", "Decisions", "FirstTouch", "Stamina", "WorkRate", "Teamwork" })
    };

    public static List<string> Anahtar(string? rolAnahtari)
    {
        if (string.IsNullOrWhiteSpace(rolAnahtari))
        {
            return new List<string>();
        }

        foreach (var (kaliplar, ozellikler) in Tablo)
        {
            if (kaliplar.Any(k => rolAnahtari.Contains(k.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)))
            {
                return ozellikler.ToList();
            }
        }

        return new List<string>();
    }
}

/// <summary>Ek odak seçeneklerinin adı (oyun dilinde) → çalıştırdığı özellik anahtarları.</summary>
public static class OdakOzellikleri
{
    private static readonly (string Ozellik, string Turkce, string[] Kelimeler)[] Tablo =
    {
        ("Passing", "Pas", new[] { "pas", "passing", "passe" }),
        ("Finishing", "Bitiricilik", new[] { "bitiricilik", "finishing", "finition" }),
        ("Crossing", "Orta", new[] { "orta", "ortalar", "crossing", "centres" }),
        ("Tackling", "Top kapma", new[] { "top kapma", "müdahale", "tackling", "tacle" }),
        ("Marking", "Markaj", new[] { "markaj", "marking", "marquage" }),
        ("Heading", "Kafa vuruşu", new[] { "kafa", "heading", "jeu de tête" }),
        ("Dribbling", "Top sürme", new[] { "dribbling", "top sürme", "dribble" }),
        ("FirstTouch", "İlk kontrol", new[] { "ilk kontrol", "first touch", "contrôle", "ball control", "top kontrolü" }),
        ("Technique", "Teknik", new[] { "teknik", "technique", "ball control", "top kontrolü" }),
        ("LongShots", "Uzaktan şut", new[] { "uzaktan şut", "long shots", "tirs de loin" }),
        ("FreeKicks", "Serbest vuruş", new[] { "serbest vuruş", "free kick", "coups francs" }),
        ("Corners", "Korner", new[] { "korner", "corner" }),
        ("PenaltyTaking", "Penaltı", new[] { "penaltı", "penalty", "penalties" }),
        ("Decisions", "Karar alma", new[] { "karar", "decisions", "décisions" }),
        ("Anticipation", "Önsezi", new[] { "önsezi", "anticipation" }),
        ("Composure", "Soğukkanlılık", new[] { "soğukkanlılık", "composure", "sang-froid" }),
        ("Concentration", "Konsantrasyon", new[] { "konsantrasyon", "concentration" }),
        ("Vision", "Vizyon", new[] { "vizyon", "vision" }),
        ("Movement", "Topsuz alan", new[] { "topsuz", "off the ball", "movement", "démarquage" }),
        ("Positioning", "Pozisyon alma", new[] { "pozisyon alma", "positioning", "placement", "defensive positioning" }),
        ("Teamwork", "Takım oyunu", new[] { "takım oyunu", "teamwork", "collectif" }),
        ("WorkRate", "Çalışkanlık", new[] { "çalışkanlık", "work rate", "volume de jeu" }),
        ("Strength", "Güç", new[] { "güç", "strength", "puissance", "kuvvet" }),
        ("Pace", "Hız", new[] { "hız", "pace", "vitesse", "quickness", "çabukluk" }),
        ("Acceleration", "Hızlanma", new[] { "hızlanma", "ivmelenme", "acceleration", "accélération", "quickness", "çabukluk" }),
        ("Agility", "Çeviklik", new[] { "çeviklik", "agility", "agilité", "quickness", "çabukluk" }),
        ("Balance", "Denge", new[] { "denge", "balance", "équilibre" }),
        ("Stamina", "Dayanıklılık", new[] { "dayanıklılık", "stamina", "endurance" }),
        ("JumpingReach", "Zıplama", new[] { "zıplama", "jumping", "détente" }),
        ("AerialReach", "Hava topu erişimi", new[] { "hava", "aerial", "jeu aérien" }),
        ("Handling", "Elle kontrol", new[] { "elle kontrol", "handling", "prise de balle" }),
        ("Reflexes", "Refleks", new[] { "refleks", "reflexes", "réflexes", "shot stopping" }),
        ("OneOnOnes", "Birebir", new[] { "birebir", "one on one", "un contre un" }),
        ("Kicking", "Degaj", new[] { "degaj", "kicking", "jeu au pied" }),
        ("Throwing", "Elle atış", new[] { "elle atış", "throwing" }),
        ("CommandOfArea", "Alan hakimiyeti", new[] { "alan hakimiyeti", "ceza sahası", "command of area", "autorité" }),
        ("Communication", "İletişim", new[] { "iletişim", "communication" }),
        ("RushingOut", "Kaleden çıkma", new[] { "kaleden çıkma", "rushing out", "sorties" })
    };

    public static List<string> Ozellikler(string odakAdi)
    {
        var ad = Seanslar.Kucuk(odakAdi);
        var sonuc = new List<string>();

        foreach (var (ozellik, _, kelimeler) in Tablo)
        {
            if (kelimeler.Any(k => KelimeIceriyor(ad, Seanslar.Kucuk(k))))
            {
                sonuc.Add(ozellik);
            }
        }

        return sonuc;
    }

    private static readonly Dictionary<string, string> EkAdlar = new()
    {
        ["Aggression"] = "Agresiflik", ["Bravery"] = "Cesaret", ["Flair"] = "Yaratıcılık", ["Determination"] = "Kararlılık",
        ["Leadership"] = "Liderlik", ["NaturalFitness"] = "Doğal kondisyon", ["LongThrows"] = "Uzun taç", ["Eccentricity"] = "Sıra dışılık",
        ["TendencyToPunch"] = "Yumruklama eğilimi"
    };

    public static string TurkceAd(string ozellik) =>
        Tablo.FirstOrDefault(t => t.Ozellik == ozellik).Turkce ?? (EkAdlar.TryGetValue(ozellik, out var ad) ? ad : ozellik);

    // "pas" "pasif" içinde, "orta" "ortalama" içinde eşleşmesin: kelime başı ve sonu kontrol edilir (çok kelimeli ifadeler serbest).
    private static bool KelimeIceriyor(string metin, string kelime)
    {
        var i = metin.IndexOf(kelime, StringComparison.Ordinal);

        while (i >= 0)
        {
            var once = i == 0 || !char.IsLetter(metin[i - 1]);
            var sonEk = i + kelime.Length;
            var sonra = sonEk >= metin.Length || !char.IsLetter(metin[sonEk]) || (kelime.Length >= 5 && kelime.Contains(' '));

            if (once && sonra)
            {
                return true;
            }

            i = metin.IndexOf(kelime, i + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
