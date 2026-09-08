namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Shared holiness arithmetic. Regeneration uses elapsed simulation time and the
/// source-derived 1 holiness/minute base, not a 20/minute misreading of DM Process.
/// </summary>
public static class NeoTheologyHoliness
{
    public const double DebitTolerance = 1e-6;
    public const double DefaultBasePerMinute = 1d;
    public const double DiscipleCapacity = 50d;
    public const double PreacherCapacity = 80d;
    public const double InquisitorCapacity = 100d;
    public const double PreacherRegenMultiplier = 1.15d;
    public const double InquisitorRegenMultiplier = 1.25d;

    public static int CognitionSteps(int effectiveCog)
    {
        return Math.Max(0, (int)Math.Floor(Math.Max(effectiveCog, 0) / 4d + 0.5d));
    }

    public static double RankCapacity(NeoTheologyRank rank)
    {
        return rank switch
        {
            NeoTheologyRank.Preacher => PreacherCapacity,
            NeoTheologyRank.Inquisitor => InquisitorCapacity,
            _ => DiscipleCapacity,
        };
    }

    public static double RankRegenMultiplier(NeoTheologyRank rank)
    {
        return rank switch
        {
            NeoTheologyRank.Preacher => PreacherRegenMultiplier,
            NeoTheologyRank.Inquisitor => InquisitorRegenMultiplier,
            _ => 1d,
        };
    }

    public static double RegenerationPerSecond(
        NeoTheologyRank rank,
        int effectiveCog,
        float righteousLife,
        bool channeling,
        int eligibleDisciples,
        double basePerMinute = DefaultBasePerMinute,
        double? preacherMultiplier = null,
        double? inquisitorMultiplier = null)
    {
        var cognitionSteps = CognitionSteps(effectiveCog);
        var righteousFactor = 1.5d * Math.Clamp(righteousLife, 0f, 100f) / 100d;
        var channelingFactor = channeling ? Math.Max(0, eligibleDisciples) / 5d : 0d;
        var rankMultiplier = rank switch
        {
            NeoTheologyRank.Preacher => preacherMultiplier ?? PreacherRegenMultiplier,
            NeoTheologyRank.Inquisitor => inquisitorMultiplier ?? InquisitorRegenMultiplier,
            _ => 1d,
        };
        var perSecond = (basePerMinute / 60d) * rankMultiplier
                        * (1d + 0.05d * cognitionSteps + righteousFactor + channelingFactor);
        return double.IsFinite(perSecond) && perSecond >= 0 ? perSecond : 0d;
    }

    public static double Normalize(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) <= DebitTolerance)
            return 0d;
        return value;
    }

    public static bool CanAfford(double current, double cost)
    {
        if (!double.IsFinite(cost) || cost < 0 || !double.IsFinite(current))
            return false;
        return current + DebitTolerance >= cost;
    }

    public static double ClampResource(double current, double maximum)
    {
        if (!double.IsFinite(current) || !double.IsFinite(maximum) || maximum < 0)
            return 0d;
        return Math.Clamp(current, 0d, maximum);
    }
}
