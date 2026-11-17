using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.PDA;
using Content.Shared.UserInterface;
using Robust.Server.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Manages entity-based PVS overrides, ActiveRemoteScreenViewer authorization sets,
/// and opening/closing live remote Computer + Program BUIs for SSH remote screen sessions.
/// </summary>
public sealed partial class ServerRemoteScreenSystem : SharedRemoteScreenSystem
{
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _viewerTrackedPvs = new();
    private readonly Dictionary<(EntityUid Actor, EntityUid Target), bool> _alreadyOpenOnAttach = new();
    private readonly HashSet<(EntityUid Actor, EntityUid Target)> _detachingTargets = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActiveRemoteScreenViewerComponent, ComponentShutdown>(OnViewerShutdown);
        SubscribeLocalEvent<ActiveRemoteScreenViewerComponent, PlayerDetachedEvent>(OnViewerDetached);
        SubscribeNetworkEvent<RemoteScreenLocalWindowStateEvent>(OnLocalWindowState);
    }

    private void OnLocalWindowState(RemoteScreenLocalWindowStateEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } actor || !TryGetEntity(ev.TargetEntity, out var targetUid))
            return;

        if (!IsRemoteScreenAuthorized(actor, targetUid.Value))
            return;

        var compUid = _router.UnwrapComputerEntity(targetUid.Value);
        _alreadyOpenOnAttach[(actor, compUid)] = ev.IsOpenOnScreen;
    }

    private void OnViewerShutdown(Entity<ActiveRemoteScreenViewerComponent> ent, ref ComponentShutdown args)
    {
        var session = TryComp<ActorComponent>(ent.Owner, out var actor) ? actor.PlayerSession : null;
        RevokeTrackedPvs(ent.Owner, session);
    }

    private void OnViewerDetached(Entity<ActiveRemoteScreenViewerComponent> ent, ref PlayerDetachedEvent args)
    {
        RevokeAllForActor(ent.Owner, args.Player);
    }

    private void RevokeTrackedPvs(EntityUid actorUid, ICommonSession? session)
    {
        if (!_viewerTrackedPvs.Remove(actorUid, out var tracked))
            return;

        if (session == null)
            return;

        foreach (var ent in tracked)
        {
            if (Exists(ent))
                _pvsOverride.RemoveSessionOverride(ent, session);
        }
    }

    public bool TryBuildRemoteScreenState(
        EntityUid terminalProgUid,
        EntityUid targetComputerUid,
        EntityUid actor,
        out RemoteScreenState? state,
        out string error,
        int depth = 0)
    {
        state = null;
        error = string.Empty;

        targetComputerUid = _router.UnwrapComputerEntity(targetComputerUid);
        if (!TryComp<OxydComputerComponent>(targetComputerUid, out var targetComp))
        {
            error = "Target is not an NTOS computer entity.";
            return false;
        }

        if (!targetComp.AllowRemoteControl || targetComp.RemoteControlWireCut || targetComp.PowerWireCut)
        {
            error = "Remote control disabled or motherboard wire severed on target.";
            return false;
        }

        if (!_router.HasActiveNetworkCard(targetComputerUid))
        {
            error = "Target computer has no active network card installed.";
            return false;
        }

        if (depth >= 4)
        {
            error = "Maximum screen-sharing recursion depth reached.";
            return false;
        }

        var compSys = EntityManager.System<OxydComputerSystem>();
        var guid = _router.EnsureDeviceGuid(targetComputerUid);
        var category = GetDeviceCategory(targetComputerUid, targetComp);
        var primaryKey = compSys.GetPrimaryUiKey(targetComputerUid, targetComp);
        var fgName = MetaData(targetComputerUid).EntityName;

        if (targetComp.ActiveProgram is { } active && Exists(active) && TryComp<ProgramComponent>(active, out var actProg))
            fgName = actProg.Title;
        else if (targetComp.BootProgram is { } boot && Exists(boot) && TryComp<ProgramComponent>(boot, out var bootProg))
            fgName = bootProg.Title;

        var progNetIds = targetComp.RunningPrograms
            .Where(p => Exists(p))
            .Select(p => GetNetEntity(p))
            .ToList();

        _alreadyOpenOnAttach.TryGetValue((actor, targetComputerUid), out var wasAlreadyOpen);

        state = new RemoteScreenState(
            GetNetEntity(targetComputerUid),
            targetComp.HostMachine is { } hm && Exists(hm) ? GetNetEntity(hm) : null,
            primaryKey,
            guid,
            MetaData(targetComputerUid).EntityName,
            category,
            fgName,
            targetComp.AllowRemoteControl,
            targetComp.IsBooted,
            progNetIds,
            targetComp.DefaultScreenSize,
            depth,
            wasAlreadyOpen: wasAlreadyOpen);

        return true;
    }

    public void AttachViewer(EntityUid terminalProgUid, EntityUid actor, EntityUid targetComputerUid)
    {
        targetComputerUid = _router.UnwrapComputerEntity(targetComputerUid);
        if (!TryComp<OxydComputerComponent>(targetComputerUid, out var targetComp))
            return;

        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return;

        var compSys = EntityManager.System<OxydComputerSystem>();
        if (!targetComp.IsBooted && targetComp.AutoBootWhenPowered)
            compSys.RefreshComputerBootState(targetComputerUid, targetComp);

        var targets = new HashSet<NetEntity> { GetNetEntity(targetComputerUid) };
        var pvsEntities = new HashSet<EntityUid> { targetComputerUid };

        if (TryComp<ComputerStatsComponent>(targetComputerUid, out var stats) &&
            stats.PrimaryDriveUid is { } driveUid &&
            Exists(driveUid))
        {
            pvsEntities.Add(driveUid);
        }

        foreach (var progUid in targetComp.RunningPrograms)
        {
            if (!Exists(progUid))
                continue;
            targets.Add(GetNetEntity(progUid));
            pvsEntities.Add(progUid);
        }

        var remViewer = EnsureComp<ActiveRemoteScreenViewerComponent>(actor);
        foreach (var net in targets)
            remViewer.AuthorizedTargets.Add(net);
        Dirty(actor, remViewer);

        if (!_viewerTrackedPvs.TryGetValue(actor, out var tracked))
        {
            tracked = new HashSet<EntityUid>();
            _viewerTrackedPvs[actor] = tracked;
        }

        foreach (var pvsEnt in pvsEntities)
        {
            if (tracked.Add(pvsEnt))
                _pvsOverride.AddSessionOverride(pvsEnt, actorComp.PlayerSession);
        }

        var key = compSys.GetPrimaryUiKey(targetComputerUid, targetComp);
        _alreadyOpenOnAttach[(actor, targetComputerUid)] = _ui.IsUiOpen(targetComputerUid, key, actor);
        _ui.OpenUi(targetComputerUid, key, actor);
        compSys.UpdateComputerDisplayUi(targetComputerUid, targetComp);

        foreach (var progUid in targetComp.RunningPrograms)
        {
            if (Exists(progUid))
                _ui.OpenUi(progUid, ProgramUiKey.Key, actor);
        }

        _bandwidth.RecordScreenSharingBandwidth(targetComputerUid, _router.ResolveComputerFromProgram(terminalProgUid));
    }

    /// <summary>
    /// Ensures any actor remotely viewing a computer is immediately authorized to interact with
    /// newly launched child program entities on that computer.
    /// </summary>
    public void AuthorizeNewProgramForRemoteViewers(EntityUid compUid, EntityUid newProgUid)
    {
        var compNet = GetNetEntity(compUid);
        var progNet = GetNetEntity(newProgUid);

        var query = EntityQueryEnumerator<ActiveRemoteScreenViewerComponent, ActorComponent>();
        while (query.MoveNext(out var actorUid, out var rem, out var actorComp))
        {
            if (!rem.AuthorizedTargets.Contains(compNet))
                continue;

            rem.AuthorizedTargets.Add(progNet);
            Dirty(actorUid, rem);

            if (!_viewerTrackedPvs.TryGetValue(actorUid, out var tracked))
            {
                tracked = new HashSet<EntityUid>();
                _viewerTrackedPvs[actorUid] = tracked;
            }
            if (tracked.Add(newProgUid))
                _pvsOverride.AddSessionOverride(newProgUid, actorComp.PlayerSession);
        }
    }

    public void DetachViewer(EntityUid terminalProgUid, EntityUid? actor = null, EntityUid? targetComputerUid = null)
    {
        if (actor == null)
        {
            if (targetComputerUid == null)
                return;
            var remQuery = EntityQueryEnumerator<ActiveRemoteScreenViewerComponent, ActorComponent>();
            var tComp = _router.UnwrapComputerEntity(targetComputerUid.Value);
            var tNet = GetNetEntity(tComp);
            while (remQuery.MoveNext(out var vUid, out var rComp, out var aComp))
            {
                if (rComp.AuthorizedTargets.Contains(tNet))
                    DetachSpecificTarget(vUid, aComp.PlayerSession, rComp, tComp);
            }
            return;
        }

        if (!TryComp<ActiveRemoteScreenViewerComponent>(actor.Value, out var rem) ||
            !TryComp<ActorComponent>(actor.Value, out var actorComp))
        {
            return;
        }

        if (targetComputerUid is { } specific)
        {
            DetachSpecificTarget(actor.Value, actorComp.PlayerSession, rem, _router.UnwrapComputerEntity(specific));
        }
        else
        {
            RevokeAllForActor(actor.Value, actorComp.PlayerSession);
        }
    }

    public bool IsDetachingViewer(EntityUid actor, EntityUid targetCompUid)
    {
        return _detachingTargets.Contains((actor, _router.UnwrapComputerEntity(targetCompUid)));
    }

    /// <summary>
    /// Handles re-activating/re-opening a device locally while it is actively streamed in RemoteScreenView.
    /// Keeps all active BUIs intact instead of toggling them closed, and asks the client to pop any
    /// offscreen-stashed window back onto WindowRoot.
    /// </summary>
    public void HandleLocalOpenWhileViewing(EntityUid targetUid, Enum key, EntityUid actor)
    {
        var compUid = _router.UnwrapComputerEntity(targetUid);
        if (!Exists(compUid) || !Exists(actor))
            return;

        _alreadyOpenOnAttach[(actor, compUid)] = true;

        if (!_ui.IsUiOpen(compUid, key, actor))
            _ui.OpenUi(compUid, key, actor);

        if (TryComp<OxydComputerComponent>(compUid, out var comp))
        {
            foreach (var progUid in comp.RunningPrograms)
            {
                if (Exists(progUid) && !_ui.IsUiOpen(progUid, ProgramUiKey.Key, actor))
                    _ui.OpenUi(progUid, ProgramUiKey.Key, actor);
            }
        }

        if (TryComp<ActorComponent>(actor, out var actorComp))
            RaiseNetworkEvent(new RemoteScreenBringToFrontEvent(GetNetEntity(compUid)), actorComp.PlayerSession);
    }

    /// <summary>
    /// Ensures a remote-viewed computer UI stays open (moving it offscreen cleanly on the client)
    /// when a local close event reaches the server while an active remote screen session still views it.
    /// </summary>
    public void HandleTargetUiClosedWhileViewing(EntityUid compUid, EntityUid actor)
    {
        compUid = _router.UnwrapComputerEntity(compUid);
        if (!TryComp<OxydComputerComponent>(compUid, out var comp) ||
            !comp.AllowRemoteControl ||
            comp.RemoteControlWireCut ||
            comp.PowerWireCut ||
            !Exists(actor))
        {
            return;
        }

        _alreadyOpenOnAttach[(actor, compUid)] = false;

        var compSys = EntityManager.System<OxydComputerSystem>();
        var key = compSys.GetPrimaryUiKey(compUid, comp);
        _ui.OpenUi(compUid, key, actor);
        compSys.UpdateComputerDisplayUi(compUid, comp);

        foreach (var progUid in comp.RunningPrograms)
        {
            if (Exists(progUid) && !_ui.IsUiOpen(progUid, ProgramUiKey.Key, actor))
                _ui.OpenUi(progUid, ProgramUiKey.Key, actor);
        }
    }

    private void DetachSpecificTarget(
        EntityUid actorUid,
        ICommonSession session,
        ActiveRemoteScreenViewerComponent rem,
        EntityUid targetCompUid)
    {
        var keepLocalUiOpen = _alreadyOpenOnAttach.Remove((actorUid, targetCompUid), out var openOnClient) && openOnClient;
        _detachingTargets.Add((actorUid, targetCompUid));
        try
        {
            var compSys = EntityManager.System<OxydComputerSystem>();
            var targetNet = GetNetEntity(targetCompUid);
            rem.AuthorizedTargets.Remove(targetNet);

            if (TryComp<OxydComputerComponent>(targetCompUid, out var comp))
            {
                var key = compSys.GetPrimaryUiKey(targetCompUid, comp);
                if (!keepLocalUiOpen)
                    _ui.CloseUi(targetCompUid, key, actorUid);

                foreach (var progUid in comp.RunningPrograms)
                {
                    rem.AuthorizedTargets.Remove(GetNetEntity(progUid));
                    if (!keepLocalUiOpen && Exists(progUid))
                        _ui.CloseUi(progUid, ProgramUiKey.Key, actorUid);
                    if (!keepLocalUiOpen && _viewerTrackedPvs.TryGetValue(actorUid, out var tracked) && tracked.Remove(progUid))
                        _pvsOverride.RemoveSessionOverride(progUid, session);
                }
            }

            if (!keepLocalUiOpen &&
                TryComp<ComputerStatsComponent>(targetCompUid, out var stats) &&
                stats.PrimaryDriveUid is { } dUid &&
                _viewerTrackedPvs.TryGetValue(actorUid, out var t2) &&
                t2.Remove(dUid))
            {
                _pvsOverride.RemoveSessionOverride(dUid, session);
            }

            if (!keepLocalUiOpen && _viewerTrackedPvs.TryGetValue(actorUid, out var t3) && t3.Remove(targetCompUid))
                _pvsOverride.RemoveSessionOverride(targetCompUid, session);

            if (rem.AuthorizedTargets.Count == 0)
                RemComp<ActiveRemoteScreenViewerComponent>(actorUid);
            else
                Dirty(actorUid, rem);
        }
        finally
        {
            _detachingTargets.Remove((actorUid, targetCompUid));
        }
    }

    private void RevokeAllForActor(EntityUid actorUid, ICommonSession session)
    {
        if (TryComp<ActiveRemoteScreenViewerComponent>(actorUid, out var rem))
        {
            var targets = rem.AuthorizedTargets.Select(GetEntity).ToList();
            foreach (var ent in targets)
            {
                if (Exists(ent) && HasComp<OxydComputerComponent>(ent))
                    DetachSpecificTarget(actorUid, session, rem, ent);
            }

            if (HasComp<ActiveRemoteScreenViewerComponent>(actorUid))
            {
                foreach (var netEnt in rem.AuthorizedTargets.ToList())
                {
                    var ent = GetEntity(netEnt);
                    if (Exists(ent) && HasComp<ProgramComponent>(ent))
                        _ui.CloseUi(ent, ProgramUiKey.Key, actorUid);
                }
                RemComp<ActiveRemoteScreenViewerComponent>(actorUid);
            }
        }

        foreach (var k in _alreadyOpenOnAttach.Keys.Where(k => k.Actor == actorUid).ToList())
            _alreadyOpenOnAttach.Remove(k);

        if (_viewerTrackedPvs.Remove(actorUid, out var tracked))
        {
            foreach (var ent in tracked)
            {
                if (Exists(ent))
                    _pvsOverride.RemoveSessionOverride(ent, session);
            }
        }
    }

    public string GetDeviceCategory(EntityUid uid, OxydComputerComponent? comp = null)
    {
        if (Resolve(uid, ref comp, false))
        {
            if (comp.Template == ComputerTemplate.Pda)
                return "PDA Terminal";
            if (comp.Template == ComputerTemplate.Desktop)
                return "Workstation";
            if (comp.HostMachine is { } hm)
                return MetaData(hm).EntityName;
        }

        return MetaData(uid).EntityName;
    }
}
