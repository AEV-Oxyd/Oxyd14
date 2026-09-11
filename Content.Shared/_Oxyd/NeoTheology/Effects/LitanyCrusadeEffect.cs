using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Crusade (Eris <c>rituals/group.dm:220-245</c>): with at least six followers the rite teaches
/// the crusader litanies to every bearer who took part. Eris stores this in the implant's
/// <c>known_rituals</c>; the fork grants the <c>OxydLitanyCrusader</c> set instead. Eris also
/// flips the world crusade flag on faction items; the fork has no equivalent (named divergence).
/// </summary>
public sealed partial class LitanyCrusadeEffect : LitanyCeremonyEffect
{
    /// <summary>Eris <c>success()</c> floor for teaching the crusader litanies.</summary>
    public const int MinimumParticipants = 6;

    /// <summary>The set the rite reveals: Eternal Brotherhood, Call to Battle, Searing Revelation.</summary>
    public static readonly ProtoId<LitanySetPrototype> CrusaderSet = "OxydLitanyCrusader";

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        // Begin resolves no candidates: the followers join while the rite runs.
        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.CeremonyParticipants < MinimumParticipants)
            return true;

        var handled = false;
        foreach (var target in context.Targets)
        {
            var grant = new LitanyGrantLitanySetEvent(target, CrusaderSet, false);
            system.RaiseOn(target, ref grant);
            handled |= grant.Handled;
        }

        return handled;
    }
}
