using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:31-60</c> (baptism): the target must be a living body with an
/// installed, not-yet-active cruciform; the litany activates it. Activation is server
/// gameplay, so the effect raises <see cref="LitanyActivateCruciformEvent"/> on the target
/// and the server CruciformSystem does the work.
/// </summary>
public sealed partial class LitanyEpiphanyEffect : LitanyEffect
{
    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 || !system.IsAlive(context.Targets[0]))
        {
            failure = "oxyd-litany-no-target";
            return false;
        }

        if (!system.TryGetInstalledCruciform(context.Targets[0], out var cruciform))
        {
            failure = "oxyd-litany-no-cruciform";
            return false;
        }

        if (cruciform.Active)
        {
            failure = "oxyd-litany-already-active";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var target = context.Targets[0];
        var activate = new LitanyActivateCruciformEvent(target, false);
        system.RaiseOn(target, ref activate);
        return activate.Handled;
    }
}
