using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:232-320</c> (the offering family): the priest faces the Eye of the
/// Protector and names an offering; the altar collects the required items and the Eye banks them.
/// Both halves are server-side, so the effect raises <see cref="LitanyOfferingEvent"/> on the Eye
/// and the server <c>AltarSystem</c> runs its own <c>TryMakeOffering</c>. One class serves
/// DivineIntervention and HolyGuidance — they differ only in their offering prototype.
/// </summary>
public sealed partial class LitanyOfferingEffect : LitanyEffect<LitanyOfferingEffect>
{
    /// <summary>The <c>oxydOffering</c> prototype this litany makes, e.g. <c>OxydNtOfferingDivineIntervention</c>.</summary>
    [DataField(required: true)]
    public string Offering = string.Empty;

    public LitanyOfferingEffect()
    {
        Conditions =
        [
            new LitanyOfferingReadyCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Raised once on the caster: the first Eye that accepts banks the offering.</summary>
public sealed partial class LitanyOfferingEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyOfferingEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyOfferingEffect> args)
    {
        var handled = false;
        if (args.User is { } user && _litany.GetCastContext(user) is { } context)
        {
            foreach (var target in context.Targets)
            {
                if (!_litany.IsLitanyEye(target))
                    continue;

                var offering = new LitanyOfferingEvent(user, args.Effect.Offering, false, false);
                _litany.RaiseOn(target, ref offering);
                if (offering.Handled)
                {
                    handled = true;
                    break;
                }
            }
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
