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
    /// ponytail: linear scan per call. Fine for ≤100 players; add a spatial index if a
    /// ceremony ever runs per-tick on a 200-pop server.
    /// </summary>
    public IEnumerable<(EntityUid Body, EntityUid Cruciform, CruciformComponent CruciformState)> BearersInRange(
        EntityUid origin, float range)
    {
        var xform = Transform(origin);
        var query = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();

        while (query.MoveNext(out var body, out var bearer, out var bodyXform))
        {
            if (bodyXform.MapID != xform.MapID)
                continue;
            if ((bodyXform.WorldPosition - xform.WorldPosition).Length() > range)
                continue;
            if (bearer.Cruciform is not { } cruciform)
                continue;
            if (!TryComp<CruciformComponent>(cruciform, out var comp) || !comp.Active)
                continue;

            yield return (body, cruciform, comp);
        }
    }
}
