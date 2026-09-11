using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/custodian.dm:7-40</c> (Words of Purging): eases the target's addiction.
/// The fork has no addiction progression model, so the server handler purges the listed
/// habit-forming reagents from the bloodstream — the named divergence from the plan's
/// "new capability" row.
/// </summary>
public sealed partial class LitanyWordsOfPurgingEffect : LitanyEffect
{
    [DataField]
    public List<ProtoId<ReagentPrototype>> Reagents = new()
    {
        "Desoxyephedrine",
        "Ephedrine",
        "THC",
        "Nicotine",
        "SpaceDrugs",
    };

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        failure = null;
        return context.Targets.Count > 0;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        var handled = false;
        foreach (var target in context.Targets)
        {
            var purge = new LitanyPurgeAddictionEvent(target, Reagents, false);
            system.RaiseOn(target, ref purge);
            handled |= purge.Handled;
        }

        return handled;
    }
}
