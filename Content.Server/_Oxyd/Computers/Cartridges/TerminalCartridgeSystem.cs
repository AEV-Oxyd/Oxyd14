using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.CartridgeLoader;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Cartridges;

public sealed partial class TerminalCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Content.Server.PDA.PdaSystem _pdaSystem = default!;
    [Dependency] private NetworkBandwidthSystem _bandwidth = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;

    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private ServerRemoteScreenSystem _remoteScreen = default!;

    /// <summary>
    /// Viewer actor -> remote device whose screen they are currently streaming.
    /// Used to keep the remote device (and its programs) inside the viewer's PVS so the client can resolve its NetEntities.
    /// </summary>
    private readonly Dictionary<EntityUid, EntityUid> _activeRemoteScreens = new();

    /// <summary>
    /// Terminal entity -> most recent interacting viewer actor.
    /// </summary>
    private readonly Dictionary<EntityUid, EntityUid> _terminalViewers = new();

    /// <summary>
    /// Terminals whose UI state is currently being rebuilt; prevents infinite update loops when two devices view each other.
    /// </summary>
    private readonly HashSet<EntityUid> _updatingTerminals = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TerminalCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<TerminalCartridgeComponent, CartridgeActivatedEvent>(OnCartridgeActivated);
        SubscribeLocalEvent<TerminalCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    private void OnCartridgeActivated(EntityUid uid, TerminalCartridgeComponent comp, ref CartridgeActivatedEvent args)
    {
        UpdateUiState(uid, args.Loader, comp);
    }

    private void AttachRemoteScreen(EntityUid actor, EntityUid target)
    {
        if (_activeRemoteScreens.TryGetValue(actor, out var prev) && prev != target)
            DetachRemoteScreen(actor);

        _activeRemoteScreens[actor] = target;

        if (_playerManager.TryGetSessionByEntity(actor, out var session))
            _pvsOverride.AddSessionOverride(target, session);
    }

    private void DetachRemoteScreen(EntityUid? actor)
    {
        if (actor is not { } a || !_activeRemoteScreens.Remove(a, out var target))
            return;

        if (!TerminatingOrDeleted(target) && _playerManager.TryGetSessionByEntity(a, out var session))
            _pvsOverride.RemoveSessionOverride(target, session);
    }

    public void OnRemoteScreenClosed(EntityUid actor, EntityUid uid)
    {
        if (_activeRemoteScreens.TryGetValue(actor, out var target) && target == uid)
            DetachRemoteScreen(actor);
        _remoteScreen.DetachAllForHostDevice(actor, uid);
    }

    public bool OnRemoteScreenOpened(EntityUid actor, EntityUid hostPdaUid)
    {
        if (!TryComp<CartridgeLoaderComponent>(hostPdaUid, out var loader) ||
            loader.ActiveProgram is not { } activeProg ||
            !TryComp<TerminalCartridgeComponent>(activeProg, out var termComp))
        {
            return false;
        }

        if (actor != EntityUid.Invalid)
            _terminalViewers[activeProg] = actor;

        UpdateUiState(activeProg, hostPdaUid, termComp);
        return true;
    }
    private bool TryGetDisk(EntityUid loaderUid, out EntityUid diskUid, [NotNullWhen(true)] out FileSystemComponent? diskFs, out string diskName)
    {
        diskUid = default;
        diskFs = null;
        diskName = "Data Disk";

        if (TryComp<PdaComponent>(loaderUid, out var pda) && pda.DiskSlot.Item is { } dUid && !TerminatingOrDeleted(dUid))
        {
            if (TryComp<FileSystemComponent>(dUid, out var dfs))
            {
                diskUid = dUid;
                diskFs = dfs;
                diskName = MetaData(dUid).EntityName;
                return true;
            }
        }
        return false;
    }

    private void ForwardToRemoteProgram(TerminalCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (!comp.IsScreenAttached ||
            comp.ConnectedRemoteGuid == null ||
            !_guidSystem.TryGetEntityByGuid(comp.ConnectedRemoteGuid, out var optTarget) ||
            optTarget is not { } targetUid ||
            !TryComp<CartridgeLoaderComponent>(targetUid, out var remoteLoader) ||
            remoteLoader.ActiveProgram is not { } remoteProgram)
        {
            return;
        }

        // Re-address the message as if it were sent through the remote device's own loader.
        args.LoaderUid = GetNetEntity(targetUid);
        RaiseLocalEvent(remoteProgram, args);
    }

    private void OnUiReady(EntityUid uid, TerminalCartridgeComponent comp, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, comp);
    }

    private void OnUiMessage(EntityUid uid, TerminalCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (args.Actor != EntityUid.Invalid)
            _terminalViewers[uid] = args.Actor;

        if (args is not TerminalUiMessageEvent msg ||
            (msg.TargetProgram is { } targetProg && GetEntity(targetProg) != uid))
        {
            // Not a message for this terminal itself: while a remote screen is embedded, it comes from the
            // remote program's UI fragment (which the client hosts inside this terminal), so forward it.
            ForwardToRemoteProgram(comp, args);
            return;
        }

        var loaderUid = GetEntity(args.LoaderUid);

        if (!string.IsNullOrEmpty(msg.RemoteAction))
        {
            HandleRemoteScreenAction(uid, loaderUid, comp, msg.RemoteAction, msg.RemoteActionArg, args.Actor);
            UpdateUiState(uid, loaderUid, comp);
            return;
        }

        var input = msg.CommandText.Trim();

        if (string.IsNullOrEmpty(input))
        {
            UpdateUiState(uid, loaderUid, comp);
            return;
        }

        var localGuid = _guidSystem.GetGuid(loaderUid);
        var isSsh = comp.ConnectedRemoteGuid != null;
        var currentPrompt = isSsh
            ? (comp.SshLocalMode
                ? $"[LOCAL] [{localGuid}]$ "
                : $"{comp.ConnectedRemoteUser ?? "admin"}@{comp.ConnectedRemoteGuid}:~$ ")
            : (comp.AwaitingPassword ? "Password: " : $"[{localGuid}]$ ");

        if (comp.AwaitingPassword)
        {
            comp.History.Add($"Password: ********");
            HandlePasswordAuth(loaderUid, comp, input);
            UpdateUiState(uid, loaderUid, comp);
            return;
        }

        comp.History.Add($"{currentPrompt}{input}");

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        var (fName, pName) = ExtractFileAndPackage(cmd, parts);

        if (isSsh)
        {
            if (cmd is "switch" or "mode")
            {
                comp.SshLocalMode = !comp.SshLocalMode;
                comp.History.Add(comp.SshLocalMode
                    ? "Switched shell context to LOCAL. Commands will execute locally. Use 'switch', 'remote', or button to return."
                    : $"Switched shell context to REMOTE [{comp.ConnectedRemoteGuid}].");
                UpdateUiState(uid, loaderUid, comp);
                return;
            }

            if (cmd == "local")
            {
                comp.SshLocalMode = true;
                comp.History.Add("Switched shell context to LOCAL. Commands will execute locally. Use 'switch', 'remote', or button to return.");
                UpdateUiState(uid, loaderUid, comp);
                return;
            }

            if (cmd == "remote")
            {
                comp.SshLocalMode = false;
                comp.History.Add($"Switched shell context to REMOTE [{comp.ConnectedRemoteGuid}].");
                UpdateUiState(uid, loaderUid, comp);
                return;
            }

            if (comp.SshLocalMode)
            {
                _fileSystem.LogSystemActivity(loaderUid, $"SSH-LOCAL ({comp.ConnectedRemoteGuid})", input, fName, pName);
                HandleLocalCommand(loaderUid, comp, input);
            }
            else
            {
                _fileSystem.LogSystemActivity(loaderUid, $"SSH-OUT -> {comp.ConnectedRemoteGuid}", input, fName, pName);
                if (comp.ConnectedRemoteGuid != null && _guidSystem.TryGetEntityByGuid(comp.ConnectedRemoteGuid, out var optRem) && optRem.HasValue)
                {
                    _fileSystem.LogSystemActivity(optRem.Value, $"SSH-IN <- {_guidSystem.GetGuid(loaderUid)}", input, fName, pName);
                }
                HandleRemoteSshCommand(uid, loaderUid, comp, input, args.Actor);
            }
        }
        else
        {
            _fileSystem.LogSystemActivity(loaderUid, "LOCAL-TERM", input, fName, pName);
            HandleLocalCommand(loaderUid, comp, input);
        }

        UpdateUiState(uid, loaderUid, comp);
    }

    private (string? fileName, string? packageName) ExtractFileAndPackage(string cmd, string[] parts)
    {
        string? fileName = null;
        string? packageName = null;

        if (parts.Length > 1)
        {
            var target = parts[1];
            if (cmd is "run" or "exec" or "start" or "install")
            {
                packageName = target.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                    ? target
                    : $"{target}.bin";
            }
            else if (target.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || target.StartsWith("/bin/"))
            {
                packageName = target;
            }
            else
            {
                fileName = target;
            }
        }

        if (parts.Length > 2 && fileName == null)
        {
            fileName = parts[2];
        }

        return (fileName, packageName);
    }

    private void HandlePasswordAuth(EntityUid loaderUid, TerminalCartridgeComponent comp, string password)
    {
        comp.AwaitingPassword = false;
        var targetGuid = comp.PendingSshGuid;

        if (!_guidSystem.TryGetEntityByGuid(targetGuid, out var optTargetUid) || !optTargetUid.HasValue)
        {
            comp.History.Add($"ssh: Connection to {targetGuid} dropped.");
            return;
        }
        var targetUid = optTargetUid.Value;

        var valid = password == "admin" || password == "ntos";
        if (TryComp<DataServerComponent>(targetUid, out var dataServer))
        {
            if (password == dataServer.NetworkMasterKey || dataServer.AcceptsPublicReads)
                valid = true;
        }

        if (TryComp<PdaComponent>(targetUid, out var targetPda))
        {
            if (!string.IsNullOrEmpty(targetPda.SshPassword) && password == targetPda.SshPassword)
                valid = true;
        }

        if (valid)
        {
            comp.ConnectedRemoteGuid = targetGuid;
            comp.ConnectedRemoteUser = comp.PendingSshUser;
            comp.History.Add($"Authentication successful. Connection established.");
            comp.History.Add($"Last login: {DateTime.Now:yyyy-MM-dd HH:mm:ss} from {_guidSystem.GetGuid(loaderUid)}");
            comp.History.Add($"Type 'help' for remote commands or 'exit' to disconnect.");

            _fileSystem.LogSystemActivity(loaderUid, "SSH-AUTH", $"Connected to {targetGuid} as {comp.ConnectedRemoteUser}");
            _fileSystem.LogSystemActivity(targetUid, "SSH-AUTH", $"Accepted connection from {_guidSystem.GetGuid(loaderUid)} as {comp.ConnectedRemoteUser}");
        }
        else
        {
            comp.History.Add("Permission denied (publickey, password).");
            _fileSystem.LogSystemActivity(loaderUid, "SSH-AUTH", $"Failed authentication to {targetGuid}");
            _fileSystem.LogSystemActivity(targetUid, "SSH-AUTH", $"Rejected auth attempt from {_guidSystem.GetGuid(loaderUid)}");
        }
    }

    private EntityUid? FindInstalledProgram(EntityUid deviceUid, string query, [NotNullWhen(false)] out string? notInstalledProtoId)
    {
        notInstalledProtoId = null;
        var clean = query.Trim();
        if (clean.StartsWith("/bin/"))
            clean = clean["/bin/".Length..];
        if (clean.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^".bin".Length];

        static string Norm(string s) => s.Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "")
            .Replace(".", "")
            .ToLowerInvariant();

        var normQuery = Norm(clean);
        if (string.IsNullOrEmpty(normQuery))
            return null;

        var allPrograms = _cartridgeLoader.GetAllPrograms(deviceUid).ToList();

        // 1. Check if query matches a ProgramVirtualFile in filesystem to resolve its ProgramId
        string? resolvedProtoId = null;
        if (TryComp<FileSystemComponent>(deviceUid, out var fs))
        {
            foreach (var f in fs.Files.Values)
            {
                if (f is ProgramVirtualFile pvf)
                {
                    if (Norm(pvf.Name) == normQuery ||
                        Norm(pvf.Title) == normQuery ||
                        Norm(pvf.ProgramId) == normQuery)
                    {
                        resolvedProtoId = pvf.ProgramId;
                        break;
                    }
                }
            }
        }

        // 2. Exact / normalized match on already installed instances
        foreach (var p in allPrograms)
        {
            var meta = MetaData(p);
            var protoId = meta.EntityPrototype?.ID ?? "";
            var normProto = Norm(protoId);
            var normName = Norm(meta.EntityName);

            // Match by resolved prototype
            if (resolvedProtoId != null && protoId.Equals(resolvedProtoId, StringComparison.OrdinalIgnoreCase))
                return p;

            // Direct equality
            if (normProto == normQuery || normName == normQuery)
                return p;

            // Strip "oxyd" and "cartridge"
            var strippedProto = normProto;
            if (strippedProto.StartsWith("oxyd"))
                strippedProto = strippedProto[4..];
            if (strippedProto.EndsWith("cartridge"))
                strippedProto = strippedProto[..^"cartridge".Length];

            if (strippedProto == normQuery)
                return p;

            if (TryComp<CartridgeComponent>(p, out var cartComp))
            {
                var progName = Loc.GetString(cartComp.ProgramName);
                if (Norm(progName) == normQuery)
                    return p;
            }
        }

        // 3. Substring / partial match on already installed instances
        foreach (var p in allPrograms)
        {
            var meta = MetaData(p);
            var protoId = meta.EntityPrototype?.ID ?? "";
            var normProto = Norm(protoId);
            var strippedProto = normProto;
            if (strippedProto.StartsWith("oxyd"))
                strippedProto = strippedProto[4..];
            if (strippedProto.EndsWith("cartridge"))
                strippedProto = strippedProto[..^"cartridge".Length];

            if (strippedProto.Contains(normQuery) || normQuery.Contains(strippedProto))
                return p;

            if (TryComp<CartridgeComponent>(p, out var cartComp))
            {
                var progName = Loc.GetString(cartComp.ProgramName);
                if (Norm(progName).Contains(normQuery) || normQuery.Contains(Norm(progName)))
                    return p;
            }
        }

        // 4. Program is not currently installed. If it exists in filesystem as a binary, return its prototype for installation
        if (resolvedProtoId != null)
        {
            notInstalledProtoId = resolvedProtoId;
        }

        return null;
    }

    private void HandleLocalCommand(EntityUid loaderUid, TerminalCartridgeComponent comp, string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var localGuid = _guidSystem.GetGuid(loaderUid);

        switch (cmd)
        {
            case "help":
                comp.History.Add("Available Local Commands:");
                comp.History.Add("  scan [type]           - Scan network for devices (APC, AIR-ALARM, COMPUTER, PDA)");
                comp.History.Add("  ssh <guid> [user]     - Connect to remote host via SSH key/auth");
                comp.History.Add("  ping <guid>           - Test network connectivity to target device");
                comp.History.Add("  ifconfig / ip         - Display local network configuration");
                comp.History.Add("  packets / tcpdump     - Inspect local packet buffers and router traffic log");
                comp.History.Add("  ls [dir]              - List files in local directory");
                comp.History.Add("  cd [dir]              - Change current working directory");
                comp.History.Add("  pwd                   - Print current working directory");
                comp.History.Add("  cat <file>            - Print content of local file (e.g. /sys/system.log)");
                comp.History.Add("  run / exec <program>  - Execute / launch program");
                if (comp.ConnectedRemoteGuid != null)
                {
                    comp.History.Add("  switch / remote       - Return to remote SSH session");
                }
                comp.History.Add("  rm <file>             - Delete a local file");
                comp.History.Add("  keygen                - Generate local ed25519 SSH public key");
                comp.History.Add("  clear                 - Clear terminal screen");
                comp.History.Add("  exit                  - Close terminal session");
                break;

            case "scan":
            case "devices":
            case "arp":
                AppendDeviceScan(loaderUid, comp, parts.Length > 1 ? parts[1] : null);
                break;

            case "pwd":
                comp.History.Add(comp.CurrentDirectory ?? "/");
                break;

            case "cd":
                var targetDir = parts.Length > 1 ? parts[1].Trim() : "/";
                if (targetDir == "~" || targetDir == "")
                    targetDir = "/notes/";
                else if (targetDir == "..")
                    targetDir = "/";
                else if (!targetDir.StartsWith('/'))
                    targetDir = (comp.CurrentDirectory ?? "/") + targetDir;
                if (!targetDir.EndsWith('/'))
                    targetDir += "/";
                comp.CurrentDirectory = targetDir;
                comp.History.Add($"Working directory: {comp.CurrentDirectory}");
                break;

            case "exit":
                if (comp.ConnectedRemoteGuid != null)
                {
                    comp.SshLocalMode = false;
                    comp.History.Add($"Exited local shell mode. Returned to remote SSH session [{comp.ConnectedRemoteGuid}].");
                    return;
                }
                comp.History.Add("exit: Local shell cannot be closed. Terminal session active.");
                break;

            case "switch":
            case "remote":
                if (comp.ConnectedRemoteGuid != null)
                {
                    comp.SshLocalMode = false;
                    comp.History.Add($"Switched shell context to REMOTE [{comp.ConnectedRemoteGuid}].");
                    return;
                }
                comp.History.Add("remote: No active remote SSH session to switch to.");
                break;

            case "clear":
                comp.History.Clear();
                break;

            case "ssh":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: ssh <guid> [user]  or  ssh <user>@<guid>");
                    return;
                }

                var targetArg = parts[1];
                var user = "admin";
                var guid = targetArg;

                if (targetArg.Contains('@'))
                {
                    var atSplit = targetArg.Split('@');
                    user = atSplit[0];
                    guid = atSplit[1];
                }
                else if (parts.Length >= 3)
                {
                    user = parts[2];
                }

                guid = guid.ToUpperInvariant();
                _remoteScreen.EnsureAllDevicesProvisioned();

                if (!_guidSystem.TryGetEntityByGuid(guid, out var optTarget) || !optTarget.HasValue)
                {
                    comp.History.Add($"ssh: Could not resolve hostname {guid}: Host unreachable");
                    return;
                }
                var targetUid = optTarget.Value;

                comp.History.Add($"Initiating SSH handshake with {guid}:22...");

                // Check key-based authentication via target's authorized_keys
                var keyMatched = false;
                if (TryComp<FileSystemComponent>(targetUid, out var targetFs))
                {
                    if (_fileSystem.TryGetFile<TextVirtualFile>(targetUid, "/sys/authorized_keys", out var authFile, targetFs))
                    {
                        if (authFile.TextContent.Contains(localGuid))
                            keyMatched = true;
                    }
                }

                if (TryComp<DataServerComponent>(targetUid, out var ds) && ds.AcceptsPublicReads)
                    keyMatched = true;

                if (keyMatched)
                {
                    comp.ConnectedRemoteGuid = guid;
                    comp.ConnectedRemoteUser = user;
                    comp.History.Add($"Authenticated with public key (id_ed25519). Access granted.");
                    comp.History.Add($"Welcome to NT-OS Remote Shell [{guid}]");

                    _fileSystem.LogSystemActivity(loaderUid, "SSH-AUTH", $"Connected to {guid} as {user} (publickey)");
                    _fileSystem.LogSystemActivity(targetUid, "SSH-AUTH", $"Accepted connection from {localGuid} as {user} (publickey)");
                }
                else
                {
                    comp.PendingSshGuid = guid;
                    comp.PendingSshUser = user;
                    comp.AwaitingPassword = true;
                    comp.History.Add($"{user}@{guid}'s password: ");
                }
                break;

            case "ping":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: ping <guid>");
                    return;
                }
                var pingGuid = parts[1].ToUpperInvariant();
                if (_guidSystem.TryGetEntityByGuid(pingGuid, out var optPing) && optPing.HasValue)
                {
                    var pingTarget = optPing.Value;
                    TryComp<NetworkBandwidthComponent>(pingTarget, out var bw);
                    var saturated = bw?.IsSaturated ?? false;
                    var latency = saturated ? 480 : 16;
                    comp.History.Add($"PING {pingGuid}: 64 bytes received. rtt={latency}ms saturated={saturated}");

                    _bandwidth.StorePacket(loaderUid, localGuid, pingGuid, "ICMP/PING", "Echo request (64 B)", 64);
                    _bandwidth.StorePacket(pingTarget, localGuid, pingGuid, "ICMP/PING", "Echo request (64 B)", 64);
                    _bandwidth.StorePacket(pingTarget, pingGuid, localGuid, "ICMP/PONG", $"Echo reply (64 B, rtt={latency}ms)", 64);
                    _bandwidth.StorePacket(loaderUid, pingGuid, localGuid, "ICMP/PONG", $"Echo reply (64 B, rtt={latency}ms)", 64);
                    _bandwidth.LogPacketToRouters(localGuid, pingGuid, "ICMP/PING", "Echo request (64 B)", 64);
                    _bandwidth.LogPacketToRouters(pingGuid, localGuid, "ICMP/PONG", $"Echo reply (64 B, rtt={latency}ms)", 64);
                }
                else
                {
                    comp.History.Add($"ping: Host {pingGuid} unreachable (100% packet loss)");
                    _bandwidth.StorePacket(loaderUid, localGuid, pingGuid, "ICMP/PING", "Echo request (Unreachable)", 64, dropped: true);
                    _bandwidth.LogPacketToRouters(localGuid, pingGuid, "ICMP/PING", "Echo request (Unreachable)", 64, dropped: true);
                }
                break;

            case "packets":
            case "tcpdump":
            case "netlog":
                if (TryComp<NetworkBandwidthComponent>(loaderUid, out var localPktBw) && localPktBw.StoredPackets.Count > 0)
                {
                    comp.History.Add($"--- Packet Buffer on {localGuid} [{localPktBw.StoredPackets.Count} packets] ---");
                    foreach (var pkt in localPktBw.StoredPackets.TakeLast(8))
                    {
                        var status = pkt.Dropped ? " [DROPPED]" : "";
                        var timeStr = pkt.Timestamp.ToString(@"mm\:ss");
                        comp.History.Add($"[{timeStr}] {pkt.SourceAddress} -> {pkt.TargetAddress} [{pkt.Protocol}] {pkt.ByteSize}B {status}");
                    }
                }
                else
                {
                    comp.History.Add($"No packets recorded in buffer on {localGuid}.");
                }
                break;

            case "ifconfig":
            case "ip":
                TryComp<NetworkBandwidthComponent>(loaderUid, out var myBw);
                comp.History.Add($"eth0: GUID: {localGuid} Link: ONLINE");
                comp.History.Add($"      MaxBps: {myBw?.MaxBps ?? 128000} B/s | Queued: {myBw?.CurrentQueuedPackets ?? 0} | Saturated: {myBw?.IsSaturated ?? false}");
                break;

            case "ls":
                var dir = parts.Length >= 2 ? parts[1] : "/";
                if (!dir.StartsWith('/')) dir = "/" + dir;
                if (!dir.EndsWith('/')) dir += "/";

                var hasDisk = TryGetDisk(loaderUid, out _, out var diskFs, out _);

                if (dir == "/disk/")
                {
                    if (hasDisk && diskFs != null)
                    {
                        var diskSubdirs = new HashSet<string>();
                        foreach (var f in diskFs.Files.Values)
                        {
                            if (f.Directory.StartsWith('/') && f.Directory != "/")
                            {
                                var rel = f.Directory.TrimStart('/');
                                var nextSlash = rel.IndexOf('/');
                                if (nextSlash != -1)
                                    diskSubdirs.Add(rel.Substring(0, nextSlash));
                            }
                        }

                        comp.History.Add($"Disk Directory: /disk/ ({diskSubdirs.Count} dirs, {diskFs.Files.Count} files)");
                        foreach (var d in diskSubdirs.OrderBy(s => s))
                        {
                            comp.History.Add($"  [DIR]  {d}/");
                        }
                        foreach (var f in diskFs.Files.Values.OrderBy(f => f.Name))
                        {
                            var flags = f.IsReadOnly ? "[RO]" : "    ";
                            comp.History.Add($"  {flags} {f.Name}.{f.Extension,-5} {f.Size,6} B");
                        }
                    }
                    else
                    {
                        comp.History.Add("ls: /disk/: No disk inserted in drive");
                    }
                }
                else if (TryComp<FileSystemComponent>(loaderUid, out var fs))
                {
                    var subdirs = new HashSet<string>();
                    foreach (var f in fs.Files.Values)
                    {
                        if (f.Directory.StartsWith(dir) && f.Directory != dir)
                        {
                            var rel = f.Directory.Substring(dir.Length);
                            var nextSlash = rel.IndexOf('/');
                            if (nextSlash != -1)
                            {
                                subdirs.Add(rel.Substring(0, nextSlash));
                            }
                        }
                    }

                    if (dir == "/" && hasDisk)
                    {
                        subdirs.Add("disk");
                    }

                    var files = fs.Files.Values.Where(f => f.Directory == dir).ToList();
                    comp.History.Add($"Directory: {dir} ({subdirs.Count} dirs, {files.Count} files)");
                    foreach (var d in subdirs.OrderBy(s => s))
                    {
                        comp.History.Add($"  [DIR]  {d}/");
                    }
                    foreach (var f in files.OrderBy(f => f.Name))
                    {
                        var flags = f.IsReadOnly ? "[RO]" : "    ";
                        comp.History.Add($"  {flags} {f.Name}.{f.Extension,-5} {f.Size,6} B");
                    }
                }
                break;

            case "cat":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: cat <file>");
                    return;
                }
                var catTarget = parts[1];
                if (catTarget.StartsWith("/disk/"))
                {
                    var diskRel = catTarget.Substring("/disk".Length);
                    if (TryGetDisk(loaderUid, out _, out var dFs, out _) &&
                        (dFs.Files.TryGetValue(diskRel, out var df) || dFs.Files.TryGetValue(catTarget, out df)))
                    {
                        if (df is TextVirtualFile tf)
                            comp.History.Add(tf.TextContent);
                        else if (df is RecipeBlueprintFile rFile)
                        {
                            var rList = string.Join(", ", rFile.GetAllRecipes().Select(r => r.Id));
                            comp.History.Add($"Lathe Blueprint: [{rList}] Uses: {(rFile.RemainingUses?.ToString() ?? "Unlimited")} DRM: {rFile.DrmLocked}");
                        }
                        else
                            comp.History.Add($"Binary File ({df.Size} B)");
                    }
                    else
                    {
                        comp.History.Add($"cat: {catTarget}: No such file on disk");
                    }
                }
                else if (TryComp<FileSystemComponent>(loaderUid, out var catFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(loaderUid, catTarget, out var file, catFs))
                {
                    if (file is TextVirtualFile tf)
                        comp.History.Add(tf.TextContent);
                    else if (file is RecipeBlueprintFile rFile)
                    {
                        var rList = string.Join(", ", rFile.GetAllRecipes().Select(r => r.Id));
                        comp.History.Add($"Lathe Blueprint: [{rList}] Uses: {(rFile.RemainingUses?.ToString() ?? "Unlimited")} DRM: {rFile.DrmLocked}");
                    }
                    else
                        comp.History.Add($"Binary File ({file.Size} B)");
                }
                else
                {
                    comp.History.Add($"cat: {parts[1]}: No such file");
                }
                break;

            case "rm":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: rm <file>");
                    return;
                }
                if (TryComp<FileSystemComponent>(loaderUid, out var rmFs))
                {
                    if (_fileSystem.TryDeleteFile(loaderUid, parts[1], rmFs))
                        comp.History.Add($"Removed '{parts[1]}'");
                    else
                        comp.History.Add($"rm: cannot remove '{parts[1]}': Permission denied or not found");
                }
                break;

            case "keygen":
                if (TryComp<FileSystemComponent>(loaderUid, out var keyFs))
                {
                    var keyContent = $"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI{localGuid} user@{localGuid}";
                    var keyFile = new TextVirtualFile
                    {
                        Name = "id_ed25519",
                        Extension = "pub",
                        Directory = "/sys/",
                        Size = 68,
                        TextContent = keyContent,
                        IsReadOnly = false
                    };
                    _fileSystem.TryWriteFile(loaderUid, keyFile, keyFs);
                    comp.History.Add($"Your public key has been saved in /sys/id_ed25519.pub:");
                    comp.History.Add($"  {keyContent}");
                }
                break;

            case "run":
            case "exec":
            case "start":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: run <program_name_or_bin>");
                    return;
                }
                var lProgTarget = parts[1].Trim();

                if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var lLoader))
                {
                    comp.History.Add($"run: Device [{localGuid}] does not support cartridge programs.");
                    return;
                }

                var lFoundProg = FindInstalledProgram(loaderUid, lProgTarget, out var lNotInstalledProto);
                if (lFoundProg == null && lNotInstalledProto != null)
                {
                    if (_cartridgeLoader.InstallProgram((loaderUid, lLoader), lNotInstalledProto))
                    {
                        lFoundProg = _cartridgeLoader.GetAllPrograms(loaderUid)
                            .FirstOrDefault(p => MetaData(p).EntityPrototype?.ID == lNotInstalledProto);
                    }
                }

                if (lFoundProg != null)
                {
                    _cartridgeLoader.ActivateProgram((loaderUid, lLoader), lFoundProg.Value);
                    if (TryComp<PdaComponent>(loaderUid, out var lPda))
                    {
                        if (!lPda.OpenPrograms.Contains(lFoundProg.Value))
                            lPda.OpenPrograms.Add(lFoundProg.Value);
                        _pdaSystem.UpdatePdaUi(loaderUid, lPda);
                    }

                    var pName = MetaData(lFoundProg.Value).EntityName;
                    var pid = (lFoundProg.Value.GetHashCode() & 0xFFFF).ToString("X4");
                    comp.History.Add($"Process '{pName}' (instance [0x{pid}]) running on [{localGuid}].");
                }
                else
                {
                    comp.History.Add($"run: '{lProgTarget}': Executable program not found.");
                }
                break;

            default:
                comp.History.Add($"command not found: {cmd}. Type 'help' for commands.");
                break;
        }
    }

    private void AppendDeviceScan(EntityUid sourceUid, TerminalCartridgeComponent comp, string? filterArg)
    {
        _remoteScreen.EnsureAllDevicesProvisioned();
        var sourceXform = Transform(sourceUid);
        var sourceMap = sourceXform.MapID;
        var filter = filterArg?.Trim().ToUpperInvariant();

        var results = new List<(string Guid, string Category, bool HasGui, string Name)>();
        var query = EntityQueryEnumerator<DeviceGuidComponent, TransformComponent>();
        while (query.MoveNext(out var devUid, out var guidComp, out var xform))
        {
            if (xform.MapID != sourceMap || string.IsNullOrEmpty(guidComp.Guid))
                continue;

            var cat = _remoteScreen.GetDeviceCategory(devUid);
            var name = MetaData(devUid).EntityName;
            var hasGui = _remoteScreen.CanShareScreen(devUid);

            if (!string.IsNullOrEmpty(filter) &&
                !cat.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !guidComp.Guid.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add((guidComp.Guid, cat, hasGui, name));
        }

        results.Sort((a, b) =>
        {
            var catCmp = string.Compare(a.Category, b.Category, StringComparison.Ordinal);
            return catCmp != 0 ? catCmp : string.Compare(a.Guid, b.Guid, StringComparison.Ordinal);
        });

        comp.History.Add($"--- Network Device Discovery ({results.Count} nodes online) ---");
        foreach (var item in results.Take(25))
        {
            var guiTag = item.HasGui ? "[GUI]" : "[CLI]";
            comp.History.Add($"  [{item.Guid}]  {item.Category,-10} {guiTag}  {item.Name}");
        }
        if (results.Count > 25)
        {
            comp.History.Add($"  ... ({results.Count - 25} more; use 'scan <apc|alarm|computer|pda>' to filter)");
        }
    }

    private void HandleRemoteSshCommand(EntityUid uid, EntityUid loaderUid, TerminalCartridgeComponent comp, string input, EntityUid? userActor = null)
    {
        var targetGuid = comp.ConnectedRemoteGuid!;
        if (!_guidSystem.TryGetEntityByGuid(targetGuid, out var optTargetUid) || !optTargetUid.HasValue)
        {
            comp.History.Add($"ssh: Connection reset by peer {targetGuid}.");
            comp.ConnectedRemoteGuid = null;
            return;
        }
        var targetUid = optTargetUid.Value;
        _remoteScreen.EnsureDeviceProvisioned(targetUid);

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "exit":
            case "quit":
            case "logout":
                comp.IsScreenAttached = false;
                DetachRemoteScreen(userActor);
                _remoteScreen.DetachViewer(uid, userActor);

                comp.History.Add($"Connection to {targetGuid} closed.");
                _fileSystem.LogSystemActivity(loaderUid, "SSH-EXIT", $"Disconnected from {targetGuid}");
                _fileSystem.LogSystemActivity(targetUid, "SSH-EXIT", $"Session closed by {_guidSystem.GetGuid(loaderUid)}");
                comp.ConnectedRemoteGuid = null;
                comp.ConnectedRemoteUser = null;
                comp.SshLocalMode = false;
                break;

            case "clear":
                comp.History.Clear();
                break;

            case "scan":
            case "devices":
            case "arp":
                AppendDeviceScan(targetUid, comp, parts.Length > 1 ? parts[1] : null);
                break;

            case "help":
                comp.History.Add($"Remote Commands on [{targetGuid}]:");
                comp.History.Add("  ls [dir]                  - List remote directory");
                comp.History.Add("  cd [dir]                  - Change remote working directory");
                comp.History.Add("  pwd                       - Print current working directory");
                comp.History.Add("  cat <file>                - Print remote file (e.g. /sys/system.log)");
                comp.History.Add("  run / exec <program>      - Execute / launch program on remote host");
                comp.History.Add("  screen [open|close|info]  - View and use remote machine's screen/GUI");
                comp.History.Add("  switch / local            - Switch to local shell context for dirs/files");
                comp.History.Add("  upload / put <file> [dir] - Upload local file to remote host");
                comp.History.Add("  fetch / scp <file>        - Download file from remote host to local /notes/");
                comp.History.Add("  rm <file>                 - Delete remote file");
                comp.History.Add("  packets / tcpdump         - Inspect remote device packet logs");
                comp.History.Add("  top / stat                - Display remote CPU, RAM, and bandwidth metrics");
                comp.History.Add("  reboot                    - Remote reboot and flush network buffers");
                comp.History.Add("  clear                     - Clear terminal screen");
                comp.History.Add("  exit / quit               - Close SSH session");
                break;

            case "pwd":
                comp.History.Add(comp.CurrentDirectory ?? "/");
                break;

            case "cd":
                var rTargetDir = parts.Length > 1 ? parts[1].Trim() : "/";
                if (rTargetDir == "~" || rTargetDir == "")
                    rTargetDir = "/notes/";
                else if (rTargetDir == "..")
                    rTargetDir = "/";
                else if (!rTargetDir.StartsWith('/'))
                    rTargetDir = (comp.CurrentDirectory ?? "/") + rTargetDir;
                if (!rTargetDir.EndsWith('/'))
                    rTargetDir += "/";
                comp.CurrentDirectory = rTargetDir;
                comp.History.Add($"Remote directory: {comp.CurrentDirectory}");
                break;

            case "ls":
                var dir = parts.Length >= 2 ? parts[1] : "/";
                if (!dir.StartsWith('/')) dir = "/" + dir;
                if (!dir.EndsWith('/')) dir += "/";

                if (TryComp<FileSystemComponent>(targetUid, out var remFs))
                {
                    var subdirs = new HashSet<string>();
                    foreach (var f in remFs.Files.Values)
                    {
                        if (f.Directory.StartsWith(dir) && f.Directory != dir)
                        {
                            var rel = f.Directory.Substring(dir.Length);
                            var nextSlash = rel.IndexOf('/');
                            if (nextSlash != -1)
                            {
                                subdirs.Add(rel.Substring(0, nextSlash));
                            }
                        }
                    }

                    var files = remFs.Files.Values.Where(f => f.Directory == dir).ToList();
                    comp.History.Add($"Remote [{targetGuid}]: {dir} ({subdirs.Count} dirs, {files.Count} files)");
                    foreach (var d in subdirs.OrderBy(s => s))
                    {
                        comp.History.Add($"  [DIR]  {d}/");
                    }
                    foreach (var f in files.OrderBy(f => f.Name))
                    {
                        var flags = f.IsReadOnly ? "[RO]" : "    ";
                        comp.History.Add($"  {flags} {f.Name}.{f.Extension,-5} {f.Size,6} B");
                    }
                }
                break;

            case "cat":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: cat <file>");
                    return;
                }
                if (TryComp<FileSystemComponent>(targetUid, out var catFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(targetUid, parts[1], out var rFile, catFs))
                {
                    if (rFile is TextVirtualFile tf)
                        comp.History.Add(tf.TextContent);
                    else if (rFile is DatabaseVirtualFile db)
                        comp.History.Add(string.Join("\n", db.Data.Select(kv => $"{kv.Key} = {kv.Value}")));
                    else
                        comp.History.Add($"Binary File ({rFile.Size} B)");
                }
                else
                {
                    comp.History.Add($"cat: {parts[1]}: No such file");
                }
                break;

            case "rm":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: rm <file>");
                    return;
                }
                if (TryComp<FileSystemComponent>(targetUid, out var remDelFs))
                {
                    if (_fileSystem.TryDeleteFile(targetUid, parts[1], remDelFs))
                        comp.History.Add($"Remote file '{parts[1]}' removed.");
                    else
                        comp.History.Add($"rm: cannot remove '{parts[1]}': Permission denied or not found");
                }
                break;

            case "fetch":
            case "scp":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: fetch <remote_file>");
                    return;
                }
                if (TryComp<FileSystemComponent>(targetUid, out var srcFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(targetUid, parts[1], out var dlFile, srcFs))
                {
                    if (TryComp<FileSystemComponent>(loaderUid, out var destFs))
                    {
                        var content = dlFile is TextVirtualFile tf ? tf.TextContent : "DATA";
                        var localCopy = new TextVirtualFile
                        {
                            Name = dlFile.Name,
                            Extension = dlFile.Extension,
                            Directory = "/notes/",
                            Size = dlFile.Size,
                            TextContent = content,
                            IsReadOnly = false
                        };
                        if (_fileSystem.TryWriteFile(loaderUid, localCopy, destFs))
                        {
                            comp.History.Add($"Initiating encrypted transfer for '{dlFile.Name}.{dlFile.Extension}' ({dlFile.Size} B)...");
                            comp.History.Add($"[====================] 100% Transferred to local /notes/");

                            var srcGuid = CompOrNull<DeviceGuidComponent>(targetUid)?.Guid ?? targetGuid;
                            var destGuid = CompOrNull<DeviceGuidComponent>(loaderUid)?.Guid ?? $"DEV:{loaderUid}";
                            _bandwidth.StorePacket(loaderUid, srcGuid, destGuid, "TCP/FILE", $"File download: {dlFile.Name}.{dlFile.Extension} ({dlFile.Size} B)", dlFile.Size);
                            _bandwidth.StorePacket(targetUid, srcGuid, destGuid, "TCP/FILE", $"File download: {dlFile.Name}.{dlFile.Extension} ({dlFile.Size} B)", dlFile.Size);
                            _bandwidth.LogPacketToRouters(srcGuid, destGuid, "TCP/FILE", $"File download: {dlFile.Name}.{dlFile.Extension} ({dlFile.Size} B)", dlFile.Size);

                            _pdaSystem.UpdatePdaUi(loaderUid);
                        }
                        else
                        {
                            comp.History.Add($"fetch: failed to write to local storage (Disk full?)");
                        }
                    }
                }
                else
                {
                    comp.History.Add($"fetch: {parts[1]}: File not found on remote");
                }
                break;

            case "upload":
            case "push":
            case "put":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: upload <local_file> [remote_dest_dir]");
                    return;
                }
                var localSrc = parts[1];
                var remoteDestDir = parts.Length >= 3 ? parts[2] : null;

                VirtualFile? uploadSrcFile = null;
                if (TryComp<FileSystemComponent>(loaderUid, out var lFs))
                {
                    if (!_fileSystem.TryGetFile(loaderUid, localSrc, out uploadSrcFile, lFs))
                    {
                        if (!localSrc.StartsWith('/'))
                        {
                            _fileSystem.TryGetFile(loaderUid, $"/notes/{localSrc}", out uploadSrcFile, lFs);
                            if (uploadSrcFile == null)
                                _fileSystem.TryGetFile(loaderUid, $"/sys/{localSrc}", out uploadSrcFile, lFs);
                            if (uploadSrcFile == null)
                                _fileSystem.TryGetFile(loaderUid, $"/bin/{localSrc}", out uploadSrcFile, lFs);
                        }
                    }
                }

                if (uploadSrcFile == null && localSrc.StartsWith("/disk/") && TryGetDisk(loaderUid, out _, out var dFs, out _))
                {
                    var diskRel = localSrc.Substring("/disk".Length);
                    if (!dFs.Files.TryGetValue(diskRel, out uploadSrcFile))
                        dFs.Files.TryGetValue(localSrc, out uploadSrcFile);
                }

                if (uploadSrcFile == null)
                {
                    comp.History.Add($"upload: '{localSrc}': Local file not found");
                    return;
                }

                if (!TryComp<FileSystemComponent>(targetUid, out var rFs))
                {
                    comp.History.Add($"upload: Remote host [{targetGuid}] has no filesystem");
                    return;
                }

                var targetDir = remoteDestDir ?? uploadSrcFile.Directory;
                if (!targetDir.StartsWith('/')) targetDir = "/" + targetDir;
                if (!targetDir.EndsWith('/')) targetDir += "/";

                VirtualFile remoteCopy;
                if (uploadSrcFile is TextVirtualFile tfCopy)
                {
                    remoteCopy = new TextVirtualFile
                    {
                        Name = tfCopy.Name,
                        Extension = tfCopy.Extension,
                        Directory = targetDir,
                        Size = tfCopy.Size,
                        TextContent = tfCopy.TextContent,
                        IsReadOnly = false
                    };
                }
                else if (uploadSrcFile is RecipeBlueprintFile rbpCopy)
                {
                    remoteCopy = new RecipeBlueprintFile
                    {
                        Name = rbpCopy.Name,
                        Extension = rbpCopy.Extension,
                        Directory = targetDir,
                        Size = rbpCopy.Size,
                        Recipe = rbpCopy.Recipe,
                        Recipes = new(rbpCopy.Recipes),
                        RemainingUses = rbpCopy.RemainingUses,
                        DrmLocked = rbpCopy.DrmLocked,
                        IsReadOnly = false
                    };
                }
                else if (uploadSrcFile is ProgramVirtualFile pvfCopy)
                {
                    remoteCopy = new ProgramVirtualFile
                    {
                        Name = pvfCopy.Name,
                        Extension = pvfCopy.Extension,
                        Directory = targetDir,
                        Size = pvfCopy.Size,
                        ProgramId = pvfCopy.ProgramId,
                        Title = pvfCopy.Title,
                        IsReadOnly = false
                    };
                }
                else
                {
                    remoteCopy = new TextVirtualFile
                    {
                        Name = uploadSrcFile.Name,
                        Extension = uploadSrcFile.Extension,
                        Directory = targetDir,
                        Size = uploadSrcFile.Size,
                        TextContent = "DATA",
                        IsReadOnly = false
                    };
                }

                if (_fileSystem.TryWriteFile(targetUid, remoteCopy, rFs))
                {
                    comp.History.Add($"Initiating encrypted upload for '{uploadSrcFile.Name}.{uploadSrcFile.Extension}' ({uploadSrcFile.Size} B)...");
                    comp.History.Add($"[====================] 100% Uploaded to remote {remoteCopy.FullPath}");

                    var localGuidStr = _guidSystem.GetGuid(loaderUid);
                    _bandwidth.StorePacket(loaderUid, localGuidStr, targetGuid, "TCP/UPLOAD", $"Upload: {uploadSrcFile.Name}.{uploadSrcFile.Extension} ({uploadSrcFile.Size} B)", uploadSrcFile.Size);
                    _bandwidth.StorePacket(targetUid, localGuidStr, targetGuid, "TCP/UPLOAD", $"Upload: {uploadSrcFile.Name}.{uploadSrcFile.Extension} ({uploadSrcFile.Size} B)", uploadSrcFile.Size);
                    _bandwidth.LogPacketToRouters(localGuidStr, targetGuid, "TCP/UPLOAD", $"Upload: {uploadSrcFile.Name}.{uploadSrcFile.Extension} ({uploadSrcFile.Size} B)", uploadSrcFile.Size);

                    _pdaSystem.UpdatePdaUi(targetUid);
                }
                else
                {
                    comp.History.Add($"upload: Failed to write to remote host (Storage full or read-only)");
                }
                break;

            case "run":
            case "exec":
            case "start":
                if (parts.Length < 2)
                {
                    comp.History.Add("Usage: run <program_name_or_bin>");
                    return;
                }
                var progTarget = parts[1].Trim();

                if (!TryComp<CartridgeLoaderComponent>(targetUid, out var remLoader))
                {
                    comp.History.Add($"run: Remote device [{targetGuid}] does not support program execution.");
                    return;
                }

                var targetProg = FindInstalledProgram(targetUid, progTarget, out var rNotInstalledProto);
                if (targetProg == null && rNotInstalledProto != null)
                {
                    if (_cartridgeLoader.InstallProgram((targetUid, remLoader), rNotInstalledProto))
                    {
                        targetProg = _cartridgeLoader.GetAllPrograms(targetUid)
                            .FirstOrDefault(p => MetaData(p).EntityPrototype?.ID == rNotInstalledProto);
                    }
                }

                if (targetProg != null)
                {
                    _cartridgeLoader.ActivateProgram((targetUid, remLoader), targetProg.Value);
                    if (TryComp<PdaComponent>(targetUid, out var remPda))
                    {
                        if (!remPda.OpenPrograms.Contains(targetProg.Value))
                            remPda.OpenPrograms.Add(targetProg.Value);
                        _pdaSystem.UpdatePdaUi(targetUid, remPda);
                    }

                    var pName = MetaData(targetProg.Value).EntityName;
                    var pid = (targetProg.Value.GetHashCode() & 0xFFFF).ToString("X4");
                    comp.History.Add($"Process '{pName}' (instance [0x{pid}]) running on [{targetGuid}].");
                }
                else
                {
                    comp.History.Add($"run: '{progTarget}': Executable program not found on remote host.");
                }
                break;

            case "screen":
            case "display":
            case "gui":
            case "vnc":
                var sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "open";
                if (sub is "close" or "detach" or "exit" or "stop")
                {
                    var wasAttached = comp.IsScreenAttached;
                    comp.IsScreenAttached = false;
                    if (wasAttached)
                    {
                        DetachRemoteScreen(userActor);
                        _remoteScreen.DetachViewer(uid, userActor);
                        comp.History.Add($"Remote screen for [{targetGuid}] detached.");
                    }
                    else
                    {
                        comp.History.Add($"screen: No active remote screen session for [{targetGuid}].");
                    }
                    break;
                }

                if (sub is "status" or "info")
                {
                    var category = _remoteScreen.GetDeviceCategory(targetUid);
                    var activeTitle = MetaData(targetUid).EntityName;
                    if (TryComp<CartridgeLoaderComponent>(targetUid, out var cl) && cl.ActiveProgram is { } ap)
                        activeTitle = MetaData(ap).EntityName;
                    else if (HasComp<PdaComponent>(targetUid))
                        activeTitle = "Home Screen";

                    var openCount = 0;
                    if (TryComp<PdaComponent>(targetUid, out var pdaC))
                        openCount = pdaC.OpenPrograms.Count;

                    var isAttached = comp.IsScreenAttached;
                    comp.History.Add($"--- Display Server on [{targetGuid}] ({category}) ---");
                    comp.History.Add($"  Status: {(_remoteScreen.CanShareScreen(targetUid) ? "ACTIVE" : "HEADLESS")}");
                    comp.History.Add($"  Active Window: {activeTitle}");
                    if (HasComp<PdaComponent>(targetUid))
                        comp.History.Add($"  Open Tabs: {openCount}");
                    comp.History.Add($"  Attached Viewers: {(isAttached ? "1 (Local User)" : "0")}");
                    comp.History.Add($"  Resolution: 480x320 Holo Matrix (PiP Enabled)");
                    break;
                }

                if (!_remoteScreen.CanShareScreen(targetUid))
                {
                    comp.History.Add($"screen: Remote device [{targetGuid}] has no graphical screen/display interface.");
                    break;
                }

                comp.IsScreenAttached = true;
                if (userActor.HasValue)
                    AttachRemoteScreen(userActor.Value, targetUid);
                if (HasComp<PdaComponent>(targetUid))
                    _pdaSystem.UpdatePdaUi(targetUid);

                var devCategory = _remoteScreen.GetDeviceCategory(targetUid);
                var curProgName = MetaData(targetUid).EntityName;
                if (TryComp<CartridgeLoaderComponent>(targetUid, out var cLoader) && cLoader.ActiveProgram is { } actProg)
                {
                    curProgName = MetaData(actProg).EntityName;
                }
                else if (HasComp<PdaComponent>(targetUid))
                {
                    curProgName = "Home Screen";
                }

                comp.History.Add($"Connecting to remote display on [{targetGuid}] ({devCategory})...");
                comp.History.Add($"[====================] 100% Display stream established.");
                comp.History.Add($"Display: [{targetGuid}] | Foreground: {curProgName}");
                comp.History.Add("Remote screen embedded in terminal. Click 'Detach Screen' or type 'screen close' to return to shell.");
                break;

            case "packets":
            case "tcpdump":
            case "netlog":
                if (TryComp<NetworkBandwidthComponent>(targetUid, out var remPktBw) && remPktBw.StoredPackets.Count > 0)
                {
                    comp.History.Add($"--- Packet Buffer on {targetGuid} [{remPktBw.StoredPackets.Count} packets] ---");
                    foreach (var pkt in remPktBw.StoredPackets.TakeLast(8))
                    {
                        var status = pkt.Dropped ? " [DROPPED]" : "";
                        var timeStr = pkt.Timestamp.ToString(@"mm\:ss");
                        comp.History.Add($"[{timeStr}] {pkt.SourceAddress} -> {pkt.TargetAddress} [{pkt.Protocol}] {pkt.ByteSize}B {status}");
                    }
                }
                else
                {
                    comp.History.Add($"No packets recorded in buffer on {targetGuid}.");
                }
                break;

            case "top":
            case "stat":
                TryComp<ComputerStatsComponent>(targetUid, out var rStats);
                TryComp<NetworkBandwidthComponent>(targetUid, out var rBw);
                comp.History.Add($"Host: {targetGuid} | CPU: {rStats?.CpuFrequencyMhz ?? 100}MHz | RAM: {rStats?.RamMb ?? 64}QB");
                comp.History.Add($"Net Bandwidth: {rBw?.MaxBps ?? 100000} B/s | Saturated: {rBw?.IsSaturated ?? false}");
                break;

            case "reboot":
                if (TryComp<NetworkBandwidthComponent>(targetUid, out var rebBw))
                {
                    rebBw.CurrentQueuedPackets = 0;
                    rebBw.IsSaturated = false;
                    Dirty(targetUid, rebBw);
                }
                comp.History.Add($"Broadcast: System {targetGuid} rebooting. Network buffers flushed.");
                break;

            default:
                comp.History.Add($"remote: command '{cmd}' executed.");
                break;
        }
    }

    public void OnRemoteDeviceUpdated(EntityUid remoteUid)
    {
        var remoteGuid = _guidSystem.GetGuid(remoteUid);
        var query = EntityQueryEnumerator<TerminalCartridgeComponent>();
        while (query.MoveNext(out var termUid, out var termComp))
        {
            if (termComp.IsScreenAttached && termComp.ConnectedRemoteGuid == remoteGuid)
            {
                if (Transform(termUid).ParentUid is { } loaderUid && loaderUid.IsValid())
                {
                    UpdateUiState(termUid, loaderUid, termComp);
                }
            }
        }
    }

    private void HandleRemoteScreenAction(EntityUid uid, EntityUid loaderUid, TerminalCartridgeComponent comp, string action, string? arg, EntityUid? actor = null)
    {
        if (comp.ConnectedRemoteGuid == null ||
            !_guidSystem.TryGetEntityByGuid(comp.ConnectedRemoteGuid, out var optTarget) ||
            !optTarget.HasValue)
        {
            return;
        }

        var targetUid = optTarget.Value;

        if (action == "detach")
        {
            comp.IsScreenAttached = false;
            DetachRemoteScreen(actor);
            _remoteScreen.DetachViewer(uid, actor);
            comp.History.Add($"Remote display stream detached from [{comp.ConnectedRemoteGuid}].");
            return;
        }

        if (action == "tab" && !string.IsNullOrEmpty(arg) && TryComp<CartridgeLoaderComponent>(targetUid, out var cLoader))
        {
            if (NetEntity.TryParse(arg, out var netProg))
            {
                var prog = GetEntity(netProg);
                if (TryComp<PdaComponent>(targetUid, out var tabPda))
                    tabPda.CurrentView = 3;
                _cartridgeLoader.ActivateProgram(new Entity<CartridgeLoaderComponent>(targetUid, cLoader), prog);
                _pdaSystem.UpdatePdaUi(targetUid);
            }
        }
        else if (action == "close_tab" && !string.IsNullOrEmpty(arg) && TryComp<PdaComponent>(targetUid, out var pda))
        {
            if (NetEntity.TryParse(arg, out var netProg))
            {
                var prog = GetEntity(netProg);
                pda.OpenPrograms.Remove(prog);
                if (TryComp<CartridgeLoaderComponent>(targetUid, out var cLoaderClose) && cLoaderClose.ActiveProgram == prog)
                {
                    if (pda.OpenPrograms.Count > 0)
                    {
                        var nextProg = pda.OpenPrograms.Last();
                        _cartridgeLoader.ActivateProgram(new Entity<CartridgeLoaderComponent>(targetUid, cLoaderClose), nextProg);
                    }
                    else
                    {
                        _cartridgeLoader.DeactivateProgram(new Entity<CartridgeLoaderComponent>(targetUid, cLoaderClose), prog);
                        if (pda.CurrentView == 3)
                            pda.CurrentView = 1;
                    }
                }
                _pdaSystem.UpdatePdaUi(targetUid, pda);
            }
        }
        else if (action == "home" && TryComp<PdaComponent>(targetUid, out var homePda))
        {
            homePda.CurrentView = 0;
            _pdaSystem.UpdatePdaUi(targetUid, homePda);
        }
        else if (action == "programs" && TryComp<PdaComponent>(targetUid, out var progsPda))
        {
            progsPda.CurrentView = 1;
            _pdaSystem.UpdatePdaUi(targetUid, progsPda);
        }
        else if (action == "settings" && TryComp<PdaComponent>(targetUid, out var settingsPda))
        {
            settingsPda.CurrentView = 2;
            _pdaSystem.UpdatePdaUi(targetUid, settingsPda);
        }
        else if (action == "run_program" && !string.IsNullOrEmpty(arg) && TryComp<CartridgeLoaderComponent>(targetUid, out var cLoaderRun))
        {
            if (NetEntity.TryParse(arg, out var netProg))
            {
                var prog = GetEntity(netProg);
                if (TryComp<PdaComponent>(targetUid, out var runPda))
                    runPda.CurrentView = 3;
                _cartridgeLoader.ActivateProgram(new Entity<CartridgeLoaderComponent>(targetUid, cLoaderRun), prog);
                _pdaSystem.UpdatePdaUi(targetUid);
            }
        }
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, TerminalCartridgeComponent comp)
    {
        if (!_updatingTerminals.Add(uid))
            return;

        try
        {
            UpdateUiStateInternal(uid, loaderUid, comp);
        }
        finally
        {
            _updatingTerminals.Remove(uid);
        }
    }

    private void UpdateUiStateInternal(EntityUid uid, EntityUid loaderUid, TerminalCartridgeComponent comp)
    {
        while (comp.History.Count > 80)
        {
            comp.History.RemoveAt(0);
        }

        var localGuid = _guidSystem.GetGuid(loaderUid);
        var prompt = comp.ConnectedRemoteGuid != null
            ? (comp.SshLocalMode
                ? $"[LOCAL] [{localGuid}]$ "
                : $"{comp.ConnectedRemoteUser ?? "admin"}@{comp.ConnectedRemoteGuid}:~$ ")
            : (comp.AwaitingPassword ? "Password: " : $"[{localGuid}]$ ");

        RemoteScreenState? remoteScreen = null;
        Content.Shared.PDA.PdaUpdateState? remotePdaState = null;

        if (comp.IsScreenAttached && comp.ConnectedRemoteGuid != null &&
            _guidSystem.TryGetEntityByGuid(comp.ConnectedRemoteGuid, out var optTarget) &&
            optTarget.HasValue)
        {
            var visitedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { localGuid };
            var chainTargets = new HashSet<EntityUid>();
            var chainPvs = new HashSet<EntityUid>();

            remoteScreen = _remoteScreen.BuildRemoteScreenState(
                optTarget.Value,
                visitedGuids,
                chainTargets,
                chainPvs);

            remotePdaState = remoteScreen?.PdaState;

            EntityUid? viewerActor = null;
            if (_terminalViewers.TryGetValue(uid, out var recordedActor) && Exists(recordedActor))
                viewerActor = recordedActor;
            else if (Transform(loaderUid).ParentUid is { } parentUid && HasComp<ActorComponent>(parentUid))
                viewerActor = parentUid;
            else if (TryComp<UserInterfaceComponent>(loaderUid, out var loaderUi))
            {
                foreach (var actorSet in loaderUi.Actors.Values)
                {
                    foreach (var actor in actorSet)
                    {
                        if (Exists(actor))
                        {
                            viewerActor = actor;
                            break;
                        }
                    }
                    if (viewerActor != null)
                        break;
                }
            }

            if (viewerActor != null)
                _terminalViewers[uid] = viewerActor.Value;

            _remoteScreen.SyncViewerChain(uid, viewerActor, chainTargets, chainPvs);
        }
        else
        {
            _remoteScreen.DetachViewer(uid);
        }

        var state = new TerminalUiState(
            comp.History,
            prompt,
            localGuid,
            comp.ConnectedRemoteGuid,
            comp.AwaitingPassword,
            comp.SshLocalMode,
            comp.IsScreenAttached,
            remotePdaState,
            remoteScreen);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
