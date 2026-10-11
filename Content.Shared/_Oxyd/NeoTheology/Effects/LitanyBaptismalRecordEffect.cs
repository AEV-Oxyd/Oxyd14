using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:213-230</c> (Baptismal Record): the priest requests the parish
/// record from the altar in front, and a paper listing the active cruciform bearers slides out.
/// The altar lookup and the paper are server-side, so the effect raises
/// <see cref="LitanyBaptismalRecordEvent"/> on the altar and the server <c>AltarSystem</c>
/// writes the record.
/// </summary>
public sealed partial class LitanyBaptismalRecordEffect : LitanyEffect<LitanyBaptismalRecordEffect>
{
    public LitanyBaptismalRecordEffect()
    {
        Conditions =
        [
            new LitanyIsAltarCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>
/// Cast-scoped: raises the record event on the first NeoTheology altar among the resolved
/// machine targets.
/// </summary>
public sealed partial class LitanyBaptismalRecordEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyBaptismalRecordEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyBaptismalRecordEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } ctx)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        foreach (var target in ctx.Targets)
        {
            if (!_litany.IsLitanyAltar(target))
                continue;

            var record = new LitanyBaptismalRecordEvent(target, false);
            _litany.RaiseOn(target, ref record);
            if (record.Handled)
            {
                _litany.ReportApplyResult(args.User, true);
                return;
            }
        }

        _litany.ReportApplyResult(args.User, false);
    }
}
