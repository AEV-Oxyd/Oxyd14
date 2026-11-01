using System.Linq;
using Content.Shared._Oxyd.Computers.Cartridges;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Servers;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Computers.Cartridges;

public sealed partial class TerminalCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private SharedDeviceGuidSystem _guidSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Content.Server.PDA.PdaSystem _pdaSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TerminalCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<TerminalCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    private void OnUiReady(EntityUid uid, TerminalCartridgeComponent comp, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, comp);
    }

    private void OnUiMessage(EntityUid uid, TerminalCartridgeComponent comp, CartridgeMessageEvent args)
    {
        if (args is not TerminalUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        var input = msg.CommandText.Trim();

        if (string.IsNullOrEmpty(input))
        {
            UpdateUiState(uid, loaderUid, comp);
            return;
        }

        var localGuid = _guidSystem.GetGuid(loaderUid);
        var currentPrompt = comp.ConnectedRemoteGuid != null
            ? $"{comp.ConnectedRemoteUser ?? "admin"}@{comp.ConnectedRemoteGuid}:~$ "
            : (comp.AwaitingPassword ? "Password: " : $"[{localGuid}]$ ");

        if (comp.AwaitingPassword)
        {
            comp.History.Add($"Password: ********");
            HandlePasswordAuth(loaderUid, comp, input);
            UpdateUiState(uid, loaderUid, comp);
            return;
        }

        comp.History.Add($"{currentPrompt}{input}");

        if (comp.ConnectedRemoteGuid != null)
        {
            HandleRemoteSshCommand(loaderUid, comp, input);
        }
        else
        {
            HandleLocalCommand(loaderUid, comp, input);
        }

        UpdateUiState(uid, loaderUid, comp);
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

        if (valid)
        {
            comp.ConnectedRemoteGuid = targetGuid;
            comp.ConnectedRemoteUser = comp.PendingSshUser;
            comp.History.Add($"Authentication successful. Connection established.");
            comp.History.Add($"Last login: {DateTime.Now:yyyy-MM-dd HH:mm:ss} from {_guidSystem.GetGuid(loaderUid)}");
            comp.History.Add($"Type 'help' for remote commands or 'exit' to disconnect.");
        }
        else
        {
            comp.History.Add("Permission denied (publickey, password).");
        }
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
                comp.History.Add("  ssh <guid> [user]     - Connect to remote host via SSH key/auth");
                comp.History.Add("  ping <guid>           - Test network connectivity to target device");
                comp.History.Add("  ifconfig / ip         - Display local network configuration");
                comp.History.Add("  ls [dir]              - List files in local directory");
                comp.History.Add("  cat <file>            - Print content of local text file");
                comp.History.Add("  rm <file>             - Delete a local file");
                comp.History.Add("  keygen                - Generate local ed25519 SSH public key");
                comp.History.Add("  clear                 - Clear terminal screen");
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
                }
                else
                {
                    comp.History.Add($"ping: Host {pingGuid} unreachable (100% packet loss)");
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

                if (TryComp<FileSystemComponent>(loaderUid, out var fs))
                {
                    var files = fs.Files.Values.Where(f => f.Directory == dir).ToList();
                    comp.History.Add($"Directory: {dir} ({files.Count} files)");
                    foreach (var f in files)
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
                if (TryComp<FileSystemComponent>(loaderUid, out var catFs) &&
                    _fileSystem.TryGetFile<VirtualFile>(loaderUid, parts[1], out var file, catFs))
                {
                    if (file is TextVirtualFile tf)
                        comp.History.Add(tf.TextContent);
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

            default:
                comp.History.Add($"command not found: {cmd}. Type 'help' for commands.");
                break;
        }
    }

    private void HandleRemoteSshCommand(EntityUid loaderUid, TerminalCartridgeComponent comp, string input)
    {
        var targetGuid = comp.ConnectedRemoteGuid!;
        if (!_guidSystem.TryGetEntityByGuid(targetGuid, out var optTargetUid) || !optTargetUid.HasValue)
        {
            comp.History.Add($"ssh: Connection reset by peer {targetGuid}.");
            comp.ConnectedRemoteGuid = null;
            return;
        }
        var targetUid = optTargetUid.Value;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "exit":
            case "logout":
                comp.History.Add($"Connection to {targetGuid} closed.");
                comp.ConnectedRemoteGuid = null;
                comp.ConnectedRemoteUser = null;
                break;

            case "help":
                comp.History.Add($"Remote Commands on [{targetGuid}]:");
                comp.History.Add("  ls [dir]          - List remote directory");
                comp.History.Add("  cat <file>        - Print remote file");
                comp.History.Add("  rm <file>         - Delete remote file");
                comp.History.Add("  fetch <file>      - Download file from remote host to local /notes/");
                comp.History.Add("  top / stat        - Display remote resource usage");
                comp.History.Add("  reboot            - Remote reboot and flush network buffers");
                comp.History.Add("  exit              - Close SSH session");
                break;

            case "ls":
                var dir = parts.Length >= 2 ? parts[1] : "/";
                if (!dir.StartsWith('/')) dir = "/" + dir;
                if (!dir.EndsWith('/')) dir += "/";

                if (TryComp<FileSystemComponent>(targetUid, out var remFs))
                {
                    var files = remFs.Files.Values.Where(f => f.Directory == dir).ToList();
                    comp.History.Add($"Remote [{targetGuid}]: {dir} ({files.Count} files)");
                    foreach (var f in files)
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

            case "top":
            case "stat":
                TryComp<ComputerStatsComponent>(targetUid, out var rStats);
                TryComp<NetworkBandwidthComponent>(targetUid, out var rBw);
                comp.History.Add($"Host: {targetGuid} | CPU: {rStats?.CpuFrequencyMhz ?? 100}MHz | RAM: {rStats?.RamMb ?? 64}MB");
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

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, TerminalCartridgeComponent comp)
    {
        while (comp.History.Count > 80)
        {
            comp.History.RemoveAt(0);
        }

        var localGuid = _guidSystem.GetGuid(loaderUid);
        var prompt = comp.ConnectedRemoteGuid != null
            ? $"{comp.ConnectedRemoteUser ?? "admin"}@{comp.ConnectedRemoteGuid}:~$ "
            : (comp.AwaitingPassword ? "Password: " : $"[{localGuid}]$ ");

        var state = new TerminalUiState(
            comp.History,
            prompt,
            localGuid,
            comp.ConnectedRemoteGuid,
            comp.AwaitingPassword);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
