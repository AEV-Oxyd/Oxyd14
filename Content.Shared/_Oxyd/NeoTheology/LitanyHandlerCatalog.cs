using System.Collections.Frozen;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Tracks which litany effects currently have a server commit handler versus which
/// foundation effects are catalogued but not yet implemented. An enabled, ungated
/// chant must appear in exactly one of these sets.
/// </summary>
public static class LitanyHandlerCatalog
{
    public const int ExpectedFoundationEffectCount = 23;

    /// <summary>Effects with an actual commit handler. Empty until LitanySystem lands.</summary>
    public static readonly FrozenSet<LitanyEffectKind> Implemented = FrozenSet<LitanyEffectKind>.Empty;

    /// <summary>
    /// Foundation effects that are allowed to be enabled in the catalog before their
    /// handler exists. Move an entry to <see cref="Implemented"/> when the handler is added.
    /// </summary>
    public static readonly FrozenSet<LitanyEffectKind> PendingFoundation = new HashSet<LitanyEffectKind>
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
        return Implemented.Contains(effect) || PendingFoundation.Contains(effect);
    }

    public static bool HasExactlyOneRegistration(LitanyEffectKind effect)
    {
        return Implemented.Contains(effect) != PendingFoundation.Contains(effect);
    }
}
