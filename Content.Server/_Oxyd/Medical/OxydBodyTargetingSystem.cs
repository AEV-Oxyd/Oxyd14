using Content.Shared._Oxyd.Medical;
using Content.Shared.Body;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Eris zone_sel port: every mob carries a targeted body zone — one of the ten
/// external organ categories, mirroring Eris's BP_* hit zones. Self verbs
/// switch the zone; <see cref="OxydWoundSystem"/> attributes incoming damage
/// to the attacker's zone instead of picking an organ at random.
/// </summary>
public sealed partial class OxydBodyTargetingSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Stable verb order mapping Eris's BP_* zones onto organ categories.</summary>
    private static readonly ProtoId<OrganCategoryPrototype>[] Zones =
    {
        "Head", "Torso", "ArmLeft", "ArmRight", "HandLeft", "HandRight",
        "LegLeft", "LegRight", "FootLeft", "FootRight",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateComponent, MapInitEvent>(OnMobMapInit);
    }

    private void OnMobMapInit(EntityUid uid, MobStateComponent comp, MapInitEvent args)
    {
        EnsureComp<OxydBodyTargetingComponent>(uid);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<OxydBodyTargetingComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        // Eris zone_sel is self-only: you pick the zone your own attacks land on.
        if (args.User != ent.Owner || !args.CanAccess || !args.CanInteract)
            return;

        foreach (var zone in Zones)
        {
            var z = zone;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("oxyd-medical-target-zone", ("zone", z)),
                Act = () => SetZone(ent, z),
            });
        }
    }

    private void SetZone(Entity<OxydBodyTargetingComponent> ent, ProtoId<OrganCategoryPrototype> zone)
    {
        ent.Comp.TargetedZone = zone;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("oxyd-medical-target-zone-set", ("zone", zone)),
            ent.Owner, ent.Owner);
    }
}
