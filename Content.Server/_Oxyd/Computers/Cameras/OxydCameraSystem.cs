using System.Linq;
using System.Text;
using Content.Server._Oxyd.Computers.Network;
using Content.Server.Power.Components;
using Content.Server.SurveillanceCamera;
using Content.Server.SurveillanceCamera.Systems;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Cameras;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Emp;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.SurveillanceCamera.Components;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wires;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Cameras;

/// <summary>
/// Unified Oxyd Wi-Fi Surveillance Camera System:
/// 1) Connects every camera wirelessly to its closest operational <c>StationNetworkRouter</c> via <c>EntityLookupSystem</c>.
/// 2) Manages the physical <c>CameraMicrophoneUpgrade</c> slot (behind open <c>WiresPanel</c>) to enable hearing &amp; audio logging.
/// 3) Maintains a bounded timestamped witness log ring buffer per camera (sightings, combat, weapons, tampering, speech),
///    extractable remotely through <c>/bin/cameras.nte</c> or locally onto any physical Data Disk.
/// 4) Drives <see cref="SurveillanceCameraProgramComponent"/> BUI with full camera Examine support.
/// </summary>
public sealed partial class OxydCameraSystem : EntitySystem
{
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SurveillanceCameraSystem _cameras = default!;
    [Dependency] private SurveillanceCameraMicrophoneSystem _cameraMic = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<ProgramComponent> _programQuery = default!;
    [Dependency] private EntityQuery<OxydComputerComponent> _computerQuery = default!;
    [Dependency] private EntityQuery<SurveillanceCameraComponent> _cameraQuery = default!;
    [Dependency] private EntityQuery<SurveillanceCameraLogComponent> _logQuery = default!;
    [Dependency] private EntityQuery<ApcPowerReceiverComponent> _powerQuery = default!;
    [Dependency] private EntityQuery<EmpDisabledComponent> _empQuery = default!;
    [Dependency] private EntityQuery<WiresPanelComponent> _panelQuery = default!;

    private const float CameraWifiRange = 512f;
    private const float CameraWitnessLookupRange = 12f;
    private const float SightingScanIntervalSeconds = 6f;
    private const float MobSightingCooldownSeconds = 20f;
    private const float MobCombatCooldownSeconds = 1.5f;
    private const int MaxLiveAudioLines = 12;

    private static readonly SoundPathSpecifier DiskDumpSound = new("/Audio/Machines/terminal_insert_disc.ogg");

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<SurveillanceCameraProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnProgramBuiOpened);
            subs.Event<BoundUIClosedEvent>(OnProgramBuiClosed);
            subs.Event<SurveillanceCameraActionMessage>(OnProgramAction);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ActiveCameraWitnessComponent, SurveillanceCameraLogComponent, SurveillanceCameraComponent>();
        while (query.MoveNext(out var camUid, out var active, out var logComp, out var camComp))
        {
            if (curTime < active.NextSightingScan)
                continue;

            active.NextSightingScan = curTime + TimeSpan.FromSeconds(SightingScanIntervalSeconds);

            if (!IsCameraOperational(camUid, camComp))
            {
                RemCompDeferred<ActiveCameraWitnessComponent>(camUid);
                continue;
            }

            ScanCameraSightings((camUid, logComp), active, curTime);
        }
    }

    // =========================================================================
    // CAMERA LIFECYCLE, MICROPHONE UPGRADE SLOT & LOCAL DISK LOG EXTRACTION
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnCameraMapInit(Entity<SurveillanceCameraLogComponent> ent, ref MapInitEvent args)
    {
        _router.EnsureDeviceGuid(ent.Owner);
        SyncMicrophoneSlotLock(ent.Owner);
        SyncMicrophoneUpgradeState(ent);
        RefreshCameraWitnessActive(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCameraPowerChanged(Entity<SurveillanceCameraLogComponent> ent, ref PowerChangedEvent args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        AppendCameraLog(ent, "SYS", args.Powered ? "Camera power restored." : "Camera power lost.", allowWhenUnpowered: true);
        RefreshCameraWitnessActive(ent.Owner);
        RefreshOpenCameraPrograms(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCameraEmpPulse(Entity<SurveillanceCameraLogComponent> ent, ref EmpPulseEvent args)
    {
        AppendCameraLog(ent, "ALERT", "EMP interference detected — video feed disrupted.", allowWhenUnpowered: true);
        RefreshCameraWitnessActive(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCameraEmpRemoved(Entity<SurveillanceCameraLogComponent> ent, ref EmpDisabledRemovedEvent args)
    {
        AppendCameraLog(ent, "SYS", "EMP interference cleared — resuming surveillance.");
        RefreshCameraWitnessActive(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCameraPanelChanged(Entity<SurveillanceCameraLogComponent> ent, ref PanelChangedEvent args)
    {
        SyncMicrophoneSlotLock(ent.Owner);
        AppendCameraLog(ent, "ALERT", args.Open ? "Maintenance panel opened." : "Maintenance panel closed.");
    }

    [SubscribeLocalEvent]
    private void OnCameraItemInserted(Entity<SurveillanceCameraLogComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != SurveillanceCameraLogComponent.MicrophoneSlotId)
            return;

        SyncMicrophoneUpgradeState(ent);
        AppendCameraLog(ent, "SYS", "Audio microphone module installed.");
        RefreshProgramsViewingCamera(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCameraItemRemoved(Entity<SurveillanceCameraLogComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != SurveillanceCameraLogComponent.MicrophoneSlotId)
            return;

        SyncMicrophoneUpgradeState(ent);
        AppendCameraLog(ent, "SYS", "Audio microphone module removed.");
        RefreshProgramsViewingCamera(ent.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCameraExamined(Entity<SurveillanceCameraLogComponent> ent, ref ExaminedEvent args)
    {
        var guid = _router.EnsureDeviceGuid(ent.Owner);
        args.PushMarkup($"[color=#00D4FF]Wi-Fi GUID:[/color] {guid}");
        args.PushMarkup($"[color=#88CCCC]Witness Memory:[/color] {ent.Comp.UsedMemoryBytes} / {ent.Comp.MaxMemoryBytes} B ({ent.Comp.Entries.Count} logs)");
        args.PushMarkup(ent.Comp.HasMicrophoneUpgrade
            ? "[color=#66EEAA]Audio Upgrade:[/color] Microphone module installed (listening)."
            : "[color=#8899AA]Audio Upgrade:[/color] None (open maintenance panel to install a Camera Microphone Module).");
    }

    [SubscribeLocalEvent]
    private void OnCameraInteractUsing(Entity<SurveillanceCameraLogComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<FileSystemComponent>(args.Used, out var diskFs))
            return;

        if (TryDumpCameraLogsToDrive(ent, args.Used, diskFs, out var savedPath))
        {
            _audio.PlayPvs(DiskDumpSound, ent.Owner);
            _popup.PopupEntity($"Extracted {ent.Comp.Entries.Count} camera log(s) -> {savedPath}", ent.Owner, args.User);
            args.Handled = true;
        }
        else
        {
            _popup.PopupEntity("Failed to extract camera logs: disk full or read-only.", ent.Owner, args.User);
            args.Handled = true;
        }
    }

    [SubscribeLocalEvent]
    private void OnCameraGetVerbs(Entity<SurveillanceCameraLogComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Using is not { } used || !TryComp<FileSystemComponent>(used, out var diskFs))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Extract Camera Logs to Disk",
            Act = () =>
            {
                if (TryDumpCameraLogsToDrive(ent, used, diskFs, out var savedPath))
                {
                    _audio.PlayPvs(DiskDumpSound, ent.Owner);
                    _popup.PopupEntity($"Extracted {ent.Comp.Entries.Count} camera log(s) -> {savedPath}", ent.Owner, user);
                }
                else
                {
                    _popup.PopupEntity("Disk full or read-only.", ent.Owner, user);
                }
            }
        });
    }

    [SubscribeLocalEvent]
    private void OnCameraHeardSpeech(Entity<SurveillanceCameraLogComponent> ent, ref ListenEvent args)
    {
        if (!ent.Comp.HasMicrophoneUpgrade || !IsCameraOperational(ent.Owner))
            return;

        if (args.Source == ent.Owner)
            return;

        var speaker = Exists(args.Source) ? MetaData(args.Source).EntityName : "Unknown";
        AppendCameraLog(ent, "AUDIO", $"{speaker}: \"{args.Message}\"");
    }

    // =========================================================================
    // WORLD WITNESS EVENT CAPTURE (ENTITYLOOKUP + LOS, NO O(N*M) TICK LOOPS)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnMobDamaged(Entity<MobStateComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        var total = args.DamageDelta.GetTotal().Float();
        if (total < 0.5f)
            return;

        var targetName = MetaData(ent.Owner).EntityName;
        var originUid = args.Origin is { Valid: true } orig && Exists(orig) && orig != ent.Owner
            ? orig
            : (EntityUid?) null;
        var originStr = originUid != null
            ? $" (by {MetaData(originUid.Value).EntityName})"
            : string.Empty;

        var coords = _transform.GetMapCoordinates(ent.Owner);
        if (coords.MapId == MapId.Nullspace)
            return;

        var curTime = _timing.CurTime;
        var cams = _lookup.GetEntitiesInRange<SurveillanceCameraLogComponent>(coords, CameraWitnessLookupRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var cam in cams)
        {
            if (!CanCameraWitness(cam, ent.Owner, originUid))
                continue;

            if (TryComp<ActiveCameraWitnessComponent>(cam.Owner, out var active))
            {
                if (active.RecentCombatLogs.TryGetValue(ent.Owner, out var nextAllowed) && curTime < nextAllowed)
                    continue;
                active.RecentCombatLogs[ent.Owner] = curTime + TimeSpan.FromSeconds(MobCombatCooldownSeconds);
            }

            AppendCameraLog(cam, "COMBAT", $"{targetName} injured (+{total:0.#} dmg){originStr}");
        }
    }

    [SubscribeLocalEvent]
    private void OnGunShot(EntityUid uid, TransformComponent xform, ref GunShotEvent args)
    {
        var shooter = args.User;
        if (!Exists(shooter))
            return;

        var coords = _transform.GetMapCoordinates(shooter);
        if (coords.MapId == MapId.Nullspace)
            return;

        var shooterName = MetaData(shooter).EntityName;
        var gunName = MetaData(uid).EntityName;
        var cams = _lookup.GetEntitiesInRange<SurveillanceCameraLogComponent>(coords, CameraWitnessLookupRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var cam in cams)
        {
            if (CanCameraWitness(cam, shooter, uid))
                AppendCameraLog(cam, "WEAPON", $"{shooterName} fired {gunName}");
        }
    }

    [SubscribeLocalEvent]
    private void OnMeleeHit(EntityUid uid, TransformComponent xform, MeleeHitEvent args)
    {
        if (!Exists(args.User))
            return;

        var attackerName = MetaData(args.User).EntityName;
        var weaponName = uid == args.User ? "unarmed strike" : MetaData(uid).EntityName;

        // Prefer a mob victim if present; otherwise log any valid attacked entity (door, window, machine, etc.).
        var victim = args.HitEntities.FirstOrDefault(e => Exists(e) && e != args.User && HasComp<MobStateComponent>(e));
        if (!victim.Valid)
            victim = args.HitEntities.FirstOrDefault(e => Exists(e) && e != args.User);
        if (!victim.Valid)
            victim = args.HitEntities.FirstOrDefault(e => Exists(e));

        var anchorUid = victim.Valid ? victim : args.User;
        var coords = _transform.GetMapCoordinates(anchorUid);
        if (coords.MapId == MapId.Nullspace)
            return;

        var msg = victim.Valid
            ? $"{attackerName} attacked {MetaData(victim).EntityName} with {weaponName}"
            : $"{attackerName} swung {weaponName}";

        var cams = _lookup.GetEntitiesInRange<SurveillanceCameraLogComponent>(coords, CameraWitnessLookupRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var cam in cams)
        {
            if (!CanCameraWitness(cam, anchorUid, args.User))
                continue;

            AppendCameraLog(cam, "WEAPON", msg);
        }
    }

    [SubscribeLocalEvent]
    private void OnDisarmed(EntityUid uid, TransformComponent xform, ref DisarmedEvent args)
    {
        if (!Exists(args.Source) || !Exists(args.Target) || args.Source == args.Target)
            return;

        var coords = _transform.GetMapCoordinates(args.Target);
        if (coords.MapId == MapId.Nullspace)
            return;

        var sourceName = MetaData(args.Source).EntityName;
        var targetName = MetaData(args.Target).EntityName;
        var cams = _lookup.GetEntitiesInRange<SurveillanceCameraLogComponent>(coords, CameraWitnessLookupRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var cam in cams)
        {
            if (CanCameraWitness(cam, args.Target, args.Source))
                AppendCameraLog(cam, "COMBAT", $"{sourceName} disarmed/shoved {targetName}");
        }
    }

    [SubscribeLocalEvent]
    private void OnItemThrown(EntityUid uid, TransformComponent xform, ref ThrownEvent args)
    {
        if (args.User is not { Valid: true } thrower || !Exists(thrower) || !Exists(args.Thrown))
            return;

        var coords = _transform.GetMapCoordinates(thrower);
        if (coords.MapId == MapId.Nullspace)
            return;

        var throwerName = MetaData(thrower).EntityName;
        var itemName = MetaData(args.Thrown).EntityName;
        var cams = _lookup.GetEntitiesInRange<SurveillanceCameraLogComponent>(coords, CameraWitnessLookupRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var cam in cams)
        {
            if (CanCameraWitness(cam, thrower, args.Thrown))
                AppendCameraLog(cam, "THROW", $"{throwerName} threw {itemName}");
        }
    }

    [SubscribeLocalEvent]
    private void OnThrowDoHit(EntityUid uid, TransformComponent xform, ref ThrowDoHitEvent args)
    {
        if (!Exists(args.Thrown) || !Exists(args.Target))
            return;

        if (!HasComp<MobStateComponent>(args.Target) && !HasComp<DamageableComponent>(args.Target))
            return;

        var coords = _transform.GetMapCoordinates(args.Target);
        if (coords.MapId == MapId.Nullspace)
            return;

        var throwerUid = args.Component.Thrower is { Valid: true } t && Exists(t) ? t : (EntityUid?) null;
        var byStr = throwerUid != null ? $" (thrown by {MetaData(throwerUid.Value).EntityName})" : string.Empty;
        var itemName = MetaData(args.Thrown).EntityName;
        var targetName = MetaData(args.Target).EntityName;

        var cams = _lookup.GetEntitiesInRange<SurveillanceCameraLogComponent>(coords, CameraWitnessLookupRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var cam in cams)
        {
            if (CanCameraWitness(cam, args.Target, throwerUid ?? args.Thrown))
                AppendCameraLog(cam, "COMBAT", $"{itemName} hit {targetName}{byStr}");
        }
    }

    private bool CanCameraWitness(Entity<SurveillanceCameraLogComponent> cam, EntityUid primary, EntityUid? secondary = null)
    {
        if (!IsCameraOperational(cam.Owner))
            return false;

        if (HasCameraLineOfSight(cam, primary, secondary))
            return true;

        if (secondary is { Valid: true } sec && sec != primary && HasCameraLineOfSight(cam, sec, primary))
            return true;

        return false;
    }

    private bool HasCameraLineOfSight(Entity<SurveillanceCameraLogComponent> cam, EntityUid target, EntityUid? otherActor = null)
    {
        if (!Exists(target))
            return false;

        var camCoords = _transform.GetMapCoordinates(cam.Owner);
        var targetCoords = _transform.GetMapCoordinates(target);
        if (camCoords.MapId == MapId.Nullspace || camCoords.MapId != targetCoords.MapId)
            return false;

        return _examine.InRangeUnOccluded(
            camCoords,
            targetCoords,
            cam.Comp.WitnessRange,
            (cam.Owner, target, otherActor ?? EntityUid.Invalid, camCoords.Position, _transform),
            static (entity, state) =>
            {
                if (entity == state.Owner || entity == state.target || entity == state.Item3)
                    return true;

                // Wallmount surveillance cameras sit on or flush against their mounting wall tile;
                // ignore occluders within 0.65 tiles of the camera's own anchor while still allowing
                // intervening walls between rooms to block line of sight.
                var entPos = state._transform.GetMapCoordinates(entity).Position;
                return (entPos - state.Position).LengthSquared() <= 0.65f * 0.65f;
            });
    }

    private void ScanCameraSightings(Entity<SurveillanceCameraLogComponent> cam, ActiveCameraWitnessComponent active, TimeSpan curTime)
    {
        var coords = _transform.GetMapCoordinates(cam.Owner);
        if (coords.MapId == MapId.Nullspace)
            return;

        var mobs = _lookup.GetEntitiesInRange<MobStateComponent>(coords, cam.Comp.WitnessRange, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var mob in mobs)
        {
            if (active.RecentSightings.TryGetValue(mob.Owner, out var nextAllowed) && curTime < nextAllowed)
                continue;

            if (!HasCameraLineOfSight(cam, mob.Owner))
                continue;

            active.RecentSightings[mob.Owner] = curTime + TimeSpan.FromSeconds(MobSightingCooldownSeconds);
            AppendCameraLog(cam, "SIGHT", $"{MetaData(mob.Owner).EntityName} spotted in view.");
        }
    }

    public void AppendCameraLog(
        Entity<SurveillanceCameraLogComponent> cam,
        string category,
        string message,
        bool allowWhenUnpowered = false)
    {
        if (TerminatingOrDeleted(cam.Owner))
            return;

        if (!allowWhenUnpowered && !IsCameraOperational(cam.Owner))
            return;

        var cleanMsg = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrEmpty(cleanMsg))
            return;

        var byteSize = Math.Max(32, 24 + cleanMsg.Length);
        var entry = new CameraLogEntry
        {
            Timestamp = _timing.CurTime,
            Category = category,
            Message = cleanMsg,
            ByteSize = byteSize
        };

        while (cam.Comp.Entries.Count > 0 && cam.Comp.UsedMemoryBytes + byteSize > cam.Comp.MaxMemoryBytes)
        {
            var oldest = cam.Comp.Entries[0];
            cam.Comp.Entries.RemoveAt(0);
            cam.Comp.UsedMemoryBytes = Math.Max(0, cam.Comp.UsedMemoryBytes - oldest.ByteSize);
            cam.Comp.DroppedEntries++;
        }

        cam.Comp.Entries.Add(entry);
        cam.Comp.UsedMemoryBytes += byteSize;
        Dirty(cam);
        RefreshProgramsViewingCamera(cam.Owner);
    }

    public bool TryDumpCameraLogsToDrive(
        Entity<SurveillanceCameraLogComponent> cam,
        EntityUid driveUid,
        FileSystemComponent fs,
        out string savedPath)
    {
        var camName = GetCameraDisplayName(cam.Owner);
        var safeSlug = SanitizeSlug(camName);
        var fileName = $"cam_{safeSlug}";
        savedPath = $"/logs/{fileName}.txt";

        var sb = new StringBuilder();
        sb.AppendLine($"=== CAMERA WITNESS LOG: {camName} [{_router.EnsureDeviceGuid(cam.Owner)}] ===");
        sb.AppendLine($"Zone: {GetCameraZone(cam.Owner)} | Mic: {(cam.Comp.HasMicrophoneUpgrade ? "INSTALLED" : "NONE")} | Memory: {cam.Comp.UsedMemoryBytes}/{cam.Comp.MaxMemoryBytes} B (Dropped: {cam.Comp.DroppedEntries})");
        sb.AppendLine();

        if (cam.Comp.Entries.Count == 0)
        {
            sb.AppendLine("(No witnessed events recorded)");
        }
        else
        {
            foreach (var e in cam.Comp.Entries)
            {
                var ts = TimeSpan.FromSeconds(Math.Truncate(e.Timestamp.TotalSeconds)).ToString(@"hh\:mm\:ss");
                sb.Append('[').Append(ts).Append("] [").Append(e.Category).Append("] ").AppendLine(e.Message);
            }
        }

        var content = sb.ToString();
        var file = new TextVirtualFile
        {
            Name = fileName,
            Extension = "txt",
            Directory = "/logs/",
            TextContent = content,
            Size = Math.Max(64, content.Length)
        };

        return _fileSystem.TryWriteFile(driveUid, file, fs);
    }

    // =========================================================================
    // WI-FI ROUTER DISCOVERY (CLOSEST OPERATIONAL STATIONNETWORKROUTER)
    // =========================================================================

    public Entity<NetworkRouterComponent>? FindClosestRouterForCamera(EntityUid cameraUid)
    {
        var coords = _transform.GetMapCoordinates(cameraUid);
        if (coords.MapId == MapId.Nullspace)
            return null;

        var camGuid = _router.EnsureDeviceGuid(cameraUid);
        Entity<NetworkRouterComponent>? best = null;
        var bestDist = float.MaxValue;

        var candidates = _lookup.GetEntitiesInRange<NetworkRouterComponent>(coords, CameraWifiRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var rEnt in candidates)
        {
            if (!_router.IsRouterOperational(rEnt.Owner))
                continue;

            if (rEnt.Comp.BlacklistedGuids.Contains(camGuid))
                continue;

            var dist = (_transform.GetMapCoordinates(rEnt.Owner).Position - coords.Position).LengthSquared();
            if (dist < bestDist)
            {
                bestDist = dist;
                best = rEnt;
            }
        }

        return best;
    }

    public int CountCamerasForRouter(Entity<NetworkRouterComponent> router)
    {
        var coords = _transform.GetMapCoordinates(router.Owner);
        if (coords.MapId == MapId.Nullspace)
            return 0;

        var count = 0;
        var cams = _lookup.GetEntitiesInRange<SurveillanceCameraComponent>(coords, CameraWifiRange, LookupFlags.All | LookupFlags.Approximate);
        foreach (var cam in cams)
        {
            var camGuid = _router.EnsureDeviceGuid(cam.Owner);
            if (router.Comp.BlacklistedGuids.Contains(camGuid) || !IsCameraOperational(cam.Owner, cam.Comp))
                continue;

            var closest = FindClosestRouterForCamera(cam.Owner);
            if (closest != null && closest.Value.Owner == router.Owner)
                count++;
        }
        return count;
    }

    private List<Entity<SurveillanceCameraComponent>> GetReachableCamerasForComputer(EntityUid compUid, out Entity<NetworkRouterComponent>? router)
    {
        router = _router.FindNearestRouter(compUid);
        var result = new List<Entity<SurveillanceCameraComponent>>();
        if (router == null)
            return result;

        var rCoords = _transform.GetMapCoordinates(router.Value.Owner);
        if (rCoords.MapId != MapId.Nullspace)
        {
            var cams = _lookup.GetEntitiesInRange<SurveillanceCameraComponent>(rCoords, CameraWifiRange, LookupFlags.All | LookupFlags.Approximate);
            foreach (var cam in cams)
            {
                var camGuid = _router.EnsureDeviceGuid(cam.Owner);
                if (router.Value.Comp.BlacklistedGuids.Contains(camGuid))
                    continue;

                var closest = FindClosestRouterForCamera(cam.Owner);
                if (closest == null || closest.Value.Owner != router.Value.Owner)
                    continue;

                result.Add(cam);
            }
        }
        else
        {
            // #if DEBUG Nullspace fallback router: list cameras on the client's map.
            var clientCoords = _transform.GetMapCoordinates(compUid);
            if (clientCoords.MapId != MapId.Nullspace)
            {
                var cams = _lookup.GetEntitiesInRange<SurveillanceCameraComponent>(clientCoords, CameraWifiRange, LookupFlags.All | LookupFlags.Approximate);
                foreach (var cam in cams)
                {
                    var camGuid = _router.EnsureDeviceGuid(cam.Owner);
                    if (router.Value.Comp.BlacklistedGuids.Contains(camGuid))
                        continue;
                    result.Add(cam);
                }
            }
        }

        result.Sort((a, b) => string.Compare(GetCameraDisplayName(a.Owner), GetCameraDisplayName(b.Owner), StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // =========================================================================
    // SURVEILLANCE CAMERA APP (/bin/cameras.nte)
    // =========================================================================

    [SubscribeLocalEvent]
    private void OnProgramStartup(Entity<SurveillanceCameraProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateProgramUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnProgramActivated(Entity<SurveillanceCameraProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateProgramUi(ent.Owner, args.ComputerUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnProgramTerminated(Entity<SurveillanceCameraProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        DisconnectAllCameras(ent);
    }

    [SubscribeLocalEvent]
    private void OnCameraDeactivated(Entity<SurveillanceCameraProgramComponent> ent, ref SurveillanceCameraDeactivateEvent ev)
    {
        if (!ent.Comp.ActiveCameraUids.Contains(ev.Camera) && ent.Comp.ActiveCameraUid != ev.Camera)
            return;

        var camName = Exists(ev.Camera) ? GetCameraDisplayName(ev.Camera) : "camera";
        DisconnectSingleCamera(ent, ev.Camera);
        ent.Comp.StatusMessage = $"Camera signal lost: {camName} (deactivated or unpowered).";
        Dirty(ent);

        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            UpdateProgramUi(ent.Owner, compUid, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnCameraSpeechReceived(Entity<SurveillanceCameraProgramComponent> ent, ref SurveillanceCameraSpeechSendEvent ev)
    {
        var camUid = ev.Camera is { } c && ent.Comp.ActiveCameraUids.Contains(c)
            ? c
            : ent.Comp.ActiveCameraUid;

        if (camUid == null)
            return;

        var speaker = Exists(ev.Speaker) ? MetaData(ev.Speaker).EntityName : "Unknown";
        var line = new CameraAudioLineDto(_timing.CurTime, speaker, ev.Message);

        if (!ent.Comp.LiveAudioByCamera.TryGetValue(camUid.Value, out var camLines))
        {
            camLines = new List<CameraAudioLineDto>();
            ent.Comp.LiveAudioByCamera[camUid.Value] = camLines;
        }

        camLines.Add(line);
        while (camLines.Count > MaxLiveAudioLines)
            camLines.RemoveAt(0);

        if (ent.Comp.ActiveCameraUid == camUid.Value)
        {
            ent.Comp.LiveAudioFeed.Clear();
            ent.Comp.LiveAudioFeed.AddRange(camLines);
        }

        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            UpdateProgramUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnProgramBuiOpened(Entity<SurveillanceCameraProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        ent.Comp.ActiveViewers.Add(args.Actor);
        foreach (var camUid in ent.Comp.ActiveCameraUids)
        {
            if (Exists(camUid) && IsCameraOperational(camUid))
                _cameras.AddActiveViewer(camUid, args.Actor, ent.Owner);
        }

        if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid)
            UpdateProgramUi(ent.Owner, compUid, ent.Comp);
    }

    private void OnProgramBuiClosed(Entity<SurveillanceCameraProgramComponent> ent, ref BoundUIClosedEvent args)
    {
        ent.Comp.ActiveViewers.Remove(args.Actor);
        var monitorToRemove = ent.Comp.ActiveViewers.Count == 0 ? ent.Owner : (EntityUid?) null;
        foreach (var camUid in ent.Comp.ActiveCameraUids)
        {
            if (Exists(camUid))
                _cameras.RemoveActiveViewer(camUid, args.Actor, monitorToRemove);
        }

        if (ent.Comp.ActiveViewers.Count == 0)
            DisconnectAllCameras(ent);
    }

    private void OnProgramAction(Entity<SurveillanceCameraProgramComponent> ent, ref SurveillanceCameraActionMessage msg)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        ent.Comp.ActiveViewers.Add(msg.Actor);

        switch (msg.Action)
        {
            case SurveillanceCameraAction.Refresh:
                ent.Comp.StatusMessage = "Camera directory refreshed.";
                break;

            case SurveillanceCameraAction.SelectZone:
                ent.Comp.SelectedZone = string.IsNullOrWhiteSpace(msg.Zone) ? "All" : msg.Zone;
                break;

            case SurveillanceCameraAction.SelectCamera:
                if (msg.CameraEntity is { } camNet && TryGetEntity(camNet, out var camUid) && Exists(camUid.Value))
                    ConnectToCamera(ent, compUid, camUid.Value, msg.Actor);
                break;

            case SurveillanceCameraAction.FocusCamera:
                if (msg.CameraEntity is { } focusNet &&
                    TryGetEntity(focusNet, out var focusUid) &&
                    ent.Comp.ActiveCameraUids.Contains(focusUid.Value))
                {
                    SetFocusedCamera(ent, focusUid.Value);
                    ent.Comp.StatusMessage = $"Focused {GetCameraDisplayName(focusUid.Value)}.";
                }
                break;

            case SurveillanceCameraAction.DisconnectCamera:
                if (msg.CameraEntity is { } discNet &&
                    TryGetEntity(discNet, out var discUid) &&
                    ent.Comp.ActiveCameraUids.Contains(discUid.Value))
                {
                    var name = Exists(discUid.Value) ? GetCameraDisplayName(discUid.Value) : "camera";
                    DisconnectSingleCamera(ent, discUid.Value);
                    ent.Comp.StatusMessage = $"Closed {name} ({ent.Comp.ActiveCameraUids.Count}/{SurveillanceCameraProgramComponent.MaxActiveCameras} active).";
                }
                break;

            case SurveillanceCameraAction.Disconnect:
                DisconnectAllCameras(ent);
                ent.Comp.StatusMessage = "Disconnected from all camera feeds.";
                break;

            case SurveillanceCameraAction.DownloadLogs:
                var dlTarget = ResolveTargetCamera(ent, msg.CameraEntity);
                HandleRemoteDownloadLogs(ent, compUid, dlTarget);
                break;

            case SurveillanceCameraAction.ClearLogs:
                var clrTarget = ResolveTargetCamera(ent, msg.CameraEntity);
                if (clrTarget is { } clrCam && _logQuery.TryComp(clrCam, out var clrLog))
                {
                    clrLog.Entries.Clear();
                    clrLog.UsedMemoryBytes = 0;
                    clrLog.DroppedEntries = 0;
                    AppendCameraLog((clrCam, clrLog), "SYS", $"Witness memory cleared remotely by [{_router.EnsureDeviceGuid(compUid)}].");
                    ent.Comp.StatusMessage = "Cleared camera witness memory.";
                }
                break;
        }

        Dirty(ent);
        UpdateProgramUi(ent.Owner, compUid, ent.Comp);
    }

    private EntityUid? ResolveTargetCamera(Entity<SurveillanceCameraProgramComponent> ent, NetEntity? requested)
    {
        if (requested is { } net && TryGetEntity(net, out var uid) && Exists(uid.Value))
            return uid.Value;

        return ent.Comp.ActiveCameraUid;
    }

    private void ConnectToCamera(Entity<SurveillanceCameraProgramComponent> ent, EntityUid compUid, EntityUid camUid, EntityUid actor)
    {
        if (!_router.HasActiveNetworkCard(compUid) || !_router.IsNetworkConnected(compUid))
        {
            ent.Comp.StatusMessage = "Cannot connect: no active station network router reachable.";
            return;
        }

        if (!_cameraQuery.TryComp(camUid, out var camComp) || !IsCameraOperational(camUid, camComp))
        {
            ent.Comp.StatusMessage = "Camera offline, unpowered, or EMP-disabled.";
            return;
        }

        var camRouter = FindClosestRouterForCamera(camUid);
        var clientRouter = _router.FindNearestRouter(compUid);
        if (camRouter == null && clientRouter != null && !_router.IsUsingDebugFallbackRouter(clientRouter.Value))
        {
            ent.Comp.StatusMessage = "Camera has no Wi-Fi link to an operational router (or is blacklisted).";
            return;
        }

        ent.Comp.ActiveViewers.Add(actor);

        if (ent.Comp.ActiveCameraUids.Contains(camUid))
        {
            SetFocusedCamera(ent, camUid);
            foreach (var viewer in ent.Comp.ActiveViewers)
            {
                if (Exists(viewer))
                    _cameras.AddActiveViewer(camUid, viewer, ent.Owner, camComp);
            }

            ent.Comp.StatusMessage = $"Focused {GetCameraDisplayName(camUid)} ({ent.Comp.ActiveCameraUids.Count}/{SurveillanceCameraProgramComponent.MaxActiveCameras} active).";
            return;
        }

        if (ent.Comp.ActiveCameraUids.Count >= SurveillanceCameraProgramComponent.MaxActiveCameras)
        {
            ent.Comp.StatusMessage = $"Camera grid full ({SurveillanceCameraProgramComponent.MaxActiveCameras}/{SurveillanceCameraProgramComponent.MaxActiveCameras}) — close a feed first.";
            return;
        }

        ent.Comp.ActiveCameraUids.Add(camUid);
        ent.Comp.ActiveCameras.Add(GetNetEntity(camUid));
        SetFocusedCamera(ent, camUid);

        foreach (var viewer in ent.Comp.ActiveViewers)
        {
            if (Exists(viewer))
                _cameras.AddActiveViewer(camUid, viewer, ent.Owner, camComp);
        }

        var camGuid = _router.EnsureDeviceGuid(camUid);
        var clientGuid = _router.EnsureDeviceGuid(compUid);
        _router.RegisterActiveConnection(compUid, clientGuid, camGuid, "CAM", $"Camera feed: {GetCameraDisplayName(camUid)}");
        ent.Comp.StatusMessage = $"Connected to {GetCameraDisplayName(camUid)} [{camGuid}] ({ent.Comp.ActiveCameraUids.Count}/{SurveillanceCameraProgramComponent.MaxActiveCameras} active).";
    }

    private void SetFocusedCamera(Entity<SurveillanceCameraProgramComponent> ent, EntityUid? camUid)
    {
        ent.Comp.ActiveCameraUid = camUid;
        ent.Comp.ActiveCamera = camUid is { } valid && Exists(valid) ? GetNetEntity(valid) : null;
        ent.Comp.LiveAudioFeed.Clear();
        if (camUid is { } uid && ent.Comp.LiveAudioByCamera.TryGetValue(uid, out var lines))
            ent.Comp.LiveAudioFeed.AddRange(lines);
    }

    private void DisconnectSingleCamera(Entity<SurveillanceCameraProgramComponent> ent, EntityUid camUid)
    {
        if (Exists(camUid))
            _cameras.RemoveActiveViewers(camUid, new HashSet<EntityUid>(ent.Comp.ActiveViewers), ent.Owner);

        ent.Comp.ActiveCameraUids.Remove(camUid);
        ent.Comp.LiveAudioByCamera.Remove(camUid);
        SyncActiveCameraNetList(ent);

        if (ent.Comp.ActiveCameraUid == camUid)
        {
            var nextFocus = ent.Comp.ActiveCameraUids.Count > 0 ? ent.Comp.ActiveCameraUids[^1] : (EntityUid?) null;
            SetFocusedCamera(ent, nextFocus);
        }

        Dirty(ent);
    }

    private void DisconnectAllCameras(Entity<SurveillanceCameraProgramComponent> ent)
    {
        var viewersCopy = new HashSet<EntityUid>(ent.Comp.ActiveViewers);
        foreach (var oldCam in ent.Comp.ActiveCameraUids)
        {
            if (Exists(oldCam))
                _cameras.RemoveActiveViewers(oldCam, viewersCopy, ent.Owner);
        }

        ent.Comp.ActiveCameraUids.Clear();
        ent.Comp.ActiveCameras.Clear();
        ent.Comp.LiveAudioByCamera.Clear();
        SetFocusedCamera(ent, null);
        Dirty(ent);
    }

    private void SyncActiveCameraNetList(Entity<SurveillanceCameraProgramComponent> ent)
    {
        ent.Comp.ActiveCameras.Clear();
        foreach (var uid in ent.Comp.ActiveCameraUids)
        {
            if (Exists(uid))
                ent.Comp.ActiveCameras.Add(GetNetEntity(uid));
        }
    }

    private void HandleRemoteDownloadLogs(Entity<SurveillanceCameraProgramComponent> ent, EntityUid compUid, EntityUid? targetCamUid)
    {
        if (targetCamUid is not { } camUid || !_logQuery.TryComp(camUid, out var logComp))
        {
            ent.Comp.StatusMessage = "Connect to a camera first to download its witness logs.";
            return;
        }

        if (!_fileSystem.TryFindFileSystem(compUid, out var localDrive, out var localFs))
        {
            ent.Comp.StatusMessage = "Download failed: no local hard drive installed.";
            return;
        }

        var camGuid = _router.EnsureDeviceGuid(camUid);
        var clientGuid = _router.EnsureDeviceGuid(compUid);
        var pkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = camGuid,
            TargetAddress = clientGuid,
            Protocol = "TCP/CAMLOG",
            PayloadSummary = $"Camera Witness Logs ({logComp.Entries.Count} entries) [{GetCameraDisplayName(camUid)}]",
            ByteSize = Math.Max(128, logComp.UsedMemoryBytes),
            Timestamp = _timing.CurTime
        };

        // Record packet on router if client is connected.
        if (_router.HasActiveNetworkCard(compUid) && _router.IsNetworkConnected(compUid))
        {
            if (_router.FindNearestRouter(compUid) is { } router)
            {
                router.Comp.RecordedPackets.Add(pkt);
                while (router.Comp.RecordedPackets.Count > router.Comp.MaxRecordedPackets)
                    router.Comp.RecordedPackets.RemoveAt(0);
                Dirty(router);
            }
        }

        if (TryDumpCameraLogsToDrive((camUid, logComp), localDrive.Value, localFs, out var savedPath))
        {
            ent.Comp.StatusMessage = $"Downloaded {logComp.Entries.Count} log(s) -> {savedPath}";
        }
        else
        {
            ent.Comp.StatusMessage = "Download failed: local hard drive full.";
        }
    }

    public void UpdateProgramUi(EntityUid progUid, EntityUid compUid, SurveillanceCameraProgramComponent comp)
    {
        var online = _router.HasActiveNetworkCard(compUid) && _router.IsNetworkConnected(compUid);
        var routerLabel = "OFFLINE";
        var dirEntries = new List<CameraDirectoryEntryDto>();
        var zoneSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All" };

        if (online)
        {
            var cams = GetReachableCamerasForComputer(compUid, out var router);
            if (router != null)
            {
                routerLabel = $"{MetaData(router.Value.Owner).EntityName} [{_router.EnsureDeviceGuid(router.Value.Owner)}]";
            }

            foreach (var cam in cams)
            {
                var zone = GetCameraZone(cam.Owner);
                zoneSet.Add(zone);
                var isOp = IsCameraOperational(cam.Owner, cam.Comp);
                var hasLog = _logQuery.TryComp(cam.Owner, out var lc);
                var hasMic = hasLog ? lc!.HasMicrophoneUpgrade : HasComp<SurveillanceCameraMicrophoneComponent>(cam.Owner);

                dirEntries.Add(new CameraDirectoryEntryDto(
                    GetNetEntity(cam.Owner),
                    _router.EnsureDeviceGuid(cam.Owner),
                    GetCameraDisplayName(cam.Owner),
                    zone,
                    isOp,
                    hasMic,
                    hasLog ? lc!.Entries.Count : 0,
                    hasLog ? lc!.UsedMemoryBytes : 0,
                    hasLog ? lc!.MaxMemoryBytes : 4096));
            }
        }
        else if (comp.ActiveCameraUids.Count > 0 || comp.ActiveCameraUid != null)
        {
            DisconnectAllCameras((progUid, comp));
        }

        var zones = zoneSet.OrderBy(z => z == "All" ? string.Empty : z, StringComparer.OrdinalIgnoreCase).ToList();

        // Prune any active cameras that became offline or deleted.
        for (var i = comp.ActiveCameraUids.Count - 1; i >= 0; i--)
        {
            var camUid = comp.ActiveCameraUids[i];
            if (!Exists(camUid) || !IsCameraOperational(camUid))
                DisconnectSingleCamera((progUid, comp), camUid);
        }

        if (comp.ActiveCameraUid != null && !comp.ActiveCameraUids.Contains(comp.ActiveCameraUid.Value))
        {
            var fallback = comp.ActiveCameraUids.Count > 0 ? comp.ActiveCameraUids[0] : (EntityUid?) null;
            SetFocusedCamera((progUid, comp), fallback);
        }
        else if (comp.ActiveCameraUid == null && comp.ActiveCameraUids.Count > 0)
        {
            SetFocusedCamera((progUid, comp), comp.ActiveCameraUids[0]);
        }

        var activeFeeds = new List<ActiveCameraFeedDto>(comp.ActiveCameraUids.Count);
        foreach (var camUid in comp.ActiveCameraUids)
        {
            var hasLog = _logQuery.TryComp(camUid, out var lc);
            var hasMic = hasLog ? lc!.HasMicrophoneUpgrade : HasComp<SurveillanceCameraMicrophoneComponent>(camUid);
            var camAudio = comp.LiveAudioByCamera.TryGetValue(camUid, out var lines)
                ? new List<CameraAudioLineDto>(lines)
                : new List<CameraAudioLineDto>();

            activeFeeds.Add(new ActiveCameraFeedDto(
                GetNetEntity(camUid),
                _router.EnsureDeviceGuid(camUid),
                GetCameraDisplayName(camUid),
                GetCameraZone(camUid),
                hasMic,
                hasLog ? lc!.Entries.Count : 0,
                hasLog ? lc!.UsedMemoryBytes : 0,
                hasLog ? lc!.MaxMemoryBytes : 4096,
                hasLog ? lc!.DroppedEntries : 0,
                camAudio));
        }

        NetEntity? activeNet = null;
        var activeName = string.Empty;
        var activeGuid = string.Empty;
        var activeZone = string.Empty;
        var activeHasMic = false;
        var activeUsed = 0;
        var activeMax = 4096;
        var activeDropped = 0;
        var activeLogs = new List<CameraLogEntryDto>();

        if (comp.ActiveCameraUid is { } aUid && Exists(aUid) && IsCameraOperational(aUid))
        {
            activeNet = GetNetEntity(aUid);
            activeName = GetCameraDisplayName(aUid);
            activeGuid = _router.EnsureDeviceGuid(aUid);
            activeZone = GetCameraZone(aUid);
            if (_logQuery.TryComp(aUid, out var aLog))
            {
                activeHasMic = aLog.HasMicrophoneUpgrade;
                activeUsed = aLog.UsedMemoryBytes;
                activeMax = aLog.MaxMemoryBytes;
                activeDropped = aLog.DroppedEntries;
                for (var i = aLog.Entries.Count - 1; i >= 0; i--)
                {
                    var e = aLog.Entries[i];
                    activeLogs.Add(new CameraLogEntryDto(e.Timestamp, e.Category, e.Message));
                }
            }
            else
            {
                activeHasMic = HasComp<SurveillanceCameraMicrophoneComponent>(aUid);
            }
        }

        if (_ui.HasUi(progUid, ProgramUiKey.Key))
        {
            _ui.SetUiState(progUid, ProgramUiKey.Key, new SurveillanceCameraUiState(
                online,
                routerLabel,
                comp.StatusMessage,
                comp.SelectedZone,
                activeNet,
                activeName,
                activeGuid,
                activeZone,
                activeHasMic,
                activeUsed,
                activeMax,
                activeDropped,
                zones,
                dirEntries,
                activeLogs,
                new List<CameraAudioLineDto>(comp.LiveAudioFeed),
                activeFeeds,
                SurveillanceCameraProgramComponent.MaxActiveCameras));
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void SyncMicrophoneSlotLock(EntityUid cameraUid)
    {
        if (!_panelQuery.TryComp(cameraUid, out var panel))
            return;

        if (_itemSlots.TryGetSlot(cameraUid, SurveillanceCameraLogComponent.MicrophoneSlotId, out var slot))
            _itemSlots.SetLock(cameraUid, slot, !panel.Open);
    }

    private void SyncMicrophoneUpgradeState(Entity<SurveillanceCameraLogComponent> ent)
    {
        var hasMicSlot = _itemSlots.TryGetSlot(ent.Owner, SurveillanceCameraLogComponent.MicrophoneSlotId, out var slot);
        var hasInsertedUpgrade = hasMicSlot &&
                                 slot!.Item is { Valid: true } item &&
                                 HasComp<CameraMicrophoneUpgradeComponent>(item);

        // Preserve built-in microphones (e.g. wireless cameras that pre-declare hasMicrophoneUpgrade: true).
        if (hasMicSlot)
        {
            ent.Comp.HasMicrophoneUpgrade = hasInsertedUpgrade;
            if (hasInsertedUpgrade)
            {
                var range = TryComp<CameraMicrophoneUpgradeComponent>(slot!.Item!.Value, out var upg) ? upg.HearingRange : 10f;
                var mic = EnsureComp<SurveillanceCameraMicrophoneComponent>(ent.Owner);
                _cameraMic.SetEnabled(ent.Owner, true, mic);
                EnsureComp<ActiveListenerComponent>(ent.Owner).Range = range;
            }
            else if (TryComp<SurveillanceCameraMicrophoneComponent>(ent.Owner, out var existingMic))
            {
                _cameraMic.SetEnabled(ent.Owner, false, existingMic);
                RemCompDeferred<ActiveListenerComponent>(ent.Owner);
            }
        }
        else if (ent.Comp.HasMicrophoneUpgrade)
        {
            var mic = EnsureComp<SurveillanceCameraMicrophoneComponent>(ent.Owner);
            _cameraMic.SetEnabled(ent.Owner, true, mic);
            EnsureComp<ActiveListenerComponent>(ent.Owner).Range = mic.Range;
        }

        Dirty(ent);
    }

    private void RefreshCameraWitnessActive(EntityUid camUid)
    {
        if (IsCameraOperational(camUid))
        {
            var active = EnsureComp<ActiveCameraWitnessComponent>(camUid);
            if (active.NextSightingScan == TimeSpan.Zero)
                active.NextSightingScan = _timing.CurTime + TimeSpan.FromSeconds(2f);
        }
        else
        {
            RemCompDeferred<ActiveCameraWitnessComponent>(camUid);
        }
    }

    private void RefreshProgramsViewingCamera(EntityUid camUid)
    {
        var q = EntityQueryEnumerator<SurveillanceCameraProgramComponent, ProgramComponent>();
        while (q.MoveNext(out var progUid, out var comp, out var prog))
        {
            if ((comp.ActiveCameraUid == camUid || comp.ActiveCameraUids.Contains(camUid)) &&
                prog.Computer is { } compUid)
            {
                UpdateProgramUi(progUid, compUid, comp);
            }
        }
    }

    private void RefreshOpenCameraPrograms(EntityUid camUid)
    {
        var q = EntityQueryEnumerator<SurveillanceCameraProgramComponent, ProgramComponent>();
        while (q.MoveNext(out var progUid, out var comp, out var prog))
        {
            if ((comp.ActiveViewers.Count > 0 || comp.ActiveCameraUid == camUid || comp.ActiveCameraUids.Contains(camUid)) &&
                prog.Computer is { } compUid)
            {
                UpdateProgramUi(progUid, compUid, comp);
            }
        }
    }

    public bool IsCameraOperational(EntityUid camUid, SurveillanceCameraComponent? camComp = null)
    {
        if (TerminatingOrDeleted(camUid) || !_cameraQuery.Resolve(camUid, ref camComp, false))
            return false;

        if (!camComp.Active || _empQuery.HasComp(camUid))
            return false;

        if (_powerQuery.TryComp(camUid, out var power) && !power.Powered)
            return false;

        return true;
    }

    public string GetCameraDisplayName(EntityUid camUid)
    {
        if (_cameraQuery.TryComp(camUid, out var cam) &&
            !cam.UseEntityNameAsCameraId &&
            !string.IsNullOrWhiteSpace(cam.CameraId) &&
            (cam.NameSet ||
             (!string.Equals(cam.CameraId, "camera", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(cam.CameraId, "Unnamed", StringComparison.OrdinalIgnoreCase))))
        {
            return cam.CameraId;
        }

        return MetaData(camUid).EntityName;
    }

    public string GetCameraZone(EntityUid camUid)
    {
        if (TryComp<DeviceNetworkComponent>(camUid, out var devNet) && devNet.ReceiveFrequencyId is { } freqProto && !string.IsNullOrWhiteSpace(freqProto.Id))
        {
            var freq = freqProto.Id;
            if (freq.StartsWith("SurveillanceCamera", StringComparison.OrdinalIgnoreCase) && freq.Length > "SurveillanceCamera".Length)
                return freq["SurveillanceCamera".Length..];
            return freq;
        }

        if (_cameraQuery.TryComp(camUid, out var cam) && cam.AvailableNetworks.Count > 0)
        {
            var first = cam.AvailableNetworks[0].Id;
            if (first.StartsWith("SurveillanceCamera", StringComparison.OrdinalIgnoreCase) && first.Length > "SurveillanceCamera".Length)
                return first["SurveillanceCamera".Length..];
            return first;
        }

        return "General";
    }

    private static string SanitizeSlug(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (ch is ' ' or '-' or '_')
                sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "camera";
    }
}
