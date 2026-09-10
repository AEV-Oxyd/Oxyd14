using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Item;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Locates ritual items resting on or beside a NeoTheology altar. The altar is a place
/// marker — Eris litanies scan its turf for offerings and upgrades rather than using a
/// container, so this exposes the same lookup to the Phase 4 litany handlers.
/// </summary>
public sealed partial class AltarSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

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
}
