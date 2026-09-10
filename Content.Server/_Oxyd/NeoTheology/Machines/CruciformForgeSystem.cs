using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.6: the cruciform forge. Banks material stacks handed to it and, once the recipe is
/// stocked, spends <see cref="CruciformForgeComponent.WorkTime"/> to forge a cruciform.
/// </summary>
public sealed class CruciformForgeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CruciformForgeComponent>();
        while (query.MoveNext(out var uid, out var forge))
        {
            if (!forge.Working || forge.StartedAt is not { } started)
                continue;

            if (now - started < forge.WorkTime)
                continue;

            forge.Working = false;
            forge.StartedAt = null;
            forge.Ready = true;
            Spawn(forge.Product, Transform(uid).Coordinates);
            Dirty(uid, forge);
        }
    }

    /// <summary>
    /// Banks <paramref name="item"/> in the forge. One item counts as one sheet of the material it
    /// is made of; a part-used stack keeps its remainder.
    /// </summary>
    public bool TryInsert(EntityUid uid, EntityUid item, CruciformForgeComponent? forge = null)
    {
        if (!Resolve(uid, ref forge))
            return false;

        if (!TryComp<PhysicalCompositionComponent>(item, out var composition) ||
            composition.MaterialComposition.Count == 0)
        {
            return false;
        }

        var material = composition.MaterialComposition.Keys.First();
        var available = TryComp<StackComponent>(item, out var stack) ? _stack.GetCount((item, stack)) : 1;

        // ponytail: banked amounts count sheets, the unit the recipe is written in.
        var space = forge.StorageCapacity - forge.Stored.GetValueOrDefault(material);
        var take = Math.Min(space, available);
        if (take <= 0)
            return false;

        if (stack != null)
            _stack.TryUse((item, stack), take);
        else
            QueueDel(item);

        forge.Stored[material] = forge.Stored.GetValueOrDefault(material) + take;
        Dirty(uid, forge);
        return true;
    }

    /// <summary>
    /// Spends the recipe and starts a work run. Refuses, spending nothing, if any material is short.
    /// </summary>
    public bool TryProduce(EntityUid uid, CruciformForgeComponent? forge = null)
    {
        if (!Resolve(uid, ref forge) || forge.Working)
            return false;

        foreach (var (material, amount) in forge.Needed)
        {
            if (forge.Stored.GetValueOrDefault(material) < amount)
                return false;
        }

        foreach (var (material, amount) in forge.Needed)
            forge.Stored[material] -= amount;

        forge.Working = true;
        forge.StartedAt = _timing.CurTime;
        forge.Ready = false;
        Dirty(uid, forge);
        return true;
    }

    /// <summary>
    /// Hands the forged cruciform to <paramref name="user"/>. The product is a bare implant entity,
    /// so this only lands against a user who can hold it.
    /// </summary>
    public bool TryTakeProduct(EntityUid uid, EntityUid user, CruciformForgeComponent? forge = null)
    {
        if (!Resolve(uid, ref forge) || !forge.Ready)
            return false;

        foreach (var product in _lookup.GetEntitiesInRange<CruciformComponent>(Transform(uid).Coordinates, 1f))
        {
            if (!_hands.TryForcePickupAnyHand(user, product, checkActionBlocker: false))
                continue;

            forge.Ready = false;
            Dirty(uid, forge);
            return true;
        }

        return false;
    }
}
