namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Shared holiness arithmetic. Regeneration uses elapsed simulation time and the
/// configured base rate, not a hardcoded value from the source implementation.
/// </summary>
public static class NeoTheologyHoliness
{
    public static int CognitionSteps(int effectiveCog)
    {
        return Math.Max(0, (int)Math.Floor(Math.Max(effectiveCog, 0) / 4d + 0.5d));
    }

    public static double RegenerationPerSecond(
        int effectiveCog,
        float righteousLife,
        bool channeling,
        int eligibleDisciples,
        double basePerMinute,
        double profileMultiplier)
    {
        if (!double.IsFinite(basePerMinute) || basePerMinute < 0 ||
            !double.IsFinite(profileMultiplier) || profileMultiplier < 0)
            return 0d;

        var cognitionSteps = CognitionSteps(effectiveCog);
        var righteousFactor = 1.5d * Math.Clamp(righteousLife, 0f, 100f) / 100d;
        var channelingFactor = channeling ? Math.Max(0, eligibleDisciples) / 5d : 0d;
        var perSecond = (basePerMinute / 60d) * profileMultiplier
                        * (1d + 0.05d * cognitionSteps + righteousFactor + channelingFactor);
        return double.IsFinite(perSecond) && perSecond >= 0 ? perSecond : 0d;
    }

    public static double Normalize(double value, double debitTolerance)
    {
        if (!double.IsFinite(value) || !double.IsFinite(debitTolerance) || debitTolerance < 0 ||
            Math.Abs(value) <= debitTolerance)
            return 0d;
        return value;
    }

    public static bool CanAfford(double current, double cost, double debitTolerance)
    {
        if (!double.IsFinite(cost) || cost < 0 || !double.IsFinite(current) ||
            !double.IsFinite(debitTolerance) || debitTolerance < 0)
            return false;
        return current + debitTolerance >= cost;
    }

    public static double ClampResource(double current, double maximum)
    {
        if (!double.IsFinite(current) || !double.IsFinite(maximum) || maximum < 0)
            return 0d;
        return Math.Clamp(current, 0d, maximum);
    }
}
