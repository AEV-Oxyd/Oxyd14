using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Implants.Components;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Component-only cruciform lookups usable from shared code (the litany effect
/// classes are shared because the client deserializes <see cref="LitanyPrototype"/>).
/// Gameplay logic — holiness, access, spending — stays in the server CruciformSystem.
/// </summary>
public partial class SharedCruciformSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public bool TryGetLinkedBearer(EntityUid body, EntityUid cruciform, out CruciformComponent component)
    {
        component = null!;
        if (!TryComp<CruciformBearerComponent>(body, out var bearer) || bearer.Cruciform != cruciform)
            return false;
        if (!TryComp<CruciformComponent>(cruciform, out CruciformComponent? linkedComponent) ||
            linkedComponent == null ||
            linkedComponent.ImplantedEntity != body)
            return false;

        component = linkedComponent;
        if (!TryComp<SubdermalImplantComponent>(cruciform, out var implant) || implant.ImplantedEntity != body)
            return false;
        if (!TryComp<ImplantedComponent>(body, out var installed))
            return false;

        return installed.ImplantContainer.ContainedEntities.Contains(cruciform);
    }

    public bool TryGetCruciformEntity(EntityUid body, out EntityUid cruciform, out CruciformComponent component)
    {
        cruciform = EntityUid.Invalid;
        component = null!;
        if (!TryComp<CruciformBearerComponent>(body, out var bearer) || bearer.Cruciform is not { } linked)
            return false;
        if (!TryGetLinkedBearer(body, linked, out component))
            return false;

        cruciform = linked;
        return true;
    }

    public bool TryGetCruciform(EntityUid body, out EntityUid cruciform, out CruciformComponent component)
    {
        if (!TryGetCruciformEntity(body, out cruciform, out component))
            return false;

        return component.Active;
    }

    public bool IsActiveBearer(EntityUid body)
    {
        return TryGetCruciform(body, out _, out _);
    }

    /// <summary>
    /// Active cruciform bearers within <paramref name="range"/> of <paramref name="origin"/>.
    /// </summary>
    public IEnumerable<(EntityUid Body, EntityUid Cruciform, CruciformComponent CruciformState)> BearersInRange(
        EntityUid origin, float range)
    {
        if (range <= 0)
            yield break;

        var coordinates = Transform(origin).Coordinates;
        foreach (var (body, _) in _lookup.GetEntitiesInRange<CruciformBearerComponent>(coordinates, range))
        {
            if (_transform.InRange(coordinates, Transform(body).Coordinates, range) &&
                TryGetCruciform(body, out var cruciform, out var comp))
                yield return (body, cruciform, comp);
        }
    }
}
