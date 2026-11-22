using System.Linq;
using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Call to Battle (Eris <c>rituals/crusader.dm:24-51</c>): the caster gains skill points equal to
/// <see cref="PointsPerBearer"/> per counted bearer (the caster included) scaled by each skill's
/// ratio, for the litany's duration. Eris counts every human with an installed implant; the fork
/// counts active bearers through the shared follower scan, narrowed by <see cref="BearerWhitelist"/>.
/// </summary>
public sealed partial class LitanyCallToBattleEffect : LitanyEffect<LitanyCallToBattleEffect>
{
    /// <summary>Eris <c>count += 2</c> per bearer, the caster included.</summary>
    [DataField]
    public float PointsPerBearer = 2f;

    /// <summary>Eris counts every bearer in view; the fork scans this radius.</summary>
    [DataField]
    public float Radius = 7f;

    /// <summary>
    /// Which entities count as bearers on top of the shared follower scan (e.g. living humans
    /// bearing a cruciform component). Null accepts every scanned bearer.
    /// </summary>
    [DataField]
    public EntityWhitelist? BearerWhitelist;

    /// <summary>
    /// Skill → share of the bearer points granted. Call to Battle maps to
    /// Tgh/Rob at 1.0 (full points) and Vig at 0.5 (half).
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<SkillPrototype>, float> SkillRatios = new()
    {
        [NeoTheologySkills.Toughness] = 1f,
        [NeoTheologySkills.Robustness] = 1f,
        [NeoTheologySkills.Vigilance] = 0.5f,
    };

    public LitanyCallToBattleEffect()
    {
        Conditions =
        [
            // The caster must be a living mob that can carry skill buffs.
            new LitanySkillBuffableCondition { FailureId = "oxyd-litany-no-target" },
        ];
    }
}

/// <summary>Cast-scoped: buffs the caster by the counted visible bearers.</summary>
public sealed partial class LitanyCallToBattleEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyCallToBattleEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyCallToBattleEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } ctx)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        var effect = args.Effect;

        // The caster bears an active cruciform to cast at all, so they count first.
        var count = effect.PointsPerBearer;
        foreach (var bearer in _litany.CollectVisibleActiveFollowers(entity.Owner, effect.Radius))
        {
            if (effect.BearerWhitelist != null && !_litany.IsWhitelisted(effect.BearerWhitelist, bearer))
                continue;

            count += effect.PointsPerBearer;
        }

        var amounts = effect.SkillRatios.ToDictionary(kv => kv.Key, kv => (int)(count * kv.Value));
        _litany.ReportApplyResult(args.User,
            _litany.TryApplySkillBuff(entity.Owner, ctx.Litany.ID, amounts, ctx.Litany.EffectDuration));
    }
}
