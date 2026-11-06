using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/machinery.dm:151-168</c> (power_biogen_awake): the biogenerator is switched on
/// or off from its screen. The switch is server-side, so the effect raises
/// <see cref="LitanyToggleBiogeneratorEvent"/> on the machine; the server
/// <c>BiogeneratorSystem</c> flips its working state.
/// </summary>
public sealed partial class LitanyPowerBiogeneratorEffect : LitanyEffect<LitanyPowerBiogeneratorEffect>
{
    public LitanyPowerBiogeneratorEffect()
    {
        Conditions =
        [
            new LitanyBiogeneratorPresentCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Raised once on the caster: the first biogenerator in the resolved targets toggles.</summary>
public sealed partial class LitanyPowerBiogeneratorEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyPowerBiogeneratorEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyPowerBiogeneratorEffect> args)
    {
        var handled = false;
        if (_litany.GetCastContext(args.User) is { } context)
        {
            foreach (var target in context.Targets)
            {
                if (!_litany.IsLitanyBiogenerator(target))
                    continue;

                var toggle = new LitanyToggleBiogeneratorEvent(target, false);
                _litany.RaiseOn(target, ref toggle);
                if (toggle.Handled)
                {
                    handled = true;
                    break;
                }
            }
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
