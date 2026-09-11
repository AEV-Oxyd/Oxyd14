using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// P3.9: bounded scrying sessions (Eris <c>datum/ritual/inquisitor</c> scrying). The caster's eye
/// is retargeted onto an invisible marker at the target's coordinates; the session lapses after a
/// duration, and removal (timer, death, disconnect, or an explicit <c>RemComp</c>) restores the eye
/// and deletes the marker through the one <see cref="OnSessionShutdown"/> handler.
/// </summary>
public sealed class ScryingSystem : EntitySystem
{
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScryingSessionComponent, ComponentShutdown>(OnSessionShutdown);
        SubscribeLocalEvent<LitanyScryingEvent>(OnLitanyScrying);
    }

    /// <summary>
    /// Scrying bridge: the shared litany effect cannot call this server system, so it raises
    /// <see cref="LitanyScryingEvent"/> on the target body. Reuses the same bounded session the
    /// P3.9 API exposes; a caster mid-session (or without an eye) stays unhandled.
    /// </summary>
    private void OnLitanyScrying(ref LitanyScryingEvent args)
    {
        args.Handled = TryStartSession(args.Caster, args.Target, args.Duration);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ScryingSessionComponent>();
        while (query.MoveNext(out var uid, out var session))
        {
            if (now >= session.EndsAt)
                RemCompDeferred<ScryingSessionComponent>(uid);
        }
    }

    /// <summary>
    /// Begins a scrying session binding <paramref name="caster"/>'s eye to <paramref name="target"/>'s
    /// surroundings for <paramref name="duration"/>. One live session per caster.
    /// </summary>
    public bool TryStartSession(EntityUid caster, EntityUid target, TimeSpan duration)
    {
        if (!TryComp<EyeComponent>(caster, out var casterEye))
            return false;

        if (HasComp<ScryingSessionComponent>(caster))
            return false;

        var marker = SpawnAtPosition(null, Transform(target).Coordinates);
        _eye.SetTarget(caster, marker, casterEye);

        var session = EnsureComp<ScryingSessionComponent>(caster);
        session.Marker = marker;
        session.EndsAt = _timing.CurTime + duration;
        Dirty(caster, session);
        return true;
    }

    private void OnSessionShutdown(Entity<ScryingSessionComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<EyeComponent>(ent.Owner, out var eye))
            _eye.SetTarget(ent.Owner, null, eye);

        if (ent.Comp.Marker is { } marker)
            QueueDel(marker);
    }
}
