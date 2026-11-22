using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// The six stat rites (Eris <c>rituals/group.dm:44-200</c>): every starter and participant gains
/// the mapped skill. Eris formula: <c>buff_value + cnt * aditional_value</c> = 3 + 2 per
/// participant, plus one extra per participant when the target carries the Channeling perk.
/// Eris floors the payload at three participants and still completes the rite below it.
/// </summary>
public sealed partial class LitanyGroupStatEffect : LitanyCeremonyEffect<LitanyGroupStatEffect>
{
    /// <summary>Eris <c>buff_value</c>.</summary>
    public const int BaseAmount = 3;

    /// <summary>Eris <c>aditional_value</c>.</summary>
    public const int PerParticipant = 2;

    /// <summary>Eris <c>success()</c> refuses the buff below three participants.</summary>
    public const int MinimumParticipants = 3;

    /// <summary>Eris <c>stat_buff</c>: the 1:1 fork skill for the Eris stat.</summary>
    [DataField(required: true)]
    public ProtoId<SkillPrototype> Skill { get; private set; }

    /// <summary>Eris <c>stat_message</c>.</summary>
    [DataField(required: true)]
    public LocId Message { get; private set; } = string.Empty;

    public override bool ConsumesMiraclePoint => true;

    // Begin resolves no candidates: the followers join while the rite runs, so the effect
    // carries no target conditions at all.
}

/// <summary>
/// Cast-scoped ceremony payload: grants the rite's skill buff to every participant who does
/// not reject holy influence. Below the floor it narrates the too-few line and still completes.
/// </summary>
public sealed partial class LitanyGroupStatEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyGroupStatEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyGroupStatEffect> args)
    {
        if (_litany.GetCastContext(args.User) is not { } ctx)
        {
            _litany.ReportApplyResult(args.User, false);
            return;
        }

        if (ctx.CeremonyParticipants < LitanyGroupStatEffect.MinimumParticipants)
        {
            foreach (var target in ctx.Targets)
                _litany.DeliverSocialNotice(target, Loc.GetString("oxyd-litany-ceremony-too-few"));

            // Eris completes the rite and only skips the payload.
            _litany.ReportApplyResult(args.User, true);
            return;
        }

        foreach (var target in ctx.Targets)
        {
            var channeling = _litany.TryGetInstalledCruciform(target, out var cruciform) && cruciform.Channeling;
            var amount = LitanyGroupStatEffect.BaseAmount +
                         LitanyGroupStatEffect.PerParticipant * ctx.CeremonyParticipants;
            if (channeling)
                amount += ctx.CeremonyParticipants;

            if (_litany.RejectsHolyInfluence(target))
                continue;

            _litany.TryAddTimedSkill(target, ctx.Litany.ID, args.Effect.Skill, amount);
            _litany.DeliverSocialNotice(target, Loc.GetString(args.Effect.Message));
        }

        _litany.ReportApplyResult(args.User, true);
    }
}
