using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:13-42</c> (resurrection): the cloner starts a soul-safe job for
/// the soul in the reader. The server checks the saved profile, mind, machines, and biomass before payment.
/// The effect raises <see cref="LitanyResurrectionEvent"/> on the cloner for validation and execution.
/// </summary>
public sealed partial class LitanyResurrectionEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (!Execute(system, context, true))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
        => Execute(system, context, false);

    private bool Execute(LitanyEffectSystem system, LitanyEffectContext context, bool validateOnly)
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

            var start = new LitanyResurrectionEvent(target, reader, false, validateOnly);
            system.RaiseOn(target, ref start);
            if (start.Handled)
                return true;
        }

        return false;
    }
}
