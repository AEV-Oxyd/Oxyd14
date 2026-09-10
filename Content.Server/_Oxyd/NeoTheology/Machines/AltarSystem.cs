using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Locates ritual items resting on or beside a NeoTheology altar. The altar is a place
/// marker — Eris litanies scan its turf for offerings and upgrades rather than using a
/// container, so this exposes the same lookup to the Phase 4 litany handlers.
/// </summary>
public sealed partial class AltarSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly EyeOfTheProtectorSystem _eye = default!;

    /// <summary>Finds items resting on or beside a NeoTheology altar.</summary>
    public IEnumerable<EntityUid> ItemsOnAltar(EntityUid altar)
    {
        if (TryComp<NeoTheologyAltarComponent>(altar, out var comp))
            foreach (var (item, _) in _lookup.GetEntitiesInRange<ItemComponent>(Transform(altar).Coordinates, comp.Radius))
                yield return item;
    }

    /// <summary>Finds the first item on the altar carrying <typeparamref name="T"/>.</summary>
    public bool TryFindItemOnAltar<T>(EntityUid altar, out EntityUid item) where T : IComponent
    {
        foreach (var candidate in ItemsOnAltar(altar))
        {
            if (TryComp<T>(candidate, out _))
            {
                item = candidate;
                return true;
            }
        }

        item = EntityUid.Invalid;
        return false;
    }

    /// <summary>
    /// P3.8: Eris <c>make_offering()</c>. Collects the named offering's requirements from the
    /// altar's turf and, only when every requirement is met, consumes them and banks the
    /// observation on <paramref name="eye"/>. A partial match consumes nothing.
    /// </summary>
    public bool TryMakeOffering(EntityUid altar, EntityUid eye, string offeringKey, out int accepted)
    {
        accepted = 0;

        if (!ProtoMan.TryIndex<OfferingPrototype>(offeringKey, out var offering))
            return false;

        // Available count per item on the altar; a non-stack item counts as one.
        var available = new Dictionary<EntityUid, int>();
        foreach (var item in ItemsOnAltar(altar))
            available[item] = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;

        // Plan the consumption without mutating anything, so a short requirement refuses cleanly.
        var plan = new List<(EntityUid Item, int Amount)>();
        foreach (var req in offering.Required)
        {
            var remaining = req.Count;
            foreach (var (item, count) in available)
            {
                if (count <= 0 || !MatchesProto(item, req.Proto))
                    continue;

                var take = Math.Min(count, remaining);
                plan.Add((item, take));
                available[item] = count - take;
                remaining -= take;
                if (remaining == 0)
                    break;
            }

            if (remaining > 0)
                return false; // under-stocked: consume nothing
        }

        foreach (var (item, amount) in plan)
        {
            if (TryComp<StackComponent>(item, out var stack))
                _stack.SetCount(item, stack.Count - amount, stack);
            else
                QueueDel(item);

            accepted += amount;
        }

        _eye.AddObservation(eye, offering.Observation);
        return true;
    }

    /// <summary>Whether <paramref name="item"/>'s prototype is <paramref name="wanted"/> or a descendant.</summary>
    private bool MatchesProto(EntityUid item, EntProtoId wanted)
    {
        if (MetaData(item).EntityPrototype is not { } proto)
            return false;

        foreach (var parent in ProtoMan.EnumerateParents<EntityPrototype>(proto, includeSelf: true))
        {
            if (parent.ID == wanted.Id)
                return true;
        }

        return false;
    }
}
