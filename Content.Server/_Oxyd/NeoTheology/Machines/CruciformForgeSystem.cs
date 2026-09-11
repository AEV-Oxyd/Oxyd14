using Content.Server.Materials;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.6: the cruciform forge. Materials handed to it are banked by
/// <see cref="SharedMaterialStorageSystem"/>'s own <c>InteractUsing</c> handler (the forge carries a
/// <see cref="MaterialStorageComponent"/>); once the recipe is stocked, it spends
/// <see cref="CruciformForgeComponent.WorkTime"/> to forge a cruciform.
/// </summary>
public sealed class CruciformForgeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CruciformForgeComponent, LitanyForgeProduceEvent>(OnLitanyForgeProduce);
    }

    /// <summary>
    /// MakeCruciform bridge (Eris <c>rituals/machinery.dm:43-75</c>): the litany asks the forge to
    /// start its own produce run; the recipe check and the spend are <see cref="TryProduce"/>'s.
    /// </summary>
    private void OnLitanyForgeProduce(Entity<CruciformForgeComponent> ent, ref LitanyForgeProduceEvent args)
    {
        args.Handled = TryProduce(ent.Owner, ent.Comp);
    }

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
    /// Spends the recipe and starts a work run. Refuses, spending nothing, if any material is short.
    /// </summary>
    public bool TryProduce(EntityUid uid, CruciformForgeComponent? forge = null)
    {
        if (!Resolve(uid, ref forge) || forge.Working)
            return false;

        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return false;

        foreach (var (material, amount) in forge.Needed)
        {
            if (_materialStorage.GetMaterialAmount(uid, material, storage) < amount)
                return false;
        }

        foreach (var (material, amount) in forge.Needed)
            _materialStorage.TryChangeMaterialAmount(uid, material, -amount, storage);

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
