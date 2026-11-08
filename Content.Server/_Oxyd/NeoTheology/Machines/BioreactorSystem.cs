using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Stacks;
using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Flattened Eris bioreactor. Three chamber booleans drive everything: the pump fills or empties
/// the closed chamber, the platform door only opens on an unbreached, unsolved chamber, and whatever
/// is on the chamber's own tile — crops and dead bodies — is processed into biomatter.
/// </summary>
/// <remarks>
/// Eris' platform/pump/console part graph is flattened into one machine; the chamber is the
/// machine's own tile, so entity selection is a tile lookup, not a radius scan. Biomatter output
/// comes from the processed entity's own material contents (its
/// <see cref="PhysicalCompositionComponent"/>), not a per-entity constant.
/// </remarks>
public sealed partial class BioreactorSystem : EntitySystem
{
    private static readonly EntProtoId BiomatterProto = NeoTheologyPrototypes.BiomatterEnt;
    private static readonly ProtoId<MaterialPrototype> BiomatterMaterial = NeoTheologyPrototypes.BiomatterMaterial;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private NeoTheologyMachineSystem _machines = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>
    /// BioreactorSolution bridge (Eris <c>rituals/machinery.dm:200-213</c>): the litany pumps the
    /// chamber in or out; the shut/unbreached gate is <see cref="TryPumpSolution"/>'s.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyPumpBioreactor(Entity<BioreactorComponent> ent, ref LitanyPumpBioreactorEvent args)
    {
        args.Handled = args.ValidateOnly ? CanPumpSolution(ent.Owner, ent.Comp) : TryPumpSolution(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// BioreactorChamber bridge (Eris <c>rituals/machinery.dm:219-236</c>): the litany opens or
    /// shuts the platform door; the breach re-scan and the solution gate are
    /// <see cref="TryToggleChamber"/>'s.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyToggleBioreactorChamber(Entity<BioreactorComponent> ent, ref LitanyToggleBioreactorChamberEvent args)
    {
        args.Handled = args.ValidateOnly ? CanToggleChamber(ent.Owner, ent.Comp) : TryToggleChamber(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// The entities intersecting the reactor's own tile — the flattened chamber.
    /// </summary>
    private IEnumerable<EntityUid> ChamberContents(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            yield break;

        foreach (var ent in _lookup.GetLocalEntitiesIntersecting(
                     _map.GetTileRef(grid, gridComp, xform.Coordinates)))
            yield return ent;
    }

    /// <summary>Whether <paramref name="target"/> sits on the reactor's chamber tile.</summary>
    private bool InChamber(EntityUid uid, EntityUid target)
    {
        var reactorXform = Transform(uid);
        var targetXform = Transform(target);
        if (targetXform.MapID != reactorXform.MapID || targetXform.GridUid != reactorXform.GridUid)
            return false;

        return targetXform.Coordinates.Position.Floored() == reactorXform.Coordinates.Position.Floored();
    }

    public override void Update(float frameTime)
    {
        UpdateConsoles(frameTime);

        var query = EntityQueryEnumerator<BioreactorComponent>();

        while (query.MoveNext(out var uid, out var reactor))
        {
            if (!_machines.IsOperational(uid) || !reactor.ChamberClosed ||
                reactor.ChamberBreached || !reactor.ChamberSolution)
                continue;

            foreach (var ent in ChamberContents(uid))
            {
                if (ent == uid || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent))
                    continue;

                if (HasComp<BloodstreamComponent>(ent))
                    TryProcessBody(uid, ent, reactor);
                else if (TryComp<ProduceComponent>(ent, out _))
                    TryProcessProduce(uid, ent);
            }
        }
    }

    /// <summary>The entity's own biomatter content — zero for anything without one.</summary>
    private int BiomatterContent(EntityUid uid)
    {
        return TryComp<PhysicalCompositionComponent>(uid, out var composition)
            ? composition.MaterialComposition.GetValueOrDefault(BiomatterMaterial)
            : 0;
    }

    private void TryProcessProduce(EntityUid uid, EntityUid crop)
    {
        var amount = BiomatterContent(crop);
        QueueDel(crop);
        if (amount <= 0)
            return;

        var pile = Spawn(BiomatterProto, Transform(uid).Coordinates);
        _stack.SetCount((pile, null), amount);
    }

    public bool TryProcessBody(EntityUid uid, EntityUid body, BioreactorComponent? reactor = null)
    {
        if (!Resolve(uid, ref reactor) || !_machines.IsOperational(uid) || !reactor.ChamberClosed ||
            reactor.ChamberBreached || !reactor.ChamberSolution || TerminatingOrDeleted(body) ||
            EntityManager.IsQueuedForDeletion(body) || !HasComp<BloodstreamComponent>(body) ||
            !_mobState.IsDead(body) || !InChamber(uid, body))
            return false;
        // Queue before output: a second call cannot sell the same corpse twice.
        RemoveAllStorage(body, Transform(uid).Coordinates);
        // Organic matter has no PhysicalComposition yet; the corpse's mass is the content proxy.
        var amount = BiomatterContent(body) is var composed && composed > 0
            ? composed
            : TryComp<PhysicsComponent>(body, out var physics)
                ? Math.Max(1, (int) Math.Round(physics.FixturesMass))
                : 1;
        QueueDel(body);
        var pile = Spawn(BiomatterProto, Transform(uid).Coordinates);
        _stack.SetCount((pile, null), amount);
        return true;
    }

    /// <summary>Drops everything the body stores: held items, inventory slots, and implants.</summary>
    private void RemoveAllStorage(EntityUid body, EntityCoordinates destination)
    {
        _hands.DropAll(body, checkActionBlocker: false);
        if (TryComp<InventoryComponent>(body, out var inventory))
            foreach (var slot in inventory.Containers)
                _containers.EmptyContainer(slot, destination: destination);
        if (TryComp<ImplantedComponent>(body, out var implanted))
            foreach (var implant in implanted.ImplantContainer.ContainedEntities.ToArray())
                _containers.Remove(implant, implanted.ImplantContainer, force: true,
                    destination: destination);
    }

    /// <summary>
    /// Fills or empties the chamber. Only a shut, unbreached chamber can be pumped.
    /// </summary>
    public bool CanPumpSolution(EntityUid uid, BioreactorComponent? reactor = null)
    {
        return Resolve(uid, ref reactor) && _machines.IsOperational(uid) &&
               reactor.ChamberClosed && !reactor.ChamberBreached;
    }

    public bool TryPumpSolution(EntityUid uid, BioreactorComponent? reactor = null)
    {
        if (!Resolve(uid, ref reactor) || !CanPumpSolution(uid, reactor))
            return false;

        reactor.ChamberSolution = !reactor.ChamberSolution;
        Dirty(uid, reactor);
        return true;
    }

    /// <summary>
    /// Opens or shuts the platform door. Refuses while the door is still jammed or the chamber
    /// still holds solution.
    /// </summary>
    public bool CanToggleChamber(EntityUid uid, BioreactorComponent? reactor = null)
    {
        return Resolve(uid, ref reactor) && _machines.IsOperational(uid) &&
               !reactor.ChamberSolution && !IsBreached(uid, reactor);
    }

    public bool TryToggleChamber(EntityUid uid, BioreactorComponent? reactor = null)
    {
        if (!Resolve(uid, ref reactor) || !CanToggleChamber(uid, reactor))
            return false;

        ScanBreach(uid, reactor);

        reactor.ChamberClosed = !reactor.ChamberClosed;
        Dirty(uid, reactor);
        return true;
    }

    /// <summary>
    /// Re-reads whether anything still jams the platform door. Returns true while it does.
    /// </summary>
    public bool ScanBreach(EntityUid uid, BioreactorComponent? reactor = null)
    {
        if (!Resolve(uid, ref reactor))
            return false;

        reactor.ChamberBreached = IsBreached(uid, reactor);
        Dirty(uid, reactor);
        return reactor.ChamberBreached;
    }

    private bool IsBreached(EntityUid uid, BioreactorComponent reactor)
    {
        foreach (var ent in ChamberContents(uid))
        {
            if (ent == uid)
                continue;

            // Bolted-down structures are what jam the door; loose crops get processed instead.
            if (!HasComp<ProduceComponent>(ent) && Transform(ent).Anchored)
            {
                return true;
            }
        }

        return false;
    }
}
