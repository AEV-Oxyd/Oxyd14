using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Components;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Eris Reincarnation: transfer the saved soul into a matching, inactive implanted body.</summary>
public sealed partial class LitanyReincarnationEffect : LitanyEffect<LitanyReincarnationEffect>
{
    // The rite revives the dead: corpses must reach the soul-transfer check.
    public override bool AllowsDeadTarget => true;

    /// <summary>Each resolved body whose stored soul validates gets the transfer.</summary>
    public override bool PerTarget => true;

    public LitanyReincarnationEffect()
    {
        Conditions =
        [
            new LitanyReincarnatableCondition { FailureId = "oxyd-litany-soul-lost" },
        ];
    }
}

/// <summary>Raises the soul-transfer bridge on each resolved body.</summary>
public sealed partial class LitanyReincarnationEntityEffectSystem
    : EntityEffectSystem<MobStateComponent, LitanyReincarnationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<MobStateComponent> entity, ref EntityEffectEvent<LitanyReincarnationEffect> args)
    {
        var transfer = new LitanyReincarnationEvent(entity, false);
        _litany.RaiseOn(entity, ref transfer);
        if (transfer.Handled && args.User is { } user)
            _litany.AdminLog(LogType.Mind, LogImpact.High, user,
                $"reincarnated via {_litany.GetCastContext(user)?.Litany.ID} into", entity);
        _litany.ReportApplyResult(args.User, transfer.Handled);
    }
}
