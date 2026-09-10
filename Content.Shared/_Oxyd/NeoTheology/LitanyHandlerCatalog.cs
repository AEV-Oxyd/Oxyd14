using System.Collections.Frozen;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Separates the planned foundation catalog from effects that have a concrete
/// server commit handler. Catalog membership never grants runtime authority.
/// </summary>
public static class LitanyHandlerCatalog
{
    public const int ExpectedFoundationEffectCount = 23;

    /// <summary>Effects with an actual commit handler. Packet B+C: Relief, SoulHunger, Entreaty, CruciformSense; P4.3: ActivateDoor; P4.5: the medical four; P4.6: the short boosts; P4.4: Revelation, Epiphany, DivineBlessing; P4.2: Commitment, Deprivation.</summary>
    public static readonly FrozenSet<LitanyEffectKind> Implemented = new HashSet<LitanyEffectKind>
    {
        LitanyEffectKind.Relief,
        LitanyEffectKind.SoulHunger,
        LitanyEffectKind.Entreaty,
        LitanyEffectKind.CruciformSense,
        LitanyEffectKind.ActivateDoor,
        LitanyEffectKind.HandOfMercy,
        LitanyEffectKind.AbsolutionOfWounds,
        LitanyEffectKind.Convalescence,
        LitanyEffectKind.Succour,
        LitanyEffectKind.GraceOfPerseverance,
        LitanyEffectKind.UpholdHolyWord,
        LitanyEffectKind.Revelation,
        LitanyEffectKind.Epiphany,
        LitanyEffectKind.DivineBlessing,
        LitanyEffectKind.Commitment,
        LitanyEffectKind.Deprivation,
    }.ToFrozenSet();

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
