using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/base.dm:258-310</c> (reincarnation): a reunion of a stored soul with a
/// prepared body. The fork reduces it to the transaction its soul store owns — the living
/// target's cruciform is (re)bound to their soul by <c>CoreModuleBehaviorSystem.WriteSnapshot</c>,
/// the same write Eris runs when a cruciform is activated.
/// Deferred vs the source: Eris also requires the cruciform to be installed, once-activated and
/// currently inactive, then moves the stored mind into the prepared body (<c>transfer_soul</c>).
/// The fork's mind move belongs to Resurrection (reader + cloner); this litany only refreshes
/// the snapshot so the stored soul matches the body it will be reunited with.
/// </summary>
public sealed partial class LitanyReincarnationEffect : LitanyEffect
{
    /// <summary>Eris reads the soul out of the cloning module; without it there is nothing to write.</summary>
    private static readonly ProtoId<CoreModulePrototype> CloningModule = "OxydNtModuleCloning";

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 ||
            !system.TryGetInstalledCruciformEntity(context.Targets[0], out _, out var cruciform))
        {
            failure = "oxyd-litany-no-cruciform";
            return false;
        }

        if (!cruciform.InstalledModules.Contains(CloningModule))
        {
            failure = "oxyd-litany-soul-lost";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var write = new LitanyWriteSoulSnapshotEvent(context.Targets[0], false);
        system.RaiseOn(context.Targets[0], ref write);
        return write.Handled;
    }
}
