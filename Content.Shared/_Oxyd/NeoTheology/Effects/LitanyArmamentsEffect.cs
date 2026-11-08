using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Events;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:492-520</c> (Order armaments): the priest opens the authorised
/// armament shop from the Eye of the Protector. The fork's shop UI lives on the separate
/// <c>OxydNtArmamentsPrinter</c> (P2.16), so the effect opens that printer's existing BUI through
/// <see cref="LitanyOpenArmamentsEvent"/>; the point bank stays the printer's own concern.
/// </summary>
public sealed partial class LitanyArmamentsEffect : LitanyEffect<LitanyArmamentsEffect>
{
    public LitanyArmamentsEffect()
    {
        Conditions =
        [
            // The validate probe runs the same eye check the apply path does.
            new LitanyArmamentsEyeCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>
/// Cast-scoped: opens the armaments printer through the first Eye of the Protector among
/// the resolved machine targets.
/// </summary>
public sealed partial class LitanyArmamentsEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyArmamentsEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyArmamentsEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } ctx)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        // Eris opens the Eye's armory. A forge or printer on the faced tile is not enough.
        foreach (var target in ctx.Targets)
        {
            if (!_litany.IsLitanyEye(target))
                continue;

            var open = new LitanyOpenArmamentsEvent(entity.Owner, false);
            _litany.RaiseOn(target, ref open);
            if (open.Handled)
            {
                _litany.ReportApplyResult(args.User, true);
                return;
            }
        }

        _litany.ReportApplyResult(args.User, false);
    }
}
