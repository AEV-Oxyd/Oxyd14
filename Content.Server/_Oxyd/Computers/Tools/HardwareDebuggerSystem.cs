using System.Linq;
using Content.Server._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.FileSystem;
using Content.Shared._Oxyd.Computers.Hardware;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared._Oxyd.Computers.Tools;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Wires;
using Robust.Server.GameStates;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server._Oxyd.Computers.Tools;

/// <summary>
/// Implements local terminal access to any computer equipped with a <see cref="ComputerDataPortComponent"/>.
/// Allows low-level circuit, DeviceLink port, hardware POST, process, and filesystem debugging
/// without requiring a network interface card or booted operating system.
/// </summary>
public sealed partial class HardwareDebuggerSystem : EntitySystem
{
    [Dependency] private OxydComputerSystem _computer = default!;
    [Dependency] private SharedFileSystemSystem _fileSystem = default!;
    [Dependency] private NetworkRouterSystem _router = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<HardwareDebuggerComponent>(HardwareDebuggerUiKey.Key, subs =>
        {
            subs.Event<HardwareDebuggerCommandMessage>(OnCommand);
            subs.Event<HardwareDebuggerDisconnectMessage>(OnDisconnect);
        });
    }

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<HardwareDebuggerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = true;
        var compUid = _router.UnwrapComputerEntity(target);
        if (!TryComp<OxydComputerComponent>(compUid, out var oxyd))
        {
            _popup.PopupEntity("No computer circuit detected on target.", target, args.User);
            return;
        }

        if (!TryComp<ComputerDataPortComponent>(compUid, out var dataPort) || !dataPort.Enabled)
        {
            _popup.PopupEntity("Target computer has no accessible data port.", target, args.User);
            return;
        }

        if (dataPort.RequireMaintenancePanelOpen &&
            TryComp<WiresPanelComponent>(target, out var wiresPanel) &&
            !wiresPanel.Open)
        {
            _popup.PopupEntity("Open the maintenance panel to access the data port.", target, args.User);
            return;
        }

        ent.Comp.ConnectedComputer = compUid;
        ent.Comp.CurrentDirectory = "/";
        ent.Comp.History.Add($"--- Connected to {MetaData(compUid).EntityName} [{_router.EnsureDeviceGuid(compUid)}] via {dataPort.PortLabel} ---");
        Dirty(ent);

        if (TryComp<ActorComponent>(args.User, out var actor))
        {
            _pvsOverride.AddSessionOverride(compUid, actor.PlayerSession);
            if (_fileSystem.TryFindFileSystem(compUid, out var dUid, out _))
                _pvsOverride.AddSessionOverride(dUid.Value, actor.PlayerSession);
        }

        _ui.OpenUi(ent.Owner, HardwareDebuggerUiKey.Key, args.User);
        UpdateDebuggerUi(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnUiClosed(Entity<HardwareDebuggerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!HardwareDebuggerUiKey.Key.Equals(args.UiKey))
            return;

        if (TryComp<ActorComponent>(args.Actor, out var actor) && ent.Comp.ConnectedComputer is { } compUid)
        {
            _pvsOverride.RemoveSessionOverride(compUid, actor.PlayerSession);
            if (_fileSystem.TryFindFileSystem(compUid, out var dUid, out _))
                _pvsOverride.RemoveSessionOverride(dUid.Value, actor.PlayerSession);
        }
    }

    private void OnDisconnect(Entity<HardwareDebuggerComponent> ent, ref HardwareDebuggerDisconnectMessage args)
    {
        ent.Comp.ConnectedComputer = null;
        ent.Comp.History.Add("Disconnected from data port.");
        Dirty(ent);
        UpdateDebuggerUi(ent.Owner, ent.Comp);
    }

    private void OnCommand(Entity<HardwareDebuggerComponent> ent, ref HardwareDebuggerCommandMessage args)
    {
        var input = args.Command.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        if (ent.Comp.ConnectedComputer is not { } compUid || !Exists(compUid) || !TryComp<OxydComputerComponent>(compUid, out var comp))
        {
            ent.Comp.History.Add("ERROR: No target computer attached to data port.");
            UpdateDebuggerUi(ent.Owner, ent.Comp);
            return;
        }

        if (!TryComp<ComputerDataPortComponent>(compUid, out var dp) || !dp.Enabled)
        {
            ent.Comp.History.Add("ERROR: Data port physically disconnected or disabled.");
            ent.Comp.ConnectedComputer = null;
            UpdateDebuggerUi(ent.Owner, ent.Comp);
            return;
        }

        var physicalTarget = comp.HostMachine is { Valid: true } hm && Exists(hm) ? hm : compUid;
        if (Exists(args.Actor) && !_interaction.InRangeUnobstructed(args.Actor, physicalTarget))
        {
            ent.Comp.History.Add("ERROR: Out of physical cable range of target data port.");
            ent.Comp.ConnectedComputer = null;
            UpdateDebuggerUi(ent.Owner, ent.Comp);
            return;
        }

        if (dp.RequireMaintenancePanelOpen &&
            TryComp<WiresPanelComponent>(physicalTarget, out var panel) &&
            !panel.Open)
        {
            ent.Comp.History.Add("ERROR: Target maintenance panel is closed.");
            ent.Comp.ConnectedComputer = null;
            UpdateDebuggerUi(ent.Owner, ent.Comp);
            return;
        }

        ent.Comp.History.Add($"{dp.PortLabel}> {input}");

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "help":
                ent.Comp.History.Add("Data Port Debugger Commands:");
                ent.Comp.History.Add("  hw / post              - Inspect CPU, RAM, HDD, NIC, DataPort, Wires");
                ent.Comp.History.Add("  devlink list           - Inspect all mirrored DeviceLink ports");
                ent.Comp.History.Add("  devlink trigger <port> - Trigger host/computer DeviceLink port");
                ent.Comp.History.Add("  devlink send <p> <c>   - Send DeviceLink command payload");
                ent.Comp.History.Add("  devlink rebind         - Rebind host <-> computer DeviceLink bridge");
                ent.Comp.History.Add("  ls [dir] / cat <path>  - Inspect local hard drive files");
                ent.Comp.History.Add("  write <path> <text>    - Write/repair file on attached hard drive");
                ent.Comp.History.Add("  rm <path>              - Delete file from attached hard drive");
                ent.Comp.History.Add("  ps / kill <pid>        - Inspect or terminate running program entities");
                ent.Comp.History.Add("  run <file.nte>         - Execute .nte program on attached computer");
                ent.Comp.History.Add("  boot / reboot          - Force hardware re-POST and reboot first .nte");
                ent.Comp.History.Add("  clear / exit           - Clear screen or detach data port");
                break;

            case "clear":
                ent.Comp.History.Clear();
                break;

            case "exit":
            case "disconnect":
                ent.Comp.ConnectedComputer = null;
                ent.Comp.History.Add("Data port link closed.");
                break;

            case "hw":
            case "post":
                AppendHardwareReport(compUid, comp, ent.Comp.History);
                break;

            case "ps":
                ent.Comp.History.Add($"Running Program Entities ({comp.RunningPrograms.Count}):");
                foreach (var pUid in comp.RunningPrograms)
                {
                    if (!TryComp<ProgramComponent>(pUid, out var p))
                        continue;
                    var bootTag = p.IsBootProgram ? " [BOOT]" : string.Empty;
                    ent.Comp.History.Add($"  PID {pUid.Id}: {p.Title} ({p.ExecutablePath}) RAM={p.RamUsageMb}QB{bootTag}");
                }
                break;

            case "kill" when parts.Length > 1:
                if (int.TryParse(parts[1], out var pid))
                {
                    var targetProg = comp.RunningPrograms.FirstOrDefault(p => p.Id == pid);
                    if (targetProg.IsValid() && _computer.CloseProgram(compUid, targetProg, comp))
                        ent.Comp.History.Add($"Terminated PID {pid}.");
                    else
                        ent.Comp.History.Add($"Cannot kill PID {pid} (missing or protected boot process).");
                }
                break;

            case "run" when parts.Length > 1:
                if (_computer.TryLaunchExecutable(compUid, parts[1], out var launched, out var runErr, args.Actor, comp))
                    ent.Comp.History.Add($"Launched '{parts[1]}' as PID {launched.Value.Id}.");
                else
                    ent.Comp.History.Add($"run failed: {runErr}");
                break;

            case "boot":
            case "reboot":
                _computer.RebootComputer(compUid, comp);
                ent.Comp.History.Add(comp.IsBooted
                    ? $"Reboot complete: running {comp.BootProgram}"
                    : $"POST FAILURE: {comp.BootError}");
                break;

            case "ls":
                if (!_fileSystem.TryFindFileSystem(compUid, out _, out var lsFs))
                {
                    ent.Comp.History.Add("No hard drive installed.");
                    break;
                }
                foreach (var (fPath, vFile) in lsFs.Files)
                    ent.Comp.History.Add($"  {fPath} ({ComputerSizeFormat.FormatStorageQb(vFile.Size)}){(vFile.IsReadOnly ? " [RO]" : "")}");
                break;

            case "cat" when parts.Length > 1:
                var catPath = SharedFileSystemSystem.NormalizePath(parts[1]);
                if (!_fileSystem.TryFindFileSystem(compUid, out _, out var catFs) || !catFs.Files.TryGetValue(catPath, out var catFile))
                {
                    ent.Comp.History.Add($"cat: '{catPath}' not found.");
                    break;
                }
                if (catFile is TextVirtualFile tf)
                    ent.Comp.History.Add(tf.TextContent);
                else if (catFile is ProgramVirtualFile pf)
                    ent.Comp.History.Add($"[APP] proto={pf.ProgramId} title=\"{pf.Title}\" size={ComputerSizeFormat.FormatStorageQb(pf.Size)} ram={pf.RamUsageMb}QB");
                else
                    ent.Comp.History.Add($"[{catFile.FileTypeTag}] {catFile.Name}.{catFile.Extension} ({ComputerSizeFormat.FormatStorageQb(catFile.Size)})");
                break;

            case "write" when parts.Length >= 3:
                if (_fileSystem.TryFindFileSystem(compUid, out var wrDrive, out var wrFs))
                {
                    var p = SharedFileSystemSystem.NormalizePath(parts[1]);
                    var idx = p.LastIndexOf('/');
                    var dir = idx > 0 ? p[..(idx + 1)] : "/";
                    var fname = p[(idx + 1)..];
                    var extIdx = fname.LastIndexOf('.');
                    var baseName = extIdx > 0 ? fname[..extIdx] : fname;
                    var ext = extIdx > 0 ? fname[(extIdx + 1)..] : "txt";
                    var content = string.Join(" ", parts.Skip(2));
                    var written = _fileSystem.TryWriteFile(wrDrive.Value, new TextVirtualFile
                    {
                        Name = baseName,
                        Extension = ext,
                        Directory = dir,
                        TextContent = content,
                        Size = Math.Max(64, content.Length)
                    }, wrFs);
                    ent.Comp.History.Add(written ? $"Wrote {p}" : "Write failed (read-only or full).");
                }
                break;

            case "rm" when parts.Length > 1:
                var rmPath = SharedFileSystemSystem.NormalizePath(parts[1]);
                if (_fileSystem.TryFindFileSystem(compUid, out var rmDrive, out var rmFs) &&
                    _fileSystem.TryDeleteFile(rmDrive.Value, rmPath, rmFs))
                    ent.Comp.History.Add($"Removed '{rmPath}'.");
                else
                    ent.Comp.History.Add("Delete failed (file missing, protected, or no drive).");
                break;

            default:
                if (_computer.ExecuteComputerCommand(compUid, input, out var cmdOut, args.Actor))
                    ent.Comp.History.Add(cmdOut);
                else
                    ent.Comp.History.Add($"Unknown debugger command '{cmd}'. Type 'help'.");
                break;
        }

        UpdateDebuggerUi(ent.Owner, ent.Comp);
    }

    private void AppendHardwareReport(EntityUid compUid, OxydComputerComponent comp, List<string> history)
    {
        var stats = CompOrNull<ComputerStatsComponent>(compUid);
        if (stats == null)
        {
            history.Add("Hardware stats unavailable.");
            return;
        }

        history.Add($"POST Status: {(comp.IsBooted ? "OK (BOOTED)" : $"FAIL ({comp.BootError})")}");
        history.Add($"CPU: {(stats.HasCpu ? $"{stats.CpuFrequencyMhz} MHz (max {stats.MaxConcurrentPrograms} procs)" : "MISSING")}");
        history.Add($"RAM: {(stats.HasRam ? $"{stats.UsedRamMb}/{stats.RamMb} QB" : "MISSING")}");
        history.Add($"HDD: {(stats.HasHardDrive ? $"{ComputerSizeFormat.FormatStorageQb(stats.UsedStorageBytes)} / {ComputerSizeFormat.FormatStorageQb(stats.StorageBytes)} ({MetaData(stats.PrimaryDriveUid!.Value).EntityName})" : "MISSING")}");
        history.Add($"NIC: {(stats.HasNetworkCard ? $"DL={ComputerSizeFormat.FormatRateQb(stats.NetworkBandwidthBps)} UL={ComputerSizeFormat.FormatRateQb(stats.NetworkUploadBandwidthBps)}" : "NONE/OFFLINE")}");
        history.Add($"Wires: PWR={(comp.PowerWireCut ? "CUT" : "OK")} NET={(comp.NetworkWireCut ? "CUT" : "OK")} STO={(comp.StorageWireCut ? "CUT" : "OK")} DVL={(comp.DeviceLinkWireCut ? "CUT" : "OK")} REM={(comp.RemoteControlWireCut ? "CUT" : "OK")}");
    }

    public void UpdateDebuggerUi(EntityUid dbgUid, HardwareDebuggerComponent comp)
    {
        while (comp.History.Count > 100)
            comp.History.RemoveAt(0);

        var connected = comp.ConnectedComputer is { } compUid && Exists(compUid) && TryComp<OxydComputerComponent>(compUid, out var oxyd);
        if (!connected)
        {
            _ui.SetUiState(dbgUid, HardwareDebuggerUiKey.Key, new HardwareDebuggerUiState(
                false,
                "Disconnected",
                "---",
                "NO-LINK",
                "---",
                false,
                string.Empty,
                "Interact with any machine/device with a Data Port to inspect circuits.",
                new(),
                comp.History,
                "jtag> "));
            return;
        }

        var cUid = comp.ConnectedComputer!.Value;
        var oxydComp = Comp<OxydComputerComponent>(cUid);
        var stats = CompOrNull<ComputerStatsComponent>(cUid);
        var dp = CompOrNull<ComputerDataPortComponent>(cUid);
        var hostName = oxydComp.HostMachine is { } hm && Exists(hm) ? MetaData(hm).EntityName : MetaData(cUid).EntityName;

        var hwLine = stats != null
            ? $"CPU: {(stats.HasCpu ? $"{stats.CpuFrequencyMhz}MHz" : "NONE")} | RAM: {stats.UsedRamMb}/{stats.RamMb} QB | HDD: {(stats.HasHardDrive ? ComputerSizeFormat.FormatStorageQb(stats.StorageBytes) : "NONE")} | NIC: {(stats.HasNetworkCard ? "UP" : "NONE")}"
            : "No stats";

        var state = new HardwareDebuggerUiState(
            true,
            MetaData(cUid).EntityName,
            _router.EnsureDeviceGuid(cUid),
            dp?.PortLabel ?? "JTAG-0",
            hostName,
            oxydComp.IsBooted,
            oxydComp.BootError,
            hwLine,
            _computer.GetMirroredPorts(cUid, oxydComp),
            new List<string>(comp.History),
            $"{dp?.PortLabel ?? "JTAG"}@{_router.EnsureDeviceGuid(cUid)}> ");

        _ui.SetUiState(dbgUid, HardwareDebuggerUiKey.Key, state);
    }
}
