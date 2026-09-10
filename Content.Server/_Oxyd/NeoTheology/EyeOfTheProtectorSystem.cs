using Content.Shared._Oxyd.NeoTheology.Components;
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
        }
    }
}
