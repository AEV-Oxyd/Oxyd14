using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
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
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;

    /// <summary>Eris <c>REPAIR_DOOR_AMOUNT</c>: biomatter needed to repair one holy door.</summary>
    public const int RepairCost = 10;

    /// <summary>How far off a scanned tile a biomatter stack still counts as lying on it.</summary>
    private const float ScanRadius = 0.6f;

    private static readonly ProtoId<StackPrototype> BiomatterStack = "Biomatter";

    /// <summary>
    /// Eris <c>repair_door</c>: burns <paramref name="amount"/> biomatter to fully heal a
    /// damaged holy door.
    /// </summary>
    /// <remarks>
    /// ponytail: the Eris 5-second construction do-after is dropped; the caller
    /// (litany or interaction) owns the do-after. Add a NeoTheology overlay only if
    /// players miss the feedback.
    /// </remarks>
    public bool TryRepair(EntityUid door, EntityUid user, int amount)
    {
        if (!TryComp<DamageableComponent>(door, out var damageable) ||
            !TryComp<NeoTheologyDoorComponent>(door, out _))
            return false;

        if (!_damageable.TryGetDamageGreaterThan((door, damageable), FixedPoint2.Zero, out _))
            return false;

        if (!TryConsumeBiomatter(user, amount))
            return false;

        _damageable.ClearAllDamage(door);
        return true;
    }

    /// <summary>
    /// Eats <paramref name="amount"/> biomatter from the caster's tile or the tile they face.
    /// </summary>
    public bool TryConsumeBiomatter(EntityUid user, int amount)
    {
        var xform = Transform(user);
        var inFront = xform.Coordinates.Offset(xform.WorldRotation.ToVec());

        foreach (var coords in new[] { xform.Coordinates, inFront })
        {
            foreach (var (item, stack) in _lookup.GetEntitiesInRange<StackComponent>(coords, ScanRadius))
            {
                if (stack.StackTypeId != BiomatterStack || stack.Count < amount)
                    continue;

                if (_stack.TryUse((item, (StackComponent?) stack), amount))
                    return true;
            }
        }

        return false;
    }
}
