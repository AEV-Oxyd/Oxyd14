using System.Collections.Frozen;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Separates the planned foundation catalog from effects that have a concrete
/// server commit handler. Catalog membership never grants runtime authority.
/// </summary>
public static class LitanyHandlerCatalog
{
    public const int ExpectedFoundationEffectCount = 23;

    /// <summary>Effects with an actual commit handler. Empty until LitanySystem lands.</summary>
    public static readonly FrozenSet<LitanyEffectKind> Implemented = FrozenSet<LitanyEffectKind>.Empty;

    /// <summary>
    /// Foundation effects planned for milestones 4 and 5. These entries may be
    /// displayed as unavailable reference material, but are not castable.
    /// </summary>
    public static readonly FrozenSet<LitanyEffectKind> Foundation = new HashSet<LitanyEffectKind>
    {
        LitanyEffectKind.Relief,
        LitanyEffectKind.SoulHunger,
        LitanyEffectKind.Entreaty,
        LitanyEffectKind.CruciformSense,
        LitanyEffectKind.Revelation,
        LitanyEffectKind.Commitment,
        LitanyEffectKind.Deprivation,
        LitanyEffectKind.HandOfMercy,
        LitanyEffectKind.AbsolutionOfWounds,
        LitanyEffectKind.Epiphany,
        LitanyEffectKind.GraceOfPerseverance,
        LitanyEffectKind.UpholdHolyWord,
        LitanyEffectKind.BaptismalRecord,
        LitanyEffectKind.DivineBlessing,
        LitanyEffectKind.Confirmation,
        LitanyEffectKind.Adoption,
        LitanyEffectKind.Ordination,
        LitanyEffectKind.Omission,
        LitanyEffectKind.Excommunication,
        LitanyEffectKind.Convalescence,
        LitanyEffectKind.Succour,
        LitanyEffectKind.Sending,
        LitanyEffectKind.ActivateDoor,
    }.ToFrozenSet();

    public static bool HasHandler(LitanyEffectKind effect)
    {
        return Implemented.Contains(effect);
    }

    public static bool AllowsEnabledCatalogEntry(LitanyEffectKind effect)
    {
        return Implemented.Contains(effect);
    }
}
