using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Effects;

/// <summary>
/// Eris <c>rituals/inquisitor.dm:259-289</c> (Initiation): the ascension kit's convert module is
/// activated and the target is promoted to Preacher. Eris has the kit item install the module and
/// the ritual only activate it; the fork has no coreimplant_upgrade item path, so the server
/// handler performs both stages. The rank swap itself is server-only, reached through
/// <see cref="LitanyInitiationEvent"/>.
/// </summary>
public sealed partial class LitanyInitiationEffect : LitanyEffect
{
    private static readonly ProtoId<CoreModulePrototype> PriestRankModule = "OxydNtModulePriest";
    private static readonly ProtoId<CoreModulePrototype> InquisitorRankModule = "OxydNtModuleInquisitor";

    public override bool CanApply(
        LitanyEffectSystem system,
        LitanyEffectContext context,
        out LocId? failure)
    {
        if (context.Targets.Count == 0 ||
            !system.TryGetActiveCruciform(context.Targets[0], out var cruciform))
        {
            failure = "oxyd-litany-no-cruciform";
            return false;
        }

        // Eris: "The target is already a preacher."
        if (cruciform.InstalledModules.Contains(PriestRankModule) ||
            cruciform.InstalledModules.Contains(InquisitorRankModule))
        {
            failure = "oxyd-litany-initiation-already-preacher";
            return false;
        }

        failure = null;
        return true;
    }

    public override bool Apply(LitanyEffectSystem system, LitanyEffectContext context)
    {
        if (context.Targets.Count == 0)
            return false;

        var target = context.Targets[0];
        var ev = new LitanyInitiationEvent(context.User, false);
        system.RaiseOn(target, ref ev);
        return ev.Handled;
    }
}
