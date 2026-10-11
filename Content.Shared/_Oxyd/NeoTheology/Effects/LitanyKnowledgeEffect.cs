using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:291-309</c> (Knowledge): the inquisitor reads how many
/// telecrystals remain in the hidden uplink. The server <c>NtUplinkSystem</c> owns the store
/// and sends the count, or "no uplink" when the module is absent.
/// </summary>
public sealed partial class LitanyKnowledgeEffect : LitanyEffect<LitanyKnowledgeEffect>
{
    public LitanyKnowledgeEffect()
    {
        Conditions =
        [
            new LitanyUplinkReadyCondition { FailureId = "oxyd-litany-uplink-none" },
        ];
    }
}

/// <summary>Raised once on the caster: sends the uplink's telecrystal count.</summary>
public sealed partial class LitanyKnowledgeEntityEffectSystem
    : EntityEffectSystem<TransformComponent, LitanyKnowledgeEffect>
{
    [Dependency] private LitanyEffectSystem _litany = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<LitanyKnowledgeEffect> args)
    {
        var report = new LitanyUplinkReportEvent(entity, false, false);
        _litany.RaiseOn(entity, ref report);
        _litany.ReportApplyResult(args.User, report.Handled);
    }
}
