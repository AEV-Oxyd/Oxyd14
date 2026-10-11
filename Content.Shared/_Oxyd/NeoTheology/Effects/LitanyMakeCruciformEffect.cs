using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:43-75</c> (cruciformforge): the forge starts a produce run for
/// the materials it has already banked. The material check and the run are server-side, so the
/// effect raises <see cref="LitanyForgeProduceEvent"/> on the forge; the server
/// <c>CruciformForgeSystem</c> calls its own <c>TryProduce</c>.
/// </summary>
public sealed partial class LitanyMakeCruciformEffect : LitanyEffect<LitanyMakeCruciformEffect>
{
    public LitanyMakeCruciformEffect()
    {
        Conditions =
        [
            new LitanyForgeReadyCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Raised once on the caster: the first forge that accepts starts its produce run.</summary>
public sealed partial class LitanyMakeCruciformEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyMakeCruciformEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyMakeCruciformEffect> args)
    {
        var handled = false;
        if (_litany.GetCastContext(args.User) is { } context)
        {
            foreach (var target in context.Targets)
            {
                if (!_litany.IsLitanyForge(target))
                    continue;

                var produce = new LitanyForgeProduceEvent(target, false, false);
                _litany.RaiseOn(target, ref produce);
                if (produce.Handled)
                {
                    handled = true;
                    break;
                }
            }
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
