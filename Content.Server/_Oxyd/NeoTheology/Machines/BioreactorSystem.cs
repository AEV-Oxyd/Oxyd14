using Content.Shared._Oxyd.NeoTheology.Components;
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
    [Dependency] private readonly SharedStackSystem _stack = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BioreactorComponent>();

        while (query.MoveNext(out var uid, out var reactor))
        {
            // Eris only processes the platforms while the chamber is shut and unbreached.
            if (!reactor.ChamberClosed || reactor.ChamberBreached)
                continue;

            var coords = Transform(uid).Coordinates;

            foreach (var crop in _lookup.GetEntitiesInRange<ProduceComponent>(coords, reactor.ProcessingRadius))
            {
                var pile = Spawn(BiomatterProto, coords);
                _stack.SetCount(pile, reactor.BiomatterPerEntity);
                QueueDel(crop);
            }
        }
    }

    /// <summary>
    /// Fills or empties the chamber. Only a shut, unbreached chamber can be pumped.
    /// </summary>
    public bool TryPumpSolution(EntityUid uid, BioreactorComponent? reactor = null)
    {
        if (!Resolve(uid, ref reactor))
            return false;

        if (!reactor.ChamberClosed || reactor.ChamberBreached)
            return false;

        reactor.ChamberSolution = !reactor.ChamberSolution;
        Dirty(uid, reactor);
        return true;
    }

    /// <summary>
    /// Opens or shuts the platform door. Refuses while the door is still jammed or the chamber
    /// still holds solution.
    /// </summary>
    public bool TryToggleChamber(EntityUid uid, BioreactorComponent? reactor = null)
    {
        if (!Resolve(uid, ref reactor))
            return false;

        ScanBreach(uid, reactor);

        if (reactor.ChamberBreached || reactor.ChamberSolution)
            return false;

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

        var jammed = false;

        foreach (var ent in _lookup.GetEntitiesInRange(Transform(uid).Coordinates, reactor.ProcessingRadius))
        {
            if (ent == uid)
                continue;

            // Bolted-down structures are what jam the door; loose crops get processed instead.
            if (!HasComp<ProduceComponent>(ent) && TryComp<TransformComponent>(ent, out var xform) && xform.Anchored)
            {
                jammed = true;
                break;
            }
        }

        reactor.ChamberBreached = jammed;
        Dirty(uid, reactor);
        return jammed;
    }
}
