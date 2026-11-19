using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared.Lock;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Programs;

/// <summary>
/// Interactive NTOS shell, SSH client, network file transfer (scp/put),
/// DeviceLink terminal CLI, and live remote Screen-Sharing host.
/// Delivers direct BUI states on <see cref="ProgramUiKey.Key"/>.
/// </summary>
public sealed partial class TerminalSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guid = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private ServerRemoteScreenSystem _remoteScreen = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<ProgramComponent> _programQuery;
    private EntityQuery<OxydComputerComponent> _computerQuery;
    private readonly Dictionary<EntityUid, EntityUid> _terminalViewers = new();
    private readonly HashSet<EntityUid> _updating = new();

    public override void Initialize()
    {
        base.Initialize();

        _programQuery = GetEntityQuery<ProgramComponent>();
        _computerQuery = GetEntityQuery<OxydComputerComponent>();

        SubscribeLocalEvent<TerminalProgramComponent, ProgramStartupEvent>(OnStartup);
        SubscribeLocalEvent<TerminalProgramComponent, ProgramActivatedEvent>(OnActivated);
        SubscribeLocalEvent<TerminalProgramComponent, ProgramDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<TerminalProgramComponent, ProgramTerminatedEvent>(OnTerminated);
        Subs.BuiEvents<TerminalProgramComponent>(ProgramUiKey.Key, subs =>
        {
            subs.Event<TerminalExecuteCommandMessage>(OnExecuteCommand);
            subs.Event<TerminalRemoteScreenActionMessage>(OnRemoteScreenAction);
            subs.Event<BoundUIOpenedEvent>(OnBuiOpened);
            subs.Event<BoundUIClosedEvent>(OnBuiClosed);
        });
    }

    private void OnStartup(Entity<TerminalProgramComponent> ent, ref ProgramStartupEvent args)
    {
        UpdateTerminalUi(ent.Owner);
    }

    private void OnActivated(Entity<TerminalProgramComponent> ent, ref ProgramActivatedEvent args)
    {
        UpdateTerminalUi(ent.Owner);
    }

    private void OnDeactivated(Entity<TerminalProgramComponent> ent, ref ProgramDeactivatedEvent args)
    {
        DetachRemoteScreenSession(ent.Owner, ent.Comp);
    }

    private void OnTerminated(Entity<TerminalProgramComponent> ent, ref ProgramTerminatedEvent args)
    {
        DetachRemoteScreenSession(ent.Owner, ent.Comp);
    }

    private void OnBuiOpened(Entity<TerminalProgramComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateTerminalUi(ent.Owner);
    }

    private void OnBuiClosed(Entity<TerminalProgramComponent> ent, ref BoundUIClosedEvent args)
    {
        DetachRemoteScreenSession(ent.Owner, ent.Comp, args.Actor);
        UpdateTerminalUi(ent.Owner);
    }

    private void DetachRemoteScreenSession(EntityUid progUid, TerminalProgramComponent comp, EntityUid? actor = null)
    {
        var viewer = actor ?? (_terminalViewers.TryGetValue(progUid, out var recorded) ? recorded : (EntityUid?) null);
        if (viewer != null && comp.ConnectedRemoteGuid != null && _router.TryFindDeviceByGuid(comp.ConnectedRemoteGuid, out var targetUid))
        {
            _remoteScreen.DetachViewer(progUid, viewer, targetUid);
        }

        if (actor == null || _terminalViewers.TryGetValue(progUid, out var cur) && cur == actor)
            _terminalViewers.Remove(progUid);

        comp.IsScreenAttached = false;
    }

    private void OnExecuteCommand(Entity<TerminalProgramComponent> ent, ref TerminalExecuteCommandMessage args)
    {
        if (!_programQuery.TryComp(ent.Owner, out var prog) || prog.Computer is not { } compUid)
            return;

        if (!_computerQuery.TryComp(compUid, out var oxydComp))
            return;

        if (oxydComp.DisableLocalShell && ent.Comp.ConnectedRemoteGuid == null)
        {
            ent.Comp.History.Add("LOCAL SHELL DISABLED ON THIS CONTROLLER.");
            UpdateTerminalUi(ent.Owner);
            return;
        }

        if (oxydComp.HostMachine is { } hm && _lock.IsLocked(hm) && ent.Comp.ConnectedRemoteGuid == null)
        {
            ent.Comp.History.Add("DEVICE LOCKED — Swipe authorized ID card to unlock.");
            UpdateTerminalUi(ent.Owner);
            return;
        }

        var input = args.CommandText.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        if (Exists(args.Actor))
            _terminalViewers[ent.Owner] = args.Actor;

        var comp = ent.Comp;
        var localGuid = _guid.GetGuid(compUid);

        if (comp.AwaitingPassword)
        {
            comp.History.Add("********");
            HandlePasswordAuth(compUid, comp, input);
            UpdateTerminalUi(ent.Owner);
            return;
        }

        var prompt = BuildPrompt(localGuid, comp);
        comp.History.Add($"{prompt}{input}");

        if (comp.ConnectedRemoteGuid != null && !comp.SshLocalMode)
            HandleRemoteSshCommand(ent.Owner, compUid, comp, input, args.Actor);
        else
            HandleLocalCommand(ent.Owner, compUid, comp, input, args.Actor);

        UpdateTerminalUi(ent.Owner);
    }

    private void OnRemoteScreenAction(Entity<TerminalProgramComponent> ent, ref TerminalRemoteScreenActionMessage args)
    {
        if (args.Action == "detach")
        {
            DetachRemoteScreenSession(ent.Owner, ent.Comp, args.Actor);
            if (_programQuery.TryComp(ent.Owner, out var prog) && prog.Computer is { } compUid && ent.Comp.ConnectedRemoteGuid != null)
            {
                var localGuid = _guid.GetGuid(compUid);
                _router.UnregisterActiveConnection(compUid, $"SCREEN-{localGuid}-{ent.Comp.ConnectedRemoteGuid}");
                ent.Comp.History.Add($"Remote display stream detached from [{ent.Comp.ConnectedRemoteGuid}].");
            }
            UpdateTerminalUi(ent.Owner);
        }
    }

    private void HandlePasswordAuth(EntityUid compUid, TerminalProgramComponent comp, string password)
    {
        comp.AwaitingPassword = false;
        var targetGuid = comp.PendingAuthGuid;
        var targetUser = comp.PendingAuthUser ?? "admin";
        comp.PendingAuthGuid = null;
        comp.PendingAuthUser = null;

        if (string.IsNullOrEmpty(targetGuid) || !_router.TryFindDeviceByGuid(targetGuid, out var targetUid))
        {
            comp.History.Add("ssh: Host lost during authentication.");
            return;
        }

        if (!_computerQuery.TryComp(targetUid, out var targetOxyd) ||
            !string.Equals(targetOxyd.SshPassword, password, StringComparison.Ordinal))
        {
            comp.History.Add("Permission denied, please try again.");
            return;
        }

        EstablishSshSession(compUid, comp, targetUid, targetGuid, targetUser);
    }

    private void EstablishSshSession(EntityUid compUid, TerminalProgramComponent comp, EntityUid targetUid, string targetGuid, string user)
    {
        var localGuid = _guid.GetGuid(compUid);
        comp.ConnectedRemoteGuid = targetGuid;
        comp.ConnectedRemoteUser = user;
        comp.SshLocalMode = false;
        comp.RemoteDirectory = "/";

        _router.RegisterActiveConnection(compUid, localGuid, targetGuid, "SSH", $"SSH ({user}@{targetGuid})");

        if (_fileSystem.TryFindFileSystem(compUid, out var localDrive, out _))
            _fileSystem.LogSystemActivity(localDrive.Value, "SSH-OUT", $"Opened SSH session to {targetGuid}");
        if (_fileSystem.TryFindFileSystem(targetUid, out var remDrive, out _))
            _fileSystem.LogSystemActivity(remDrive.Value, "SSH-IN", $"Accepted SSH session from {localGuid}");

        comp.History.Add($"Connected to {MetaData(targetUid).EntityName} [{targetGuid}].");
        comp.History.Add("Type 'help' for remote commands or 'screen open' to stream live display.");
    }

    private bool RequireNetwork(EntityUid compUid, TerminalProgramComponent comp)
    {
        if (!_router.HasActiveNetworkCard(compUid))
        {
            comp.History.Add("net: No active network card installed (or network wire severed).");
            return false;
        }

        if (!_router.IsNetworkConnected(compUid))
        {
            comp.History.Add("net: Network unreachable — router offline or GUID blacklisted.");
            return false;
        }

        return true;
    }

    private void HandleLocalCommand(EntityUid progUid, EntityUid compUid, TerminalProgramComponent comp, string input, EntityUid actor)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var localGuid = _guid.GetGuid(compUid);

        switch (cmd)
        {
            case "help":
                comp.History.Add("NTOS Local Shell Commands:");
                comp.History.Add("  ls [dir] | cd <dir> | pwd       - Browse hard drive files");
                comp.History.Add("  cat <file> | write <f> <text>   - Inspect or edit text files");
                comp.History.Add("  rm <file>                       - Delete file from hard drive");
                comp.History.Add("  run <file.nte> | ps | kill <id> - Run/manage .nte program entities");
                comp.History.Add("  hw | reboot                     - Inspect hardware / reboot computer");
                comp.History.Add("  devlink [list|trigger|send]     - Manage mirrored DeviceLink ports");
                comp.History.Add("  apc | atmos | lathe [...]       - Quick DeviceLink machine commands");
                comp.History.Add("  ifconfig | ping <guid> | scan   - Network diagnostics (NIC required)");
                comp.History.Add("  ssh <guid> | scp / upload       - Connect & transfer files over router");
                comp.History.Add("  passwd [pass] | keygen          - Configure local SSH password/keys");
                comp.History.Add("  switch | clear | exit           - Toggle remote/local mode or close");
                break;

            case "clear":
                comp.History.Clear();
                break;

            case "switch":
            case "remote":
                if (comp.ConnectedRemoteGuid != null)
                {
                    comp.SshLocalMode = false;
                    comp.History.Add($"Switched context to remote host [{comp.ConnectedRemoteGuid}].");
                }
                else
                {
                    comp.History.Add("No active SSH session.");
                }
                break;

            case "pwd":
                comp.History.Add(comp.CurrentDirectory);
                break;

            case "cd":
                comp.CurrentDirectory = NormalizeDir(comp.CurrentDirectory, parts.Length > 1 ? parts[1] : "/");
                break;

            case "ls":
                var dirFilter = parts.Length > 1 ? NormalizeDir(comp.CurrentDirectory, parts[1]) : comp.CurrentDirectory;
                AppendDirectoryListing(compUid, dirFilter, comp);
                break;

            case "cat" when parts.Length > 1:
                AppendCatFile(compUid, ResolvePath(comp.CurrentDirectory, parts[1]), comp);
                break;

            case "write" when parts.Length >= 3:
                WriteTextFileCmd(compUid, ResolvePath(comp.CurrentDirectory, parts[1]), string.Join(" ", parts.Skip(2)), comp);
                break;

            case "rm" when parts.Length > 1:
                DeleteFileCmd(compUid, ResolvePath(comp.CurrentDirectory, parts[1]), comp);
                break;

            case "ps":
            case "tasks":
                AppendProcessList(compUid, comp, localGuid);
                break;

            case "kill" when parts.Length > 1:
                if (int.TryParse(parts[1], out var pid) &&
                    _computerQuery.TryComp(compUid, out var kComp))
                {
                    var match = kComp.RunningPrograms.FirstOrDefault(p => p.Id == pid);
                    if (match.IsValid() && _computer.CloseProgram(compUid, match, kComp))
                        comp.History.Add($"Terminated PID {pid}.");
                    else
                        comp.History.Add($"Unable to terminate PID {pid} (boot process is protected).");
                }
                break;

            case "run" when parts.Length > 1:
                if (_computer.TryLaunchExecutable(compUid, parts[1], out var launched, out var runErr, actor))
                    comp.History.Add($"Started {parts[1]} (PID {launched.Value.Id}).");
                else
                    comp.History.Add($"exec: {runErr}");
                break;

            case "reboot":
                _computer.RebootComputer(compUid);
                comp.History.Add("System rebooted.");
                break;

            case "hw":
            case "top":
            case "stat":
                AppendHardwareMetrics(compUid, comp, localGuid);
                break;

            case "ifconfig":
                var stats = CompOrNull<ComputerStatsComponent>(compUid);
                var nicOn = _router.HasActiveNetworkCard(compUid);
                var rOnline = nicOn && _router.IsNetworkConnected(compUid);
                var netLabel = rOnline ? _router.GetConnectedNetworkLabel(compUid) : null;
                var rText = rOnline ? (string.IsNullOrEmpty(netLabel) ? "ONLINE" : $"ONLINE ({netLabel})") : "OFFLINE";
                comp.History.Add($"eth0: GUID={localGuid} NIC={(nicOn ? "UP" : "MISSING/CUT")} ROUTER={rText}");
                comp.History.Add($"      DL={stats?.NetworkBandwidthBps ?? 0} B/s  UL={stats?.NetworkUploadBandwidthBps ?? 0} B/s");
                break;

            case "ping" when parts.Length > 1:
                if (!RequireNetwork(compUid, comp))
                    break;
                HandlePingCommand(compUid, comp, string.Join(" ", parts.Skip(1)));
                break;

            case "scan":
            case "arp":
                if (!RequireNetwork(compUid, comp))
                    break;
                AppendNetworkScan(compUid, comp);
                break;

            case "passwd":
                if (_computerQuery.TryComp(compUid, out var pwComp))
                {
                    pwComp.SshPassword = parts.Length > 1 ? parts[1] : string.Empty;
                    Dirty(compUid, pwComp);
                    comp.History.Add(string.IsNullOrEmpty(pwComp.SshPassword)
                        ? "SSH password cleared (public access)."
                        : "SSH password updated.");
                }
                break;

            case "ssh" when parts.Length > 1:
                if (!RequireNetwork(compUid, comp))
                    break;
                InitiateSsh(compUid, comp, parts[1], localGuid);
                break;

            case "exit":
            case "quit":
                if (comp.ConnectedRemoteGuid != null)
                {
                    comp.SshLocalMode = false;
                    HandleRemoteSshCommand(progUid, compUid, comp, "exit", actor);
                }
                else
                {
                    _computer.CloseProgram(compUid, progUid);
                }
                break;

            default:
                if (_computer.ExecuteComputerCommand(compUid, input, out var dlOut, actor) && !string.IsNullOrEmpty(dlOut))
                    comp.History.Add(dlOut);
                else
                    comp.History.Add($"sh: command not found: '{cmd}'.");
                break;
        }
    }

    private void HandleRemoteSshCommand(EntityUid progUid, EntityUid localCompUid, TerminalProgramComponent comp, string input, EntityUid actor)
    {
        var targetGuid = comp.ConnectedRemoteGuid!;
        var localGuid = _guid.GetGuid(localCompUid);

        if (!RequireNetwork(localCompUid, comp) ||
            _router.IsGuidBlacklisted(localCompUid, localGuid) ||
            _router.IsGuidBlacklisted(localCompUid, targetGuid))
        {
            comp.History.Add($"ssh: Session to {targetGuid} dropped by network/firewall.");
            DetachRemoteScreenSession(progUid, comp, actor);
            comp.ConnectedRemoteGuid = null;
            return;
        }

        if (!_router.TryFindDeviceByGuid(targetGuid, out var targetUid) ||
            !_router.HasActiveNetworkCard(targetUid) ||
            !_computerQuery.TryComp(targetUid, out var targetOxyd) ||
            !targetOxyd.AllowRemoteControl || targetOxyd.RemoteControlWireCut || targetOxyd.PowerWireCut)
        {
            comp.History.Add($"ssh: Connection reset by peer {targetGuid}.");
            DetachRemoteScreenSession(progUid, comp, actor);
            comp.ConnectedRemoteGuid = null;
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "exit":
            case "quit":
            case "logout":
                DetachRemoteScreenSession(progUid, comp, actor);
                _router.UnregisterActiveConnection(localCompUid, $"SSH-{localGuid}-{targetGuid}");
                _router.UnregisterActiveConnection(localCompUid, $"SCREEN-{localGuid}-{targetGuid}");
                comp.History.Add($"Connection to {targetGuid} closed.");
                comp.ConnectedRemoteGuid = null;
                comp.SshLocalMode = false;
                break;

            case "help":
                comp.History.Add($"Remote Commands on [{targetGuid}]:");
                comp.History.Add("  ls | cd | pwd | cat | write | rm - Manage remote hard drive files");
                comp.History.Add("  run <file.nte> | ps | kill <id>  - Run/stop remote .nte program entities");
                comp.History.Add("  screen [open|close|info]         - Attach live remote GUI Screen View");
                comp.History.Add("  scp / fetch <remoteFile>         - Download remote file to local HDD");
                comp.History.Add("  upload / put <localFile>         - Upload local file to remote HDD");
                comp.History.Add("  devlink | apc | atmos | lathe    - Control remote DeviceLink ports");
                comp.History.Add("  top | hw | packets | reboot      - Remote telemetry & packet capture");
                comp.History.Add("  local | clear | exit             - Switch local mode or disconnect");
                break;

            case "clear":
                comp.History.Clear();
                break;

            case "local":
            case "switch":
                comp.SshLocalMode = true;
                comp.History.Add("Switched shell context to [LOCAL] (type 'switch' to return remote).");
                break;

            case "pwd":
                comp.History.Add(comp.RemoteDirectory);
                break;

            case "cd":
                comp.RemoteDirectory = NormalizeDir(comp.RemoteDirectory, parts.Length > 1 ? parts[1] : "/");
                break;

            case "ls":
                AppendDirectoryListing(targetUid, parts.Length > 1 ? NormalizeDir(comp.RemoteDirectory, parts[1]) : comp.RemoteDirectory, comp);
                break;

            case "cat" when parts.Length > 1:
                AppendCatFile(targetUid, ResolvePath(comp.RemoteDirectory, parts[1]), comp);
                break;

            case "write" when parts.Length >= 3:
                WriteTextFileCmd(targetUid, ResolvePath(comp.RemoteDirectory, parts[1]), string.Join(" ", parts.Skip(2)), comp);
                break;

            case "rm" when parts.Length > 1:
                DeleteFileCmd(targetUid, ResolvePath(comp.RemoteDirectory, parts[1]), comp);
                break;

            case "ps":
            case "tasks":
                AppendProcessList(targetUid, comp, targetGuid);
                break;

            case "kill" when parts.Length > 1:
                if (int.TryParse(parts[1], out var rPid))
                {
                    var match = targetOxyd.RunningPrograms.FirstOrDefault(p => p.Id == rPid);
                    if (match.IsValid() && _computer.CloseProgram(targetUid, match, targetOxyd))
                        comp.History.Add($"Terminated remote PID {rPid}.");
                    else
                        comp.History.Add($"Unable to terminate remote PID {rPid}.");
                }
                break;

            case "run" when parts.Length > 1:
                if (_computer.TryLaunchExecutable(targetUid, parts[1], out var rLaunched, out var rErr, actor, targetOxyd))
                    comp.History.Add($"Launched '{parts[1]}' on [{targetGuid}] as PID {rLaunched.Value.Id}.");
                else
                    comp.History.Add($"remote exec: {rErr}");
                break;

            case "hw":
            case "top":
            case "stat":
                AppendHardwareMetrics(targetUid, comp, targetGuid);
                break;

            case "packets":
            case "tcpdump":
                if (TryComp<NetworkBandwidthComponent>(targetUid, out var remBw) && remBw.StoredPackets.Count > 0)
                {
                    foreach (var p in remBw.StoredPackets.TakeLast(8))
                        comp.History.Add(NetworkRouterSystem.GetPacketReadableSummary(p));
                }
                else
                {
                    comp.History.Add("Packet buffer empty.");
                }
                break;

            case "reboot":
                _computer.RebootComputer(targetUid, targetOxyd);
                comp.History.Add($"Remote computer [{targetGuid}] rebooted.");
                break;

            case "scp" when parts.Length > 1:
            case "fetch" when parts.Length > 1:
                TransferFileAcrossNetwork(targetUid, localCompUid, ResolvePath(comp.RemoteDirectory, parts[1]), isUpload: false, comp);
                break;

            case "upload" when parts.Length > 1:
            case "put" when parts.Length > 1:
                TransferFileAcrossNetwork(localCompUid, targetUid, ResolvePath(comp.CurrentDirectory, parts[1]), isUpload: true, comp);
                break;

            case "screen":
                var sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "open";
                if (sub is "close" or "off" or "detach")
                {
                    DetachRemoteScreenSession(progUid, comp, actor);
                    _router.UnregisterActiveConnection(localCompUid, $"SCREEN-{localGuid}-{targetGuid}");
                    comp.History.Add($"Closed remote display stream for [{targetGuid}].");
                }
                else if (sub == "info")
                {
                    comp.History.Add($"Remote Display [{targetGuid}]: Attached={comp.IsScreenAttached}, Booted={targetOxyd.IsBooted}, ActiveProgs={targetOxyd.RunningPrograms.Count}");
                }
                else
                {
                    comp.IsScreenAttached = true;
                    _terminalViewers[progUid] = actor;
                    _remoteScreen.AttachViewer(progUid, actor, targetUid);
                    _router.RegisterActiveConnection(localCompUid, localGuid, targetGuid, "SCREEN", $"Screen ({targetGuid})");
                    comp.History.Add($"Attached live Screen View to {MetaData(targetUid).EntityName} [{targetGuid}].");
                }
                break;

            default:
                if (_computer.ExecuteComputerCommand(targetUid, input, out var remOut, actor) && !string.IsNullOrEmpty(remOut))
                    comp.History.Add(remOut);
                else
                    comp.History.Add($"remote sh: command not found: '{cmd}'.");
                break;
        }
    }

    private void TransferFileAcrossNetwork(EntityUid srcComp, EntityUid dstComp, string srcPath, bool isUpload, TerminalProgramComponent comp)
    {
        if (!_fileSystem.TryFindFileSystem(srcComp, out _, out var srcFs) || !srcFs.Files.TryGetValue(srcPath, out var file))
        {
            comp.History.Add($"transfer: source file '{srcPath}' not found on hard drive.");
            return;
        }

        if (!_fileSystem.TryFindFileSystem(dstComp, out var dstDrive, out var dstFs))
        {
            comp.History.Add("transfer: destination has no hard drive.");
            return;
        }

        var pkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = _guid.GetGuid(srcComp),
            TargetAddress = _guid.GetGuid(dstComp),
            Protocol = isUpload ? "SCP/PUT" : "SCP/GET",
            PayloadSummary = $"{srcPath} ({file.Size} B)",
            ByteSize = Math.Max(64, file.Size),
            Timestamp = _timing.CurTime
        };

        if (!_router.RecordAndValidatePacket(srcComp, dstComp, pkt))
        {
            comp.History.Add("transfer: packet dropped by router or insufficient bandwidth/NIC.");
            return;
        }

        if (_fileSystem.TryWriteFile(dstDrive.Value, file.Clone(), dstFs))
            comp.History.Add($"Transferred '{srcPath}' ({file.Size} B) across router -> destination HDD.");
        else
            comp.History.Add("transfer: destination hard drive full.");
    }

    private void InitiateSsh(EntityUid localCompUid, TerminalProgramComponent comp, string rawTarget, string localGuid)
    {
        var targetQuery = rawTarget.Contains('@') ? rawTarget.Split('@', 2)[1] : rawTarget;
        var targetUser = rawTarget.Contains('@') ? rawTarget.Split('@', 2)[0] : "admin";

        var resolvedGuid = _router.ResolveDns(localCompUid, targetQuery) ?? targetQuery.ToUpperInvariant();
        if (_router.IsGuidBlacklisted(localCompUid, localGuid) || _router.IsGuidBlacklisted(localCompUid, resolvedGuid))
        {
            comp.History.Add($"ssh: blocked by router blacklist ({resolvedGuid}).");
            return;
        }

        if (!_router.TryFindDeviceByGuid(resolvedGuid, out var targetUid) || !_computerQuery.TryComp(targetUid, out var targetComp))
        {
            comp.History.Add($"ssh: Could not resolve host '{rawTarget}'.");
            return;
        }

        if (!_router.HasActiveNetworkCard(targetUid))
        {
            comp.History.Add($"ssh: host {resolvedGuid} unreachable (no active network card).");
            return;
        }

        if (!targetComp.AllowRemoteControl || targetComp.RemoteControlWireCut || targetComp.PowerWireCut)
        {
            comp.History.Add($"ssh: connect to {resolvedGuid} refused (remote control disabled).");
            return;
        }

        if (!string.IsNullOrEmpty(targetComp.SshPassword))
        {
            comp.AwaitingPassword = true;
            comp.PendingAuthGuid = resolvedGuid;
            comp.PendingAuthUser = targetUser;
            comp.History.Add($"{targetUser}@{resolvedGuid}'s password:");
            return;
        }

        EstablishSshSession(localCompUid, comp, targetUid, resolvedGuid, targetUser);
    }

    private void HandlePingCommand(EntityUid localCompUid, TerminalProgramComponent comp, string target)
    {
        var guid = _router.ResolveDns(localCompUid, target) ?? target.ToUpperInvariant();
        if (!_router.TryFindDeviceByGuid(guid, out var tUid) || !_router.HasActiveNetworkCard(tUid))
        {
            comp.History.Add($"PING {target}: Request timed out / unreachable.");
            return;
        }

        var pkt = new NetworkDataPacket
        {
            PacketId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SourceAddress = _guid.GetGuid(localCompUid),
            TargetAddress = guid,
            Protocol = "ICMP/ECHO",
            PayloadSummary = $"Ping {guid}",
            ByteSize = 64,
            Timestamp = _timing.CurTime
        };

        if (_router.RecordAndValidatePacket(localCompUid, tUid, pkt))
            comp.History.Add($"64 bytes from {guid} ({MetaData(tUid).EntityName}): icmp_seq=1 time=1.2 ms");
        else
            comp.History.Add($"PING {guid}: Dropped by router firewall.");
    }

    private void AppendNetworkScan(EntityUid localCompUid, TerminalProgramComponent comp)
    {
        var router = _router.FindNearestRouter(localCompUid);
        if (router == null)
        {
            comp.History.Add("scan: No reachable station router.");
            return;
        }

        _router.RefreshRouterDevices(router.Value);
        var netLabel = _router.GetConnectedNetworkLabel(localCompUid);
        var routerGuid = _router.EnsureDeviceGuid(router.Value.Owner);
        var headerPrefix = string.IsNullOrEmpty(netLabel)
            ? $"Router [{routerGuid}]"
            : $"Global Debug Router [{routerGuid}] ({netLabel})";
        comp.History.Add($"{headerPrefix} Active Network Hosts ({router.Value.Comp.ConnectedDevices.Count}):");
        foreach (var dGuid in router.Value.Comp.ConnectedDevices)
        {
            if (_router.TryFindDeviceByGuid(dGuid, out var dUid))
            {
                var blocked = router.Value.Comp.BlacklistedGuids.Contains(dGuid) ? " [BLACKLISTED]" : "";
                comp.History.Add($"  {dGuid}  {MetaData(dUid).EntityName}{blocked}");
            }
        }
    }

    private void AppendDirectoryListing(EntityUid compUid, string dir, TerminalProgramComponent comp)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out _, out var fs))
        {
            comp.History.Add("No hard drive installed.");
            return;
        }

        var normDir = dir.TrimEnd('/');
        var matches = fs.Files
            .Where(kv => dir == "/" || kv.Value.Directory.TrimEnd('/').Equals(normDir, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            comp.History.Add($"(empty directory: {dir})");
            return;
        }

        foreach (var (path, f) in matches)
            comp.History.Add($"  {path,-28} {f.Size,6} B{(f.IsReadOnly ? " [RO]" : "")}");
    }

    private void AppendCatFile(EntityUid compUid, string path, TerminalProgramComponent comp)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out _, out var fs) || !fs.Files.TryGetValue(path, out var file))
        {
            comp.History.Add($"cat: {path}: No such file.");
            return;
        }

        if (file is TextVirtualFile tf)
            comp.History.Add(tf.TextContent);
        else if (file is ProgramVirtualFile pf)
            comp.History.Add($"[NTE] {pf.Title} -> Entity={pf.ProgramId} RAM={pf.RamUsageMb}MB");
        else if (file is DatabaseVirtualFile df)
        {
            foreach (var (k, v) in df.Data.Take(20))
                comp.History.Add($"  {k}: {v}");
        }
        else if (file is TaskListVirtualFile tk)
        {
            foreach (var t in tk.Tasks)
                comp.History.Add($"  [{(t.IsTaskDone ? "x" : " ")}] #{t.Id} {t.Description} ({t.TaskIsFor})");
        }
        else if (file is RecipeBlueprintFile bp)
        {
            comp.History.Add($"Recipes: {string.Join(", ", bp.GetAllRecipes())} (Uses={bp.RemainingUses?.ToString() ?? "inf"}, Enc={bp.IsEncrypted})");
        }
    }

    private void WriteTextFileCmd(EntityUid compUid, string path, string text, TerminalProgramComponent comp)
    {
        if (!_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs))
        {
            comp.History.Add("write: No hard drive installed.");
            return;
        }

        var idx = path.LastIndexOf('/');
        var dir = idx >= 0 ? path[..(idx + 1)] : "/";
        var fullName = idx >= 0 ? path[(idx + 1)..] : path;
        var dot = fullName.LastIndexOf('.');
        var name = dot > 0 ? fullName[..dot] : fullName;
        var ext = dot > 0 ? fullName[(dot + 1)..] : "txt";

        var ok = _fileSystem.TryWriteFile(driveUid.Value, new TextVirtualFile
        {
            Name = name,
            Extension = ext,
            Directory = dir,
            TextContent = text,
            Size = Math.Max(32, text.Length)
        }, fs);

        comp.History.Add(ok ? $"Wrote {path}." : "write: Failed (read-only or full).");
    }

    private void DeleteFileCmd(EntityUid compUid, string path, TerminalProgramComponent comp)
    {
        if (_fileSystem.TryFindFileSystem(compUid, out var driveUid, out var fs) &&
            _fileSystem.TryDeleteFile(driveUid.Value, path, fs))
            comp.History.Add($"Deleted {path}.");
        else
            comp.History.Add($"rm: Failed to delete '{path}'.");
    }

    private void AppendProcessList(EntityUid compUid, TerminalProgramComponent comp, string guid)
    {
        if (!_computerQuery.TryComp(compUid, out var oxyd))
            return;

        comp.History.Add($"Processes on [{guid}] ({oxyd.RunningPrograms.Count}):");
        foreach (var pUid in oxyd.RunningPrograms)
        {
            if (!TryComp<ProgramComponent>(pUid, out var p))
                continue;
            comp.History.Add($"  {pUid.Id,-5} {p.Title,-20} {p.RamUsageMb,3}MB  {(p.IsBootProgram ? "[BOOT]" : "")} {p.ExecutablePath}");
        }
    }

    private void AppendHardwareMetrics(EntityUid compUid, TerminalProgramComponent comp, string guid)
    {
        var s = CompOrNull<ComputerStatsComponent>(compUid);
        if (s == null)
        {
            comp.History.Add("Hardware telemetry unavailable.");
            return;
        }

        comp.History.Add($"Hardware Telemetry [{guid}]:");
        comp.History.Add($"  CPU: {(s.HasCpu ? $"{s.CpuFrequencyMhz} MHz (load {s.CurrentCpuLoadPercent:0.#}%, max {s.MaxConcurrentPrograms} procs)" : "NONE")}");
        comp.History.Add($"  RAM: {s.UsedRamMb} / {s.RamMb} MB");
        comp.History.Add($"  HDD: {s.UsedStorageBytes} / {s.StorageBytes} B ({(s.HasHardDrive ? "ONLINE" : "MISSING")})");
        comp.History.Add($"  NIC: {(s.HasNetworkCard ? $"DL={s.NetworkBandwidthBps} B/s UL={s.NetworkUploadBandwidthBps} B/s" : "NONE")}");
    }

    private static string NormalizeDir(string current, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "/")
            return "/";
        if (target == "~")
            return "/notes/";
        var combined = target.StartsWith('/') ? target : $"{current.TrimEnd('/')}/{target}";
        var segments = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var seg in segments)
        {
            if (seg == ".")
                continue;
            if (seg == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(seg);
        }
        return stack.Count == 0 ? "/" : "/" + string.Join('/', stack) + "/";
    }

    private static string ResolvePath(string currentDir, string path)
    {
        if (path.StartsWith('/'))
            return path;
        return currentDir == "/" ? "/" + path : $"{currentDir.TrimEnd('/')}/{path}";
    }

    private static string BuildPrompt(string localGuid, TerminalProgramComponent comp)
    {
        if (comp.ConnectedRemoteGuid != null)
            return comp.SshLocalMode
                ? $"[LOCAL {localGuid}]$ "
                : $"{comp.ConnectedRemoteUser ?? "admin"}@{comp.ConnectedRemoteGuid}:{comp.RemoteDirectory}$ ";
        return $"[{localGuid}:{comp.CurrentDirectory}]$ ";
    }

    public void UpdateTerminalUi(EntityUid progUid)
    {
        if (!_updating.Add(progUid))
            return;

        try
        {
            if (!TryComp<TerminalProgramComponent>(progUid, out var comp) ||
                !_programQuery.TryComp(progUid, out var prog) ||
                prog.Computer is not { } compUid)
            {
                return;
            }

            while (comp.History.Count > 100)
                comp.History.RemoveAt(0);

            var localGuid = _guid.GetGuid(compUid);
            RemoteScreenState? remoteScreen = null;

            if (comp.IsScreenAttached &&
                comp.ConnectedRemoteGuid != null &&
                _terminalViewers.TryGetValue(progUid, out var viewer) &&
                _router.TryFindDeviceByGuid(comp.ConnectedRemoteGuid, out var targetUid))
            {
                if (_remoteScreen.TryBuildRemoteScreenState(progUid, targetUid, viewer, out remoteScreen, out _))
                    _bandwidth.RecordScreenSharingBandwidth(targetUid, compUid, 256);
                else
                    comp.IsScreenAttached = false;
            }

            var hasNic = _router.HasActiveNetworkCard(compUid);
            var routerOnline = hasNic && _router.IsNetworkConnected(compUid);
            var networkLabel = routerOnline ? _router.GetConnectedNetworkLabel(compUid) : null;

            var state = new TerminalUiState(
                new List<string>(comp.History),
                BuildPrompt(localGuid, comp),
                localGuid,
                comp.ConnectedRemoteGuid,
                comp.AwaitingPassword,
                comp.SshLocalMode,
                comp.IsScreenAttached,
                hasNic,
                routerOnline,
                remoteScreen,
                networkLabel);

            if (_ui.HasUi(progUid, ProgramUiKey.Key))
                _ui.SetUiState(progUid, ProgramUiKey.Key, state);
        }
        finally
        {
            _updating.Remove(progUid);
        }
    }
}
