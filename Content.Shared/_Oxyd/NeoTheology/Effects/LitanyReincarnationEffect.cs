using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>Eris Reincarnation: transfer the saved soul into a matching, inactive implanted body.</summary>
public sealed partial class LitanyReincarnationEffect : LitanyEffect<LitanyReincarnationEffect>
{
    // The rite revives the dead: corpses must reach the soul-transfer check.
    public override bool AllowsDeadTarget => true;

    public LitanyReincarnationEffect()
    {
        Conditions =
        [
            new LitanyReincarnatableCondition { FailureId = "oxyd-litany-soul-lost" },
        ];
    }
}

/// <summary>Raised once on the caster: transfers the soul into the first resolved body.</summary>
public sealed partial class LitanyReincarnationEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyReincarnationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyReincarnationEffect> args)
    {
        var handled = false;
        if (_litany.GetCastContext(args.User)?.Targets.FirstOrDefault() is { } target)
        {
            var transfer = new LitanyReincarnationEvent(target, false);
            _litany.RaiseOn(target, ref transfer);
            if (transfer.Handled && args.User is { } user)
                _litany.AdminLog(LogType.Mind, LogImpact.High, user,
                    $"reincarnated via {_litany.GetCastContext(user)?.Litany.ID} into", target);
            handled = transfer.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
