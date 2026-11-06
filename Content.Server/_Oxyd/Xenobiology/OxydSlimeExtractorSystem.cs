using Content.Shared._Oxyd.Xenobiology;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;

namespace Content.Server._Oxyd.Xenobiology;

/// <summary>
/// Eris xenobiology extract tooling (code/modules/xenobio/): the extractor
/// draws specimen cores out of refinable biological mobs (slimes in the
/// fork). Kept generic over ToolRefinableComponent rather than slime-gated —
/// any refinable specimen yields a core.
/// </summary>
public sealed partial class OxydSlimeExtractorSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    [SubscribeLocalEvent]
    private void OnSlimeExtract(Entity<OxydSlimeExtractorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;
        if (!HasComp<MobStateComponent>(args.Target.Value) || !HasComp<ToolRefinableComponent>(args.Target.Value))
            return;
        args.Handled = true;
        if (ent.Comp.Uses <= 0)
        {
            _popup.PopupEntity("The extractor is empty.", ent, args.User);
            return;
        }
        ent.Comp.Uses--;
        Spawn(ent.Comp.CoreProto, Transform(args.Target.Value).Coordinates);
        _popup.PopupEntity($"You draw a specimen core from {Name(args.Target.Value)}.", ent, args.User);
        if (ent.Comp.Uses <= 0)
            QueueDel(ent);
    }
}
