using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// P3.2: the Eye of the Protector banks observation from active faithful in its radius.
/// <see cref="Update"/> only paces the scan; the work lives in <see cref="Scan"/> so tests drive it
/// without waiting out <see cref="EyeOfTheProtectorComponent.ScanInterval"/>.
/// </summary>
public sealed class EyeOfTheProtectorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    /// <summary>When each Eye next scans.</summary>
    /// <remarks>ponytail: entries for deleted Eyes are never pruned. One Eye per station per the Eris
    /// map, so the leak is bounded; prune with an EntityTerminating handler if that changes.</remarks>
    private readonly Dictionary<EntityUid, TimeSpan> _nextScan = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent>();
        while (query.MoveNext(out var uid, out var eye))
        {
            if (_nextScan.TryGetValue(uid, out var next) && now < next)
                continue;

            _nextScan[uid] = now + eye.ScanInterval;
            eye.Scanned.Clear(); // new scan window: bearers can be re-awarded
            Scan(uid, eye);
        }
    }

    public void AddObservation(EntityUid eye, float amount)
    {
        if (!TryComp<EyeOfTheProtectorComponent>(eye, out var comp))
            return;

        comp.Observation = Math.Clamp(comp.Observation + amount, 0f, comp.MaxObservation);
        Dirty(eye, comp);
    }

    /// <summary>One scan: award each active faithful in radius exactly once per window.</summary>
    /// <remarks>
    /// ponytail: Eris also penalises mutants (<c>mutation_index</c>) and carrion (<c>is_carrion</c>)
    /// here via ObservationPerFaithless. Neither marker exists in this fork (only Botany plant
    /// mutations), so the penalty is deferred until a real marker lands — do not map it onto a
    /// guessed stand-in. ObservationPerFaithless stays unused until then.
    /// </remarks>
    public void Scan(EntityUid eye, EyeOfTheProtectorComponent? comp = null)
    {
        if (!Resolve(eye, ref comp))
            return;

        var xform = Transform(eye);
        var bearers = EntityQueryEnumerator<CruciformBearerComponent, TransformComponent>();
        while (bearers.MoveNext(out var body, out var bearer, out var bodyXform))
        {
            if (bearer.Cruciform is not { } cruciform ||
                !TryComp<CruciformComponent>(cruciform, out var state) ||
                !state.Active ||
                bodyXform.MapID != xform.MapID)
                continue;

            if ((bodyXform.WorldPosition - xform.WorldPosition).Length() > comp.ObservationRadius)
                continue;

            if (!comp.Scanned.Add(body))
                continue;

            AddObservation(eye, comp.ObservationPerFaithful);
            _statusEffects.TryAddStatusEffectDuration(body, "OxydNtEyeBlessing", comp.FaithfulBlessingDuration);
        }
    }

    /// <summary>The first Eye on the same map as <paramref name="near"/>, if any.</summary>
    /// <remarks>ponytail: deterministic order not required — one Eye per station.</remarks>
    public EntityUid? FindEye(EntityUid near)
    {
        var map = Transform(near).MapID;
        var query = EntityQueryEnumerator<EyeOfTheProtectorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                return uid;
        }

        return null;
    }
}
