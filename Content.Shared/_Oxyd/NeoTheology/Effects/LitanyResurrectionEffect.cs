using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:13-42</c> (resurrection): the cloner starts a soul-safe job for
/// the soul the reader holds. The reader lookup, the corpse and the pod's biomass are all
/// server-side, so the effect validates that both machines are among the litany's targets and
/// raises <see cref="LitanyResurrectionEvent"/> on the cloner; the server
/// <c>CruciformReaderSystem</c> does the work.
/// </summary>
public sealed partial class LitanyResurrectionEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!context.Targets.Any(system.IsLitanyCloner) ||
            !context.Targets.Any(system.IsLitanyReader))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var reader = EntityUid.Invalid;
        foreach (var target in context.Targets)
        {
            if (!system.IsLitanyReader(target))
                continue;

            reader = target;
            break;
        }

        if (reader == EntityUid.Invalid)
            return false;

        foreach (var target in context.Targets)
        {
            if (!system.IsLitanyCloner(target))
                continue;

            var start = new LitanyResurrectionEvent(target, reader, false);
            system.RaiseOn(target, ref start);
            if (start.Handled)
                return true;
        }

        return false;
    }
}
