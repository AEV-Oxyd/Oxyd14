using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Flattened Eris bioreactor. Three chamber booleans drive everything: the pump fills or empties
/// the closed chamber, the platform door only opens on an unbreached, unsolved chamber, and crops
/// left on the platforms are processed into biomatter.
/// </summary>
/// <remarks>
/// Eris' platform/pump/console part graph is flattened into one machine. Split into parts only if
/// construction gameplay needs it.
/// </remarks>
public sealed partial class BioreactorSystem : EntitySystem
{
    private static readonly EntProtoId BiomatterProto = "OxydNtBiomatter";

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly NeoTheologyMachineSystem _machines = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BioreactorComponent, LitanyPumpBioreactorEvent>(OnLitanyPumpBioreactor);
        SubscribeLocalEvent<BioreactorComponent, LitanyToggleBioreactorChamberEvent>(OnLitanyToggleBioreactorChamber);
    }

    /// <summary>
    /// BioreactorSolution bridge (Eris <c>rituals/machinery.dm:200-213</c>): the litany pumps the
    /// chamber in or out; the shut/unbreached gate is <see cref="TryPumpSolution"/>'s.
    /// </summary>
    private void OnLitanyPumpBioreactor(Entity<BioreactorComponent> ent, ref LitanyPumpBioreactorEvent args)
    {
        args.Handled = args.ValidateOnly ? CanPumpSolution(ent.Owner, ent.Comp) : TryPumpSolution(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// BioreactorChamber bridge (Eris <c>rituals/machinery.dm:219-236</c>): the litany opens or
    /// shuts the platform door; the breach re-scan and the solution gate are
    /// <see cref="TryToggleChamber"/>'s.
    /// </summary>
    private void OnLitanyToggleBioreactorChamber(Entity<BioreactorComponent> ent, ref LitanyToggleBioreactorChamberEvent args)
    {
        args.Handled = args.ValidateOnly ? CanToggleChamber(ent.Owner, ent.Comp) : TryToggleChamber(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BioreactorComponent>();

        while (query.MoveNext(out var uid, out var reactor))
        {
            if (!_machines.IsOperational(uid) || !reactor.ChamberClosed ||
                reactor.ChamberBreached || !reactor.ChamberSolution)
                continue;

            var coords = Transform(uid).Coordinates;

            foreach (var crop in _lookup.GetEntitiesInRange<ProduceComponent>(coords, reactor.ProcessingRadius))
            {
                if (TerminatingOrDeleted(crop) || EntityManager.IsQueuedForDeletion(crop))
                    continue;

                var pile = Spawn(BiomatterProto, coords);
                _stack.SetCount(pile, reactor.BiomatterPerEntity);
                QueueDel(crop);
            }
        }
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
        foreach (var ent in _lookup.GetEntitiesInRange(Transform(uid).Coordinates, reactor.ProcessingRadius))
        {
            if (ent == uid)
                continue;

            // Bolted-down structures are what jam the door; loose crops get processed instead.
            if (!HasComp<ProduceComponent>(ent) && TryComp<TransformComponent>(ent, out var xform) && xform.Anchored)
            {
                return true;
            }
        }

        return false;
    }
}
