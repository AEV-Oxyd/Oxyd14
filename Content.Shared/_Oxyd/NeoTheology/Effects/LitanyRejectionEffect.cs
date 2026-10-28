using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:64-90</c> (Rejection): the caster sheds robotic organs and foreign implants.
/// The server handles <see cref="LitanyRejectForeignBodyEvent"/> through native body and container APIs.
/// It preserves the detached entities, natural organs, and cruciform.
/// </summary>
public sealed partial class LitanyRejectionEffect : LitanyEffect<LitanyRejectionEffect>
{
    /// <summary>Each resolved body sheds its foreign matter.</summary>
    public override bool PerTarget => true;
}

/// <summary>Raises the rejection bridge on each resolved body.</summary>
public sealed partial class LitanyRejectionEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyRejectionEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyRejectionEffect> args)
    {
        // Eris succeeds even when there is nothing to reject; the handler still runs.
        var shed = new LitanyRejectForeignBodyEvent(entity, false);
        _litany.RaiseOn(entity, ref shed);
        if (shed.Handled && args.User is { } user)
            _litany.AdminLog(LogType.Action, LogImpact.High, user,
                $"shed foreign bodies via {_litany.GetCastContext(user)?.Litany.ID} on", entity);
        _litany.ReportApplyResult(args.User, shed.Handled);
    }
}
