using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Database;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/custodian.dm:7-40</c> (Words of Purging): eases the target's addiction.
/// Advances dependence recovery by 15 without removing blood reagents. Also grants 15 pain relief.
/// </summary>
public sealed partial class LitanyWordsOfPurgingEffect : LitanyEffect<LitanyWordsOfPurgingEffect>
{
    public LitanyWordsOfPurgingEffect()
    {
        Conditions =
        [
            new LitanyHasTargetsCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>
/// Raised once on the caster: Eris refuses the whole rite when ANY resolved target rejects
/// holy influence — checked before any purge runs so a rejected cast mutates nothing.
/// </summary>
public sealed partial class LitanyWordsOfPurgingEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyWordsOfPurgingEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyWordsOfPurgingEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } context)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        foreach (var target in context.Targets)
        {
            if (_litany.RejectsHolyInfluence(target))
            {
                _litany.ReportApplyResult(args.User, false);
                return;
            }
        }

        var handled = false;
        foreach (var target in context.Targets)
        {
            var purge = new LitanyPurgeAddictionEvent(target, false);
            _litany.RaiseOn(target, ref purge);
            if (purge.Handled && args.User is { } user)
                _litany.AdminLog(LogType.Action, LogImpact.Medium, user,
                    $"purged addictions via {context.Litany.ID} on", target);
            handled |= purge.Handled;
        }

        _litany.ReportApplyResult(args.User, handled);
    }
}
