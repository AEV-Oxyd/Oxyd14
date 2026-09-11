using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;

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
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;

        SubscribeLocalEvent<ScryingSessionComponent, ComponentShutdown>(OnSessionShutdown);
        SubscribeLocalEvent<LitanyScryingEvent>(OnLitanyScrying);
        SubscribeLocalEvent<ScryingSessionComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ScryingSessionComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    /// <summary>
    /// Scrying bridge: the shared litany effect cannot call this server system, so it raises
    /// <see cref="LitanyScryingEvent"/> on the target body. Reuses the same bounded session the
    /// P3.9 API exposes; a caster mid-session (or without an eye) stays unhandled.
    /// </summary>
    private void OnLitanyScrying(ref LitanyScryingEvent args)
    {
        args.Handled = args.ValidateOnly
            ? CanStartSession(args.Caster, args.Target)
            : TryStartSession(args.Caster, args.Target, args.Duration);
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is SessionStatus.Disconnected or SessionStatus.Zombie)
            EndSessionOnLosingControl(args.Session);
    }

    /// <summary>
    /// Ends the caster's session when the controlling player disconnects or leaves.
    /// Public so tests can exercise the disconnect path without a database-cached session.
    /// </summary>
    public void EndSessionOnLosingControl(ICommonSession session)
    {
        if (session.AttachedEntity is { } body)
            RemComp<ScryingSessionComponent>(body);
    }

    private void OnMobStateChanged(Entity<ScryingSessionComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            RemComp<ScryingSessionComponent>(ent.Owner);
    }

    private void OnPlayerDetached(Entity<ScryingSessionComponent> ent, ref PlayerDetachedEvent args)
    {
        RemComp<ScryingSessionComponent>(ent.Owner);
    }

    public bool CanStartSession(EntityUid caster, EntityUid target)
    {
        return !TerminatingOrDeleted(caster) && !TerminatingOrDeleted(target) &&
               HasComp<EyeComponent>(caster) && !_mobState.IsDead(caster) &&
               !HasComp<ScryingSessionComponent>(caster);
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
        if (!CanStartSession(caster, target) || !TryComp<EyeComponent>(caster, out var casterEye))
            return false;

        var marker = SpawnAtPosition(null, Transform(target).Coordinates);
        var session = EnsureComp<ScryingSessionComponent>(caster);
        session.PreviousTarget = casterEye.Target;
        session.Marker = marker;
        _eye.SetTarget(caster, marker, casterEye);
        session.EndsAt = _timing.CurTime + duration;
        Dirty(caster, session);
        return true;
    }

    private void OnSessionShutdown(Entity<ScryingSessionComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<EyeComponent>(ent.Owner, out var eye))
        {
            var previous = ent.Comp.PreviousTarget;
            if (previous is { } target && TerminatingOrDeleted(target))
                previous = null;
            _eye.SetTarget(ent.Owner, previous, eye);
        }

        if (ent.Comp.Marker is { } marker)
            QueueDel(marker);
    }
}
