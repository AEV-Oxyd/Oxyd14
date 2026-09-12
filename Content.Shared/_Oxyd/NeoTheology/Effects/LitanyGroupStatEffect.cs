using Content.Shared._Oxyd.Skills;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// The six stat rites (Eris <c>rituals/group.dm:44-200</c>): every starter and participant gains
/// the mapped skill. Eris formula: <c>buff_value + cnt * aditional_value</c> = 3 + 2 per
/// participant, plus one extra per participant when the target carries the Channeling perk.
/// Eris floors the payload at three participants and still completes the rite below it.
/// </summary>
public sealed partial class LitanyGroupStatEffect : LitanyCeremonyEffect
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

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        // Begin resolves no candidates: the followers join while the rite runs.
        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.CeremonyParticipants < MinimumParticipants)
        {
            foreach (var target in context.Targets)
                system.DeliverSocialNotice(target, Loc.GetString("oxyd-litany-ceremony-too-few"));

            // Eris completes the rite and only skips the payload.
            return true;
        }

        foreach (var target in context.Targets)
        {
            var channeling = system.TryGetInstalledCruciform(target, out var cruciform) && cruciform.Channeling;
            var amount = BaseAmount + PerParticipant * context.CeremonyParticipants;
            if (channeling)
                amount += context.CeremonyParticipants;

            var amounts = new Dictionary<ProtoId<SkillPrototype>, int> { [Skill] = amount };
            system.TryApplySkillBuff(target, context.Litany.ID, amounts, context.Litany.EffectDuration);
            system.DeliverSocialNotice(target, Loc.GetString(Message));
        }

        return true;
    }
}
