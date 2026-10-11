using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Components;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:173-211</c> (Atonement) and <c>rituals/inquisitor.dm:33-65</c>
/// (Penance): <c>adjustHalLoss(50)</c> adds temporary pain without wound or stamina damage.
/// </summary>
public sealed partial class LitanyPainEffect : LitanyEffect<LitanyPainEffect>
{
    /// <summary>Each resolved follower takes the pain rider.</summary>
    public override bool PerTarget => true;

    public LitanyPainEffect()
    {
        Conditions =
        [
            new LitanyPainAllowedCondition { FailureId = "oxyd-litany-godblood" },
        ];
    }

    [DataField]
    public float Amount = 50f;

    /// <summary>Atonement refuses Godblood. Penance does not.</summary>
    [DataField]
    public bool RefuseGodblood;
}

/// <summary>Raises the pain bridge on each resolved follower.</summary>
public sealed partial class LitanyPainEntityEffectSystem
    : EntityEffectSystem<MobStateComponent, LitanyPainEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<MobStateComponent> entity, ref EntityEffectEvent<LitanyPainEffect> args)
    {
        var pain = new LitanyPainEvent(entity, args.Effect.Amount, false);
        _litany.RaiseOn(entity, ref pain);
        _litany.ReportApplyResult(args.User, pain.Handled);
    }
}
