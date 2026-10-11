using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/priest.dm:361-393</c> (Confirmation: Disciple → Acolyte / Agrolyte /
/// Custodian). Eris pops an <c>alert()</c> offering the three designations and calls the
/// matching <c>make_*()</c>. The book UI now offers the same three profile choices;
/// the cast context's designation carries the pick. Manual-speech casts
/// have no picker and keep the historical Acolyte fallback. The rank swap is server-only,
/// reached through <see cref="LitanySetRankEvent"/>.
/// </summary>
public sealed partial class LitanyConfirmationEffect : LitanyEffect<LitanyConfirmationEffect>
{
    internal static readonly ProtoId<NeoTheologyProfilePrototype> AcolyteProfile = "OxydNtAcolyte";

    /// <summary>The pipeline delivers each resolved bearer; there is no pick-one fallback.</summary>
    public override bool PerTarget => true;

    public LitanyConfirmationEffect()
    {
        Conditions =
        [
            new LitanyActiveCruciformCondition { FailureId = "oxyd-litany-no-cruciform" },
        ];
    }
}

/// <summary>Swaps each resolved bearer's rank to the picked (or default) profile.</summary>
public sealed partial class LitanyConfirmationEntityEffectSystem
    : EntityEffectSystem<CruciformBearerComponent, LitanyConfirmationEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<CruciformBearerComponent> entity, ref EntityEffectEvent<LitanyConfirmationEffect> args)
    {
        var profile = _litany.GetCastContext(args.User)?.Designation ?? LitanyConfirmationEffect.AcolyteProfile;
        var rank = new LitanySetRankEvent(entity.Owner, profile, false);
        _litany.RaiseOn(entity.Owner, ref rank);
        if (rank.Handled && _litany.GetCastContext(args.User) is { } ctx)
        {
            _litany.AdminLog(LogType.Action, LogImpact.Medium, args.User ?? entity.Owner,
                $"confirmed via {ctx.Litany.ID} as {profile} on", entity.Owner);
        }

        _litany.ReportApplyResult(args.User, rank.Handled);
    }
}
