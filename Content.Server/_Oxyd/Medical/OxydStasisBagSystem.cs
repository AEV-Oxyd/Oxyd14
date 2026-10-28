using System.Linq;
using Content.Shared._Oxyd.Medical;
using Content.Shared.Body;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports the Eris stasis bag (items/bodybag stasis variant): a one-use bag that halts all
/// incoming damage and wound decay while a patient is sealed inside.
/// </summary>
public sealed partial class OxydStasisBagSystem : EntitySystem
{
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydStasisBagComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<OxydStasisBagComponent, GetVerbsEvent<InteractionVerb>>(AddSealVerb);
        SubscribeLocalEvent<OxydStasisBagComponent, GetVerbsEvent<AlternativeVerb>>(AddOpenVerb);
        SubscribeLocalEvent<OxydStasisBagComponent, DragDropTargetEvent>(OnDragDropOn);
        SubscribeLocalEvent<OxydStasisBagComponent, CanDropTargetEvent>(OnCanDropOn);
        SubscribeLocalEvent<OxydStasisBagComponent, EntRemovedFromContainerMessage>(OnOccupantRemoved);
        SubscribeLocalEvent<OxydStasisBagComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<OxydInStasisComponent, DamageModifyEvent>(OnStasisDamage);
    }

    private void OnInit(EntityUid uid, OxydStasisBagComponent comp, ComponentInit args)
    {
        _container.EnsureContainer<Container>(uid, OxydStasisBagComponent.BodyContainerId);
    }

    private Container BodySlot(EntityUid uid) =>
        _container.EnsureContainer<Container>(uid, OxydStasisBagComponent.BodyContainerId);

    private void OnCanDropOn(EntityUid uid, OxydStasisBagComponent comp, ref CanDropTargetEvent args)
    {
        if (args.Handled)
            return;
        // Only claim the drop for a body we can accept so other drop handlers can still run.
        args.CanDrop = BodySlot(uid).ContainedEntities.Count == 0 && HasComp<BodyComponent>(args.Dragged);
        args.Handled = args.CanDrop;
    }

    private void OnDragDropOn(EntityUid uid, OxydStasisBagComponent comp, DragDropTargetEvent args)
    {
        if (args.Handled || BodySlot(uid).ContainedEntities.Count > 0 || !HasComp<BodyComponent>(args.Dragged))
            return;

        args.Handled = true;
        Seal(uid, comp, args.Dragged);
    }

    private void AddSealVerb(EntityUid uid, OxydStasisBagComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || BodySlot(uid).ContainedEntities.Count > 0)
            return;

        // Seal the body the user is pulling (args.Target is the bag itself).
        if (!TryComp<PullerComponent>(args.User, out var puller) || puller.Pulling is not { } target ||
            !HasComp<BodyComponent>(target))
            return;
        args.Verbs.Add(new InteractionVerb
        {
            Act = () => Seal(uid, comp, target),
            Text = Loc.GetString("oxyd-medical-stasis-seal"),
        });
    }

    private void AddOpenVerb(EntityUid uid, OxydStasisBagComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (BodySlot(uid).ContainedEntities.Count == 0)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => Open(uid, comp),
            Text = Loc.GetString("oxyd-medical-stasis-open"),
        });
    }

    private void Seal(EntityUid uid, OxydStasisBagComponent comp, EntityUid patient)
    {
        _container.Insert(patient, BodySlot(uid));
        EnsureComp<OxydInStasisComponent>(patient);
        _popup.PopupEntity(Loc.GetString("oxyd-medical-stasis-sealed"), uid, uid);
    }

    private void Open(EntityUid uid, OxydStasisBagComponent comp)
    {
        foreach (var occupant in BodySlot(uid).ContainedEntities.ToArray())
        {
            _container.Remove(occupant, BodySlot(uid));
            RemComp<OxydInStasisComponent>(occupant);
        }

        // Eris stasis bags are non-reusable.
        if (comp.OneUse)
        {
            _popup.PopupEntity(Loc.GetString("oxyd-medical-stasis-discarded"), uid, uid);
            QueueDel(uid);
        }
    }

    /// <summary>Stasis negates incoming damage (Eris stasis_bag prevents body decay/damage).</summary>
    private void OnStasisDamage(EntityUid uid, OxydInStasisComponent comp, DamageModifyEvent args)
    {
        args.Damage = new DamageSpecifier();
    }

    /// <summary>Any occupant leaving the body container loses stasis protection, even when
    /// removed by something other than the Open verb (admin pull, teleport, destroy).</summary>
    private void OnOccupantRemoved(EntityUid uid, OxydStasisBagComponent comp, EntRemovedFromContainerMessage args)
    {
        RemCompDeferred<OxydInStasisComponent>(args.Entity);
    }

    /// <summary>A deleted bag frees its patient instead of leaving them permanently in stasis.</summary>
    private void OnTerminating(EntityUid uid, OxydStasisBagComponent comp, EntityTerminatingEvent args)
    {
        foreach (var occupant in BodySlot(uid).ContainedEntities.ToArray())
            RemComp<OxydInStasisComponent>(occupant);
    }
}
