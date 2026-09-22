namespace NinjagoScanner.Web.Models;

public sealed class RankDefinition
{
    public required int Level { get; init; }
    public required string Name { get; init; }
    public required string Glyph { get; init; }
    public required int MinXp { get; init; }
    public required string AccentColor { get; init; }
}

/// <summary>The 10-rank table from design.md ("Ranks"), a one-year goal curve for an active collector.</summary>
public static class RankDefinitions
{
    public static readonly IReadOnlyList<RankDefinition> All = new[]
    {
        new RankDefinition { Level = 1, Name = "Novize", Glyph = "初", MinXp = 0, AccentColor = "oklch(70% 0.02 290)" },
        new RankDefinition { Level = 2, Name = "Schüler", Glyph = "学", MinXp = 150, AccentColor = "oklch(72% 0.05 250)" },
        new RankDefinition { Level = 3, Name = "Spinjitzu-Schüler", Glyph = "旋", MinXp = 400, AccentColor = "oklch(74% 0.08 230)" },
        new RankDefinition { Level = 4, Name = "Funke", Glyph = "火", MinXp = 800, AccentColor = "oklch(68% 0.18 40)" },
        new RankDefinition { Level = 5, Name = "Sturm", Glyph = "雷", MinXp = 1500, AccentColor = "oklch(82% 0.16 95)" },
        new RankDefinition { Level = 6, Name = "Frost", Glyph = "氷", MinXp = 2600, AccentColor = "oklch(80% 0.10 220)" },
        new RankDefinition { Level = 7, Name = "Stein", Glyph = "土", MinXp = 4200, AccentColor = "oklch(65% 0.12 140)" },
        new RankDefinition { Level = 8, Name = "Energie", Glyph = "気", MinXp = 6500, AccentColor = "oklch(70% 0.19 300)" },
        new RankDefinition { Level = 9, Name = "Sensei", Glyph = "師", MinXp = 9800, AccentColor = "oklch(76% 0.13 330)" },
        new RankDefinition { Level = 10, Name = "Goldener Ninja", Glyph = "金", MinXp = 14000, AccentColor = "oklch(78% 0.15 85)" },
    };

    /// <summary>The highest rank whose threshold the given XP total has reached.</summary>
    public static RankDefinition ForXp(int xp)
    {
        return All.LastOrDefault(rank => xp >= rank.MinXp) ?? All[0];
    }

    public static RankDefinition? Next(RankDefinition current)
    {
        return All.FirstOrDefault(rank => rank.Level == current.Level + 1);
    }
}

public sealed class BackgroundUnlock
{
    public required int RequiredRankLevel { get; init; }
    public required string Name { get; init; }
    public string? ImageFile { get; init; }
}

/// <summary>Rank-gated background unlocks (design.md "Background unlocks"). Rank 10's art doesn't exist yet - null ImageFile renders a gradient placeholder.</summary>
public static class BackgroundUnlocks
{
    public static readonly IReadOnlyList<BackgroundUnlock> All = new[]
    {
        new BackgroundUnlock { RequiredRankLevel = 4, Name = "Elementarnebel", ImageFile = "elemental-mist.png" },
        new BackgroundUnlock { RequiredRankLevel = 6, Name = "Nebelgipfel", ImageFile = "misty-peaks.png" },
        new BackgroundUnlock { RequiredRankLevel = 8, Name = "Energie-Dojo", ImageFile = "circuit-dojo.png" },
        new BackgroundUnlock { RequiredRankLevel = 10, Name = "Goldener Tresor", ImageFile = null },
    };
}
