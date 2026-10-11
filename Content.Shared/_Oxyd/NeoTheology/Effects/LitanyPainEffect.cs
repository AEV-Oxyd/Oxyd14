using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:173-211</c> (Atonement) and <c>rituals/inquisitor.dm:33-65</c>
/// (Penance): <c>adjustHalLoss(50)</c> adds temporary pain without wound or stamina damage.
/// </summary>
public sealed partial class LitanyPainEffect : LitanyEffect<LitanyPainEffect>
{
    public LitanyPainEffect()
    {
        Conditions =
        [
            new LitanyHasTargetsCondition { FailureId = "oxyd-litany-no-target" },
            new LitanyPainAllowedCondition { FailureId = "oxyd-litany-godblood" },
        ];
    }

    [DataField]
    public float Amount = 50f;

    /// <summary>Atonement refuses Godblood. Penance does not.</summary>
    [DataField]
    public bool RefuseGodblood;
}

/// <summary>Raised once on the caster: the first resolved follower takes the pain rider.</summary>
public sealed partial class LitanyPainEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyPainEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyPainEffect> args)
    {
        // The cast layer has already narrowed a named rite to one follower.
        var handled = false;
        if (_litany.GetCastContext(args.User)?.Targets.FirstOrDefault() is { } target)
        {
            var pain = new LitanyPainEvent(target, args.Effect.Amount, false);
            _litany.RaiseOn(target, ref pain);
            handled = pain.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
