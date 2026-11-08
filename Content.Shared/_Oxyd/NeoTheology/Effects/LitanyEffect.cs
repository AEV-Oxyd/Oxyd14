using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Content.Shared._Oxyd.NeoTheology.Components;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Marker interface every litany effect implements, so prototype/code lookups
/// (<c>Effects.OfType&lt;ILitanyEffect&gt;()</c>) work across the generic self-types.
/// </summary>
public interface ILitanyEffect
{
    /// <summary>
    /// True for the dead-only flows (Deprivation's extraction; resurrection later). Target
    /// resolution excludes Dead mobs by default (§7.1); these effects need them delivered.
    /// </summary>
    bool AllowsDeadTarget { get; }

    /// <summary>
    /// When true the cast transaction raises the effect once per resolved target through
    /// <c>SharedEntityEffectsSystem</c>; when false it raises it once on the caster and the
    /// handler re-derives its world targets from <see cref="LitanyCastContextComponent"/>.
    /// </summary>
    bool PerTarget { get; }
}

/// <summary>
/// One declarative litany effect — real <see cref="EntityEffectBase{T}"/> data plus a matching
/// <c>EntityEffectSystem&lt;TComp, T&gt;</c> handler. The cast transaction supplies cast inputs
/// (selected text, designation, blueprint, ceremony participants, resolved targets) through a
/// <see cref="LitanyCastContextComponent"/> on the caster instead of a system field, so the
/// shared pipeline stays re-entrant.
/// </summary>
/// <typeparam name="T">The concrete effect type; the shared raiser needs the self-type.</typeparam>
[ImplicitDataDefinitionForInheritors]
public abstract partial class LitanyEffect<T> : EntityEffectBase<T>, ILitanyEffect where T : LitanyEffect<T>
{
    /// <inheritdoc/>
    public virtual bool AllowsDeadTarget => false;

    /// <inheritdoc/>
    public virtual bool PerTarget => false;
}

/// <summary>
/// Non-generic view of the ceremony flags, so <c>LitanySystem.Ceremony</c> can find them with
/// <c>Effects.OfType&lt;ILitanyCeremonyEffect&gt;()</c> regardless of each effect's self-type.
/// </summary>
public interface ILitanyCeremonyEffect
{
    /// <summary>
    /// Eris <c>/datum/ritual/group/cruciform/high_ritual</c>: a priest or inquisitor must start
    /// the rite. Sanctify overrides this because Eris marks it <c>high_ritual = FALSE</c>.
    /// </summary>
    bool RequiresClergy { get; }

    /// <summary>Eris <c>GLOB.miracle_points--</c> and <c>eotp.addObservation(25)</c> in the stat rite.</summary>
    bool ConsumesMiraclePoint { get; }
}

/// <summary>
/// Base for the group-ritual payloads (Eris <c>rituals/group.dm</c>). Ceremony effects run at
/// the end of the phrase round, once for the starter and every recorded participant.
/// </summary>
public abstract partial class LitanyCeremonyEffect<T> : LitanyEffect<T>, ILitanyCeremonyEffect
    where T : LitanyCeremonyEffect<T>
{
    /// <inheritdoc/>
    public virtual bool RequiresClergy => true;

    /// <inheritdoc/>
    public virtual bool ConsumesMiraclePoint => false;
}
