using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Eris holy-door litanies. Repairing a door consumes the biomatter lying on the caster's
/// tile or on the tile they face.
/// </summary>
public sealed partial class NeoTheologyDoorSystem : EntitySystem
{
    [Dependency] private CruciformSystem _cruciform = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedStackSystem _stack = default!;

    /// <summary>Eris <c>REPAIR_DOOR_AMOUNT</c>: biomatter needed to repair one holy door.</summary>
    public const int RepairCost = 10;

    /// <summary>How far off a scanned tile a biomatter stack still counts as lying on it.</summary>
    private const float ScanRadius = 0.6f;

    private static readonly ProtoId<StackPrototype> BiomatterStack = NeoTheologyPrototypes.BiomatterStack;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NeoTheologyDoorComponent, DamageDealtEvent>(OnDamaged);
        SubscribeLocalEvent<NeoTheologyDoorComponent, BeforeDoorOpenedEvent>(OnBeforeOpened);
    }

    private void OnBeforeOpened(EntityUid uid, NeoTheologyDoorComponent door, BeforeDoorOpenedEvent args)
    {
        if (door.MinimumClearance == NeoTheologyClearance.None)
            return;

        if (args.User is not { } user)
        {
            args.Cancel();
            return;
        }

        if (HoldsTauCross(user))
            return;

        if (_cruciform.TryGetCruciform(user, out _, out var cruciform) &&
            cruciform.Active &&
            cruciform.Clearance >= door.MinimumClearance)
            return;

        args.Cancel();
    }

    private bool HoldsTauCross(EntityUid user)
    {
        if (!TryComp<HandsComponent>(user, out var hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((user, hands)))
        {
            if (HasComp<TauCrossComponent>(held))
                return true;
        }

        return false;
    }

    private void OnDamaged(EntityUid uid, NeoTheologyDoorComponent component, DamageDealtEvent args)
    {
        if (!_damageable.IsAtLeastTotalDamage(uid, NeoTheologyDoorComponent.BrokenAt))
            return;

        component.Broken = true;
    }

    /// <summary>
    /// RepairDoor bridge (Eris <c>rituals/machinery.dm:100-145</c>): the litany names the door and
    /// its caster. Validation checks damage and biomatter before the cast spends power.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyRepairDoor(Entity<NeoTheologyDoorComponent> ent, ref LitanyRepairDoorEvent args)
    {
        args.Handled = TryRepair(ent.Owner, args.User, RepairCost, args.ValidateOnly);
    }

    /// <summary>
    /// Eris <c>repair_door</c>: burns <paramref name="amount"/> biomatter to fully heal a
    /// damaged holy door.
    /// </summary>
    /// <remarks>
    /// The litany's extraDelay is the Eris 5-second repair wait. This method heals,
    /// clears the broken flag, unbolts, and closes.
    /// </remarks>
    public bool TryRepair(EntityUid door, EntityUid user, int amount, bool validateOnly = false)
    {
        if (!TryComp<DamageableComponent>(door, out var damageable) ||
            !TryComp<NeoTheologyDoorComponent>(door, out _))
            return false;

        if (!_damageable.TryGetDamageGreaterThan((door, damageable), FixedPoint2.Zero, out _))
            return false;

        if (!TryConsumeBiomatter(user, amount, validateOnly))
            return false;

        if (!validateOnly)
        {
            _damageable.ClearAllDamage(door);
            if (TryComp<NeoTheologyDoorComponent>(door, out var litanyDoor))
            {
                litanyDoor.Broken = false;
                litanyDoor.LitanyLocked = false;
            }

            if (TryComp<DoorBoltComponent>(door, out var bolt) && _doors.IsBolted(door, bolt))
                _doors.TrySetBoltDown((door, bolt), false, user);

            if (TryComp<DoorComponent>(door, out var doorComp) && doorComp.State != DoorState.Closed)
                _doors.TryClose(door, doorComp);
        }

        return true;
    }

    /// <summary>
    /// Eats <paramref name="amount"/> biomatter from the caster's tile or the tile they face.
    /// </summary>
    public bool TryConsumeBiomatter(EntityUid user, int amount, bool validateOnly = false)
    {
        var xform = Transform(user);
        var inFront = xform.Coordinates.Offset(xform.LocalRotation.ToVec());

        foreach (var coords in new[] { xform.Coordinates, inFront })
        {
            foreach (var (item, stack) in _lookup.GetEntitiesInRange<StackComponent>(coords, ScanRadius))
            {
                if (stack.StackTypeId != BiomatterStack || stack.Count < amount)
                    continue;

                if (validateOnly || _stack.TryUse((item, (StackComponent?) stack), amount))
                    return true;
            }
        }

        return false;
    }
}
