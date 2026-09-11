using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Item;
using Content.Shared.Paper;
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
    [Dependency] private readonly PaperSystem _paper = default!;

    /// <summary>How far from the caster an altar still counts as theirs — the litany's own reach.</summary>
    private const float RitualReach = 1.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EyeOfTheProtectorComponent, LitanyOfferingEvent>(OnLitanyOffering);
        SubscribeLocalEvent<NeoTheologyAltarComponent, LitanyBaptismalRecordEvent>(OnLitanyBaptismalRecord);
    }

    /// <summary>
    /// BaptismalRecord bridge (Eris <c>rituals/priest.dm:213-230</c>): a paper listing the
    /// parishioners slides out of the altar. Eris prints the global disciple list; this fork has
    /// no registry, so the record is a live scan of active cruciform bearers, name-sorted.
    /// </summary>
    private void OnLitanyBaptismalRecord(Entity<NeoTheologyAltarComponent> ent, ref LitanyBaptismalRecordEvent args)
    {
        if (args.Handled)
            return;

        var names = new List<string>();
        var bearers = EntityQueryEnumerator<CruciformBearerComponent>();
        while (bearers.MoveNext(out var body, out var bearer))
        {
            if (bearer.Cruciform is not { } implant ||
                !TryComp<CruciformComponent>(implant, out var state) ||
                !state.Active)
                continue;

            names.Add(MetaData(body).EntityName);
        }

        names.Sort(StringComparer.Ordinal);

        var paper = Spawn("Paper", Transform(ent.Owner).Coordinates);
        _paper.SetContent(paper, string.Join("\n", names));
        args.Handled = true;
    }

    /// <summary>
    /// EyeEconomy bridge (Eris <c>rituals/priest.dm:232-320</c>): the priest stands at the Eye and
    /// names an offering; the items rest on the altar. Eris scans seven tiles around the EOTP
    /// itself; the fork's altar is the offering surface, so the handler takes the altar within the
    /// caster's ritual reach and lets <see cref="TryMakeOffering"/> own the item math.
    /// </summary>
    private void OnLitanyOffering(EntityUid eye, EyeOfTheProtectorComponent component, ref LitanyOfferingEvent args)
    {
        if (!TryFindAltar(args.User, out var altar))
            return;

        args.Handled = TryMakeOffering(altar, eye, args.OfferingKey, out _, args.ValidateOnly);
    }

    /// <summary>The first altar within ritual reach of <paramref name="near"/>, uid-ordered for determinism.</summary>
    public bool TryFindAltar(EntityUid near, out EntityUid altar)
    {
        altar = EntityUid.Invalid;
        if (!TryComp(near, out TransformComponent? xform))
            return false;

        foreach (var candidate in _lookup
                     .GetEntitiesInRange<NeoTheologyAltarComponent>(xform.Coordinates, RitualReach)
                     .OrderBy(entry => entry.Owner))
        {
            altar = candidate.Owner;
            return true;
        }

        return false;
    }

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
    public bool TryMakeOffering(EntityUid altar, EntityUid eye, string offeringKey, out int accepted, bool validateOnly = false)
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

        if (validateOnly)
            return true;

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

    /// <summary>
    /// Whether <paramref name="item"/>'s prototype is <paramref name="wanted"/> or a descendant.
    /// The index-based walk (<c>EnumerateParents</c>) only sees non-abstract prototypes, so raw
    /// parent ids are walked as well — requirements may deliberately name an abstract base:
    /// <c>FoodProduceBase</c> covers every edible fruit, and the deferred oddity line will do the
    /// same once an oddity prototype lands.
    /// ponytail: abstract intermediates are not traversed (they have no index entry), so an
    /// abstract requirement reaches direct children and non-abstract chains only.
    /// </summary>
    private bool MatchesProto(EntityUid item, EntProtoId wanted)
    {
        if (MetaData(item).EntityPrototype is not { } proto)
            return false;

        if (proto.ID == wanted.Id)
            return true;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        EnqueueParents(queue, proto);

        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id))
                continue;
            if (id == wanted.Id)
                return true;

            // Concrete ancestors keep walking their own parents; abstract ids have no index entry.
            if (ProtoMan.TryIndex<EntityPrototype>(id, out var parent))
                EnqueueParents(queue, parent);
        }

        return false;
    }

    private static void EnqueueParents(Queue<string> queue, EntityPrototype? prototype)
    {
        if (prototype?.Parents is not { } parents)
            return;

        foreach (var parent in parents)
            queue.Enqueue(parent);
    }
}
