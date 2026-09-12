using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Eris <c>rituals/construction.dm</c>: Divine Guidance prints a blueprint's material list,
/// Manifestation raises the chosen structure from the materials lying on the tile in front
/// of the caster, and Uproot returns those materials and deletes the structure. The litany
/// effects are shared prototype data, so each raises a bridge event on the caster and this
/// server system owns the catalog, the front-tile item check and the refund.
/// </summary>
public sealed partial class NeoTheologyConstructionSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedCruciformSystem _cruciform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    /// <summary>Eris forbids a second Eye of the Protector in the area.</summary>
    private static readonly EntProtoId EyeOfTheProtector = "OxydNtEyeOfTheProtector";

    /// <summary>Reach that still counts as "on the tile the caster faces" (the tile is 1 m away).</summary>
    private const float ScanRadius = 1.6f;

    /// <summary>
    /// DivineGuidance (Eris <c>blueprint_check</c>): prints what the chosen blueprint needs.
    /// Exposed so tests can assert the list without scraping a popup.
    /// </summary>
    public bool TryDescribeBlueprint(ProtoId<NeoTheologyBlueprintPrototype> id, out string description)
    {
        if (!_prototypes.TryIndex(id, out NeoTheologyBlueprintPrototype? blueprint))
        {
            description = string.Empty;
            return false;
        }

        description = Describe(blueprint);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnBlueprintInfo(ref LitanyBlueprintInfoEvent args)
    {
        if (!_prototypes.TryIndex(args.Blueprint, out NeoTheologyBlueprintPrototype? blueprint))
        {
            args.Failure = "oxyd-litany-blueprint-unknown";
            return;
        }

        _popup.PopupEntity(Describe(blueprint), args.User, args.User);
        args.Handled = true;
    }

    /// <summary>
    /// Manifestation (Eris <c>construction</c>): checks the front tile, spends the materials
    /// and raises the structure. A validate-only call must not mutate.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnManifestation(ref LitanyManifestationEvent args)
    {
        if (!_prototypes.TryIndex(args.Blueprint, out NeoTheologyBlueprintPrototype? blueprint))
        {
            args.Failure = "oxyd-litany-blueprint-unknown";
            return;
        }

        if (blueprint.Build == EyeOfTheProtector && EyeExists())
        {
            args.Failure = "oxyd-litany-eye-exists";
            return;
        }

        if (!TryGetFrontTile(args.User, out _, out var frontTile))
        {
            args.Failure = "oxyd-litany-no-target";
            return;
        }

        var entities = GetEntitiesOnTile(args.User, frontTile);
        if (!TryResolveMaterials(entities, blueprint, consume: !args.ValidateOnly, out var failure))
        {
            args.Failure = failure;
            return;
        }

        if (args.ValidateOnly)
        {
            args.Handled = true;
            return;
        }

        var spawn = ToTileCoordinates(args.User, frontTile);
        SpawnAtPosition(blueprint.Build, spawn);
        args.Handled = true;
    }

    /// <summary>
    /// Uproot (Eris <c>deconstruction</c>): returns the materials of the blueprint construct on
    /// the front tile, then deletes it. A validate-only call must not mutate.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnUproot(ref LitanyUprootEvent args)
    {
        if (!TryGetFrontTile(args.User, out _, out var frontTile))
        {
            args.Failure = "oxyd-litany-no-target";
            return;
        }

        var entities = GetEntitiesOnTile(args.User, frontTile);
        foreach (var uid in entities)
        {
            if (!TryGetBlueprintForConstruct(uid, out var blueprint))
                continue;

            if (blueprint.Build == EyeOfTheProtector && !IsClergy(args.User))
            {
                args.Failure = "oxyd-litany-uproot-forbidden";
                return;
            }

            if (!args.ValidateOnly)
            {
                Refund(uid, blueprint);
                QueueDel(uid);
            }

            args.Handled = true;
            return;
        }

        args.Failure = "oxyd-litany-uproot-none";
    }

    private string Describe(NeoTheologyBlueprintPrototype blueprint)
    {
        var parts = blueprint.Materials.Select(DescribeMaterial);
        return Loc.GetString("oxyd-litany-blueprint-requires",
            ("name", Loc.GetString(blueprint.Name)),
            ("items", string.Join(", ", parts)));
    }

    private string DescribeMaterial(NeoTheologyMaterialRequirement requirement)
    {
        if (requirement.Stack is { } stack && _prototypes.TryIndex(stack, out StackPrototype? stackProto))
            return Loc.GetString("oxyd-litany-blueprint-material",
                ("amount", requirement.Amount), ("name", Loc.GetString(stackProto.Name)));

        if (requirement.Item is { } item && _prototypes.TryIndex(item, out EntityPrototype? itemProto))
            return Loc.GetString("oxyd-litany-blueprint-material",
                ("amount", requirement.Amount), ("name", itemProto.Name));

        return string.Empty;
    }

    /// <summary>
    /// Eris <c>items_check</c>: every requirement must find its whole amount on the tile. When
    /// <paramref name="consume"/> is true, stacks lose the units and single items are deleted.
    /// </summary>
    private bool TryResolveMaterials(
        List<EntityUid> entities,
        NeoTheologyBlueprintPrototype blueprint,
        bool consume,
        out LocId? failure)
    {
        var used = new HashSet<EntityUid>();
        foreach (var requirement in blueprint.Materials)
        {
            var found = false;
            foreach (var uid in entities)
            {
                if (used.Contains(uid))
                    continue;

                if (requirement.Stack is { } stackType)
                {
                    if (!TryComp<StackComponent>(uid, out var stack) ||
                        stack.StackTypeId != stackType ||
                        stack.Count < requirement.Amount)
                    {
                        continue;
                    }

                    if (consume && !_stack.TryUse((uid, stack), requirement.Amount))
                        continue;

                    found = true;
                    used.Add(uid);
                    break;
                }

                if (requirement.Item is { } itemType)
                {
                    if (MetaData(uid).EntityPrototype?.ID != itemType.Id)
                        continue;

                    if (consume)
                        QueueDel(uid);

                    found = true;
                    used.Add(uid);
                    break;
                }
            }

            if (!found)
            {
                failure = "oxyd-litany-blueprint-missing";
                return false;
            }
        }

        failure = null;
        return true;
    }

    /// <summary>Eris returns the recipe materials onto the construct's tile.</summary>
    private void Refund(EntityUid construct, NeoTheologyBlueprintPrototype blueprint)
    {
        var coords = Transform(construct).Coordinates;
        foreach (var requirement in blueprint.Materials)
        {
            if (requirement.Stack is { } stackType &&
                _prototypes.TryIndex(stackType, out StackPrototype? stackProto))
            {
                SpawnStacks(stackProto, requirement.Amount, coords);
                continue;
            }

            if (requirement.Item is { } itemType)
                SpawnAtPosition(itemType, coords);
        }
    }

    /// <summary>Splits a refund into several stacks when the prototype's cap is smaller.</summary>
    private void SpawnStacks(StackPrototype stackProto, int amount, EntityCoordinates coords)
    {
        var remaining = amount;
        while (remaining > 0)
        {
            var uid = SpawnAtPosition(stackProto.Spawn, coords);
            if (!TryComp<StackComponent>(uid, out var stack))
                return;

            var chunk = Math.Min(remaining, stackProto.MaxCount ?? remaining);
            _stack.SetCount(uid, chunk, stack);
            remaining -= chunk;
        }
    }

    private bool TryGetBlueprintForConstruct(EntityUid uid, out NeoTheologyBlueprintPrototype blueprint)
    {
        blueprint = default!;
        var prototypeId = MetaData(uid).EntityPrototype?.ID;
        if (prototypeId is null)
            return false;

        foreach (var proto in _prototypes.EnumeratePrototypes<NeoTheologyBlueprintPrototype>())
        {
            if (proto.Build.Id != prototypeId)
                continue;

            blueprint = proto;
            return true;
        }

        return false;
    }

    private bool EyeExists()
    {
        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent>();
        return query.MoveNext(out _, out _);
    }

    private bool IsClergy(EntityUid user)
    {
        return _cruciform.TryGetCruciform(user, out _, out var cruciform) &&
               LitanyEffectSystem.IsClergyProfile(cruciform.Profile);
    }

    /// <summary>
    /// The caster's tile and the tile their rotation faces, both in the caster's local
    /// coordinate space. The local rotation and the local position use the same space,
    /// so a rotated grid cannot move the front tile away from the caster's facing.
    /// </summary>
    private bool TryGetFrontTile(EntityUid user, out Vector2i ownTile, out Vector2i frontTile)
    {
        ownTile = default;
        frontTile = default;
        if (!TryComp(user, out TransformComponent? xform) || xform.MapID == MapId.Nullspace)
            return false;

        var position = xform.Coordinates.Position;
        ownTile = position.Floored();
        frontTile = (position + xform.LocalRotation.ToVec()).Floored();
        return true;
    }

    /// <summary>
    /// Entities whose local tile matches. Sorted for determinism. Candidates already
    /// come from a world-range lookup, so the local tile stays inside the reach.
    /// </summary>
    private List<EntityUid> GetEntitiesOnTile(EntityUid user, Vector2i tile)
    {
        var results = new List<EntityUid>();
        foreach (var uid in _lookup.GetEntitiesInRange(Transform(user).Coordinates, ScanRadius))
        {
            if (!TryComp(uid, out TransformComponent? xform))
                continue;

            if (xform.MapID == MapId.Nullspace || xform.Coordinates.Position.Floored() != tile)
                continue;

            results.Add(uid);
        }

        results.Sort();
        return results;
    }

    private EntityCoordinates ToTileCoordinates(EntityUid user, Vector2i tile)
    {
        var parent = Transform(user).Coordinates.EntityId;
        return new EntityCoordinates(parent, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
    }
}
