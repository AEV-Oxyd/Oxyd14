using System.Numerics;
using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Computers;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.Client._Oxyd.Computers.Cartridges;

// ============================================================================
// 1. OS Cartridge UI (System Settings, App Launcher, Hardware & DeviceLink)
// ============================================================================

public sealed partial class OsUi : UIFragment
{
    private OsUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OsUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OsUiState osState)
            _fragment?.UpdateState(osState);
    }
}

public sealed class OsUiFragment : BoxContainer
{
    private readonly Action<OsUiMessageEvent> _send;
    private readonly Label _headerLabel = new();
    private readonly Label _metricsLabel = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };
    private readonly Button _remoteToggleBtn = new();
    private readonly Button _scanLinksBtn = new() { Text = "Scan & Link Device Ports" };
    private readonly LineEdit _cmdInput = new() { PlaceHolder = "Run computer/DeviceLink command (e.g. devlink scan, lathe status)...", HorizontalExpand = true };
    private readonly BoxContainer _appsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _hardwareBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _linksBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    public OsUiFragment(Action<OsUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var topBar = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true
        };
        _headerLabel.HorizontalExpand = true;
        topBar.AddChild(_headerLabel);

        _remoteToggleBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ToggleRemoteControl));
        topBar.AddChild(_remoteToggleBtn);

        _scanLinksBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.ScanAndLinkDevices));
        topBar.AddChild(_scanLinksBtn);

        AddChild(topBar);
        AddChild(_metricsLabel);
        AddChild(_statusLabel);

        var cmdRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 4)
        };
        var runBtn = new Button { Text = "Execute" };
        runBtn.OnPressed += _ =>
        {
            if (!string.IsNullOrWhiteSpace(_cmdInput.Text))
            {
                _send(new OsUiMessageEvent(OsUiAction.RunCommand, command: _cmdInput.Text));
                _cmdInput.Clear();
            }
        };
        _cmdInput.OnTextEntered += _ =>
        {
            if (!string.IsNullOrWhiteSpace(_cmdInput.Text))
            {
                _send(new OsUiMessageEvent(OsUiAction.RunCommand, command: _cmdInput.Text));
                _cmdInput.Clear();
            }
        };
        cmdRow.AddChild(_cmdInput);
        cmdRow.AddChild(runBtn);
        AddChild(cmdRow);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        var content = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true
        };

        content.AddChild(new Label { Text = "=== Installed Applications ===", FontColorOverride = Color.Cyan });
        content.AddChild(_appsBox);
        content.AddChild(new Label { Text = "=== Hardware Modules ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        content.AddChild(_hardwareBox);
        content.AddChild(new Label { Text = "=== Linked Device Ports ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        content.AddChild(_linksBox);

        scroll.AddChild(content);
        AddChild(scroll);
    }

    public void UpdateState(OsUiState state)
    {
        _headerLabel.Text = $"NTOS System Shell | GUID: {state.DeviceGuid} | Mode: {state.Template}";
        var batText = state.HasBattery ? $" | BAT: {state.BatteryPercent:F0}%" : "";
        _metricsLabel.Text = $"CPU: {state.CpuLoadPercent}% | RAM: {state.RamUsedMB}/{state.RamTotalMB} MB | DISK: {state.DiskUsedKB}/{state.DiskTotalKB} KB | NET DL/UL: {state.DownloadBps}/{state.UploadBps} B/s{batText}";
        _remoteToggleBtn.Text = state.AllowRemoteControl ? "Remote: ALLOWED" : "Remote: BLOCKED";
        _statusLabel.Text = state.StatusMessage;

        _appsBox.RemoveAllChildren();
        foreach (var prog in state.Programs)
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            var nameLbl = new Label
            {
                Text = $"{prog.Name} {(prog.IsBootProgram ? "[OS / BOOT]" : prog.IsRunning ? "[RUNNING]" : "")}",
                HorizontalExpand = true
            };
            row.AddChild(nameLbl);

            var openBtn = new Button { Text = "Open" };
            var targetUid = prog.Uid;
            openBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.LaunchProgram, targetUid));
            row.AddChild(openBtn);

            if (prog.IsRunning && !prog.IsBootProgram)
            {
                var closeBtn = new Button { Text = "Stop" };
                closeBtn.OnPressed += _ => _send(new OsUiMessageEvent(OsUiAction.CloseProgram, targetUid));
                row.AddChild(closeBtn);
            }

            _appsBox.AddChild(row);
        }

        _hardwareBox.RemoveAllChildren();
        if (state.Hardware.Count == 0)
        {
            _hardwareBox.AddChild(new Label { Text = "Integrated System Board" });
        }
        else
        {
            foreach (var hw in state.Hardware)
            {
                _hardwareBox.AddChild(new Label { Text = $"[{hw.SlotName}] {hw.PartName} ({hw.Size}) - {hw.Details}" });
            }
        }

        _linksBox.RemoveAllChildren();
        if (state.LinkedPorts.Count == 0)
        {
            _linksBox.AddChild(new Label { Text = "No local DeviceLink ports connected." });
        }
        else
        {
            foreach (var link in state.LinkedPorts)
            {
                _linksBox.AddChild(new Label { Text = $"{link.SourcePort} -> {link.TargetEntityName} ({link.SinkPort})" });
            }
        }
    }
}

// ============================================================================
// 2. Router Management UI (Connections, Blacklist, Packet Logs, DNS)
// ============================================================================

public sealed partial class RouterUi : UIFragment
{
    private RouterUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new RouterUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is RouterUiState routerState)
            _fragment?.UpdateState(routerState);
    }
}

public sealed class RouterUiFragment : BoxContainer
{
    private readonly Action<RouterUiMessageEvent> _send;
    private readonly Label _titleLabel = new();
    private readonly LineEdit _blacklistInput = new() { PlaceHolder = "Enter Device GUID to Blacklist...", HorizontalExpand = true };
    private readonly BoxContainer _connectionsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _blacklistBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _packetsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly BoxContainer _devicesBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    public RouterUiFragment(Action<RouterUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _titleLabel.HorizontalExpand = true;
        header.AddChild(_titleLabel);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.Refresh));
        header.AddChild(refreshBtn);

        var clearBtn = new Button { Text = "Clear Logs" };
        clearBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.ClearLogs));
        header.AddChild(clearBtn);
        AddChild(header);

        var blRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4) };
        var blBtn = new Button { Text = "Blacklist GUID & Sever Connections" };
        blBtn.OnPressed += _ =>
        {
            if (!string.IsNullOrWhiteSpace(_blacklistInput.Text))
            {
                _send(new RouterUiMessageEvent(RouterUiAction.BlacklistGuid, _blacklistInput.Text));
                _blacklistInput.Clear();
            }
        };
        blRow.AddChild(_blacklistInput);
        blRow.AddChild(blBtn);
        AddChild(blRow);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var body = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

        body.AddChild(new Label { Text = "=== Active Connections (Downloads / Uploads / SSH / Screen Shares) ===", FontColorOverride = Color.Cyan });
        body.AddChild(_connectionsBox);

        body.AddChild(new Label { Text = "=== Blacklisted GUIDs ===", FontColorOverride = Color.Red, Margin = new Thickness(0, 6, 0, 0) });
        body.AddChild(_blacklistBox);

        body.AddChild(new Label { Text = "=== Connected Devices & DNS ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        body.AddChild(_devicesBox);

        body.AddChild(new Label { Text = "=== Recorded Network Packets ===", FontColorOverride = Color.Cyan, Margin = new Thickness(0, 6, 0, 0) });
        body.AddChild(_packetsBox);

        scroll.AddChild(body);
        AddChild(scroll);
    }

    public void UpdateState(RouterUiState state)
    {
        _titleLabel.Text = $"Station Network Router | GUID: {state.RouterGuid}";

        _connectionsBox.RemoveAllChildren();
        if (state.ActiveConnections.Count == 0)
        {
            _connectionsBox.AddChild(new Label { Text = "No active connections." });
        }
        else
        {
            foreach (var conn in state.ActiveConnections)
            {
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
                row.AddChild(new Label
                {
                    Text = $"[{conn.ConnectionType}] {conn.SourceGuid} -> {conn.TargetGuid} ({conn.Description})",
                    HorizontalExpand = true
                });

                var closeBtn = new Button { Text = "Close Connection" };
                var connId = conn.ConnectionId;
                closeBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.CloseConnection, connId));
                row.AddChild(closeBtn);

                var blSourceBtn = new Button { Text = $"Blacklist {conn.SourceGuid}" };
                var srcGuid = conn.SourceGuid;
                blSourceBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.BlacklistGuid, srcGuid));
                row.AddChild(blSourceBtn);

                _connectionsBox.AddChild(row);
            }
        }

        _blacklistBox.RemoveAllChildren();
        if (state.BlacklistedGuids.Count == 0)
        {
            _blacklistBox.AddChild(new Label { Text = "No blacklisted GUIDs." });
        }
        else
        {
            foreach (var guid in state.BlacklistedGuids)
            {
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
                row.AddChild(new Label { Text = $"BLOCKED: {guid}", HorizontalExpand = true });
                var unblBtn = new Button { Text = "Unblock" };
                var g = guid;
                unblBtn.OnPressed += _ => _send(new RouterUiMessageEvent(RouterUiAction.UnblacklistGuid, g));
                row.AddChild(unblBtn);
                _blacklistBox.AddChild(row);
            }
        }

        _devicesBox.RemoveAllChildren();
        foreach (var (dnsName, dnsGuid) in state.DnsRecords)
        {
            _devicesBox.AddChild(new Label { Text = $"DNS '{dnsName}' -> {dnsGuid}", FontColorOverride = Color.LightGreen });
        }
        _devicesBox.AddChild(new Label { Text = $"Peers ({state.ConnectedDevices.Count}): {string.Join(", ", state.ConnectedDevices)}" });

        _packetsBox.RemoveAllChildren();
        for (var i = state.PacketLogs.Count - 1; i >= 0; i--)
        {
            var pkt = state.PacketLogs[i];
            _packetsBox.AddChild(new Label
            {
                Text = $"#{pkt.PacketId} [{pkt.PacketType}] {pkt.SenderGuid} -> {pkt.TargetGuid}: {pkt.DecipheredSummary}",
                FontColorOverride = pkt.IsEncrypted ? Color.Orange : Color.White
            });
        }
    }
}

// ============================================================================
// 3. Packet Sniffer & Re-emitter UI
// ============================================================================

public sealed partial class PacketSnifferUi : UIFragment
{
    private PacketSnifferUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new PacketSnifferUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PacketSnifferUiState snifferState)
            _fragment?.UpdateState(snifferState);
    }
}

public sealed class PacketSnifferUiFragment : BoxContainer
{
    private readonly Action<PacketSnifferUiMessageEvent> _send;
    private readonly Button _toggleBtn = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };
    private readonly LineEdit _targetGuidInput = new() { PlaceHolder = "Override Target GUID (optional)", HorizontalExpand = true };
    private readonly LineEdit _payloadInput = new() { PlaceHolder = "Modified payload for unencrypted packet re-emission...", HorizontalExpand = true };
    private readonly BoxContainer _packetList = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    public PacketSnifferUiFragment(Action<PacketSnifferUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var topRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _toggleBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(PacketSnifferUiAction.ToggleCapture));
        topRow.AddChild(_toggleBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(PacketSnifferUiAction.Refresh));
        topRow.AddChild(refreshBtn);

        var clearBtn = new Button { Text = "Clear Buffer" };
        clearBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(PacketSnifferUiAction.Clear));
        topRow.AddChild(clearBtn);
        AddChild(topRow);

        AddChild(_statusLabel);
        AddChild(_targetGuidInput);
        AddChild(_payloadInput);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(0, 6, 0, 0) };
        scroll.AddChild(_packetList);
        AddChild(scroll);
    }

    public void UpdateState(PacketSnifferUiState state)
    {
        _toggleBtn.Text = state.IsCapturing ? "Capture: ACTIVE" : "Capture: PAUSED";
        _statusLabel.Text = state.StatusMessage;

        _packetList.RemoveAllChildren();
        for (var i = state.Packets.Count - 1; i >= 0; i--)
        {
            var pkt = state.Packets[i];
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            var summary = new Label
            {
                Text = $"#{pkt.PacketId} [{pkt.PacketType}] {pkt.SenderGuid}->{pkt.TargetGuid} {(pkt.IsEncrypted ? "[ENC]" : "[PLAIN]")}: {pkt.PayloadPreview}",
                HorizontalExpand = true,
                FontColorOverride = pkt.IsEncrypted ? Color.Orange : Color.LightGreen
            };
            row.AddChild(summary);

            var reemitBtn = new Button
            {
                Text = pkt.IsEncrypted ? "Replay Encrypted" : "Modify & Re-emit"
            };
            var pktId = pkt.PacketId;
            reemitBtn.OnPressed += _ => _send(new PacketSnifferUiMessageEvent(
                PacketSnifferUiAction.ReemitPacket,
                pktId,
                _targetGuidInput.Text,
                _payloadInput.Text));
            row.AddChild(reemitBtn);

            _packetList.AddChild(row);
        }
    }
}

// ============================================================================
// 4. Autolathe DeviceLink Control UI
// ============================================================================

public sealed partial class LatheControlUi : UIFragment
{
    private LatheControlUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new LatheControlUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is LatheControlUiState latheState)
            _fragment?.UpdateState(latheState);
    }
}

public sealed class LatheControlUiFragment : BoxContainer
{
    private readonly Action<LatheControlUiMessageEvent> _send;
    private readonly Label _headerLabel = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };
    private readonly Label _materialsLabel = new();
    private readonly Label _queueLabel = new();
    private readonly BoxContainer _designsBox = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };

    public LatheControlUiFragment(Action<LatheControlUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var actions = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        var linkBtn = new Button { Text = "Link Lathe" };
        linkBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.LinkLathe));
        actions.AddChild(linkBtn);

        var syncBtn = new Button { Text = "Load Disk Designs" };
        syncBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.SyncDiskDesigns));
        actions.AddChild(syncBtn);

        var cancelBtn = new Button { Text = "Cancel Order" };
        cancelBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.CancelQueue));
        actions.AddChild(cancelBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.Refresh));
        actions.AddChild(refreshBtn);

        AddChild(actions);
        AddChild(_headerLabel);
        AddChild(_statusLabel);
        AddChild(_materialsLabel);
        AddChild(_queueLabel);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(0, 6, 0, 0) };
        scroll.AddChild(_designsBox);
        AddChild(scroll);
    }

    public void UpdateState(LatheControlUiState state)
    {
        _headerLabel.Text = $"Lathe: {state.LatheName} | Producing: {state.CurrentProducing}";
        _statusLabel.Text = state.StatusMessage;

        var matParts = new List<string>();
        foreach (var (mat, amt) in state.Materials)
        {
            matParts.Add($"{mat}: {amt}");
        }
        _materialsLabel.Text = matParts.Count > 0 ? $"Materials: {string.Join(" | ", matParts)}" : "Materials: None";
        _queueLabel.Text = state.QueueItems.Count > 0 ? $"Queue: {string.Join(", ", state.QueueItems)}" : "Queue: Empty";

        _designsBox.RemoveAllChildren();
        foreach (var design in state.Designs)
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label
            {
                Text = $"{design.Name} ({design.MaterialsSummary})",
                HorizontalExpand = true,
                FontColorOverride = design.CanProduce ? Color.White : Color.Gray
            });

            var printBtn = new Button { Text = "Print 1x", Disabled = !design.CanProduce };
            var designId = design.Id;
            printBtn.OnPressed += _ => _send(new LatheControlUiMessageEvent(LatheControlUiAction.PrintDesign, designId, 1));
            row.AddChild(printBtn);

            _designsBox.AddChild(row);
        }
    }
}

// ============================================================================
// 5. APC DeviceLink Control UI
// ============================================================================

public sealed partial class ApcControlUi : UIFragment
{
    private ApcControlUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new ApcControlUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is ApcControlUiState apcState)
            _fragment?.UpdateState(apcState);
    }
}

public sealed class ApcControlUiFragment : BoxContainer
{
    private readonly Action<ApcControlUiMessageEvent> _send;
    private readonly Label _apcNameLabel = new();
    private readonly Label _breakerLabel = new();
    private readonly Label _chargeLabel = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };
    private readonly Button _toggleBreakerBtn = new() { Text = "Toggle Main Breaker (via Console Command)" };

    public ApcControlUiFragment(Action<ApcControlUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        var linkBtn = new Button { Text = "Link APC" };
        linkBtn.OnPressed += _ => _send(new ApcControlUiMessageEvent(ApcControlUiAction.LinkApc));
        row.AddChild(linkBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new ApcControlUiMessageEvent(ApcControlUiAction.Refresh));
        row.AddChild(refreshBtn);

        AddChild(row);
        AddChild(_apcNameLabel);
        AddChild(_breakerLabel);
        AddChild(_chargeLabel);
        AddChild(_toggleBreakerBtn);
        AddChild(_statusLabel);

        _toggleBreakerBtn.OnPressed += _ => _send(new ApcControlUiMessageEvent(ApcControlUiAction.ToggleMainBreaker));
    }

    public void UpdateState(ApcControlUiState state)
    {
        _apcNameLabel.Text = $"Target APC: {state.ApcName}";
        _breakerLabel.Text = $"Main Breaker: {(state.MainBreakerEnabled ? "ONLINE" : "TRIPPED / OFF")}";
        _chargeLabel.Text = $"Battery Charge: {state.ChargePercent:F1}% | External Power: {state.ExternalPowerState}";
        _statusLabel.Text = state.StatusMessage;
    }
}

// ============================================================================
// 6. Atmos (Air Alarm) DeviceLink Control UI
// ============================================================================

public sealed partial class AtmosControlUi : UIFragment
{
    private AtmosControlUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new AtmosControlUiFragment(msg => userInterface.SendMessage(new CartridgeUiMessage(msg)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is AtmosControlUiState atmosState)
            _fragment?.UpdateState(atmosState);
    }
}

public sealed class AtmosControlUiFragment : BoxContainer
{
    private readonly Action<AtmosControlUiMessageEvent> _send;
    private readonly Label _alarmNameLabel = new();
    private readonly Label _telemetryLabel = new();
    private readonly Label _modeLabel = new();
    private readonly Label _statusLabel = new() { Modulate = Color.LimeGreen };

    public AtmosControlUiFragment(Action<AtmosControlUiMessageEvent> send)
    {
        _send = send;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);

        var topRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        var linkBtn = new Button { Text = "Link Air Alarm" };
        linkBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.LinkAlarm));
        topRow.AddChild(linkBtn);

        var refreshBtn = new Button { Text = "Refresh" };
        refreshBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.Refresh));
        topRow.AddChild(refreshBtn);
        AddChild(topRow);

        AddChild(_alarmNameLabel);
        AddChild(_telemetryLabel);
        AddChild(_modeLabel);

        var modesRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 6) };
        var filterBtn = new Button { Text = "Filtering" };
        filterBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModeFiltering));
        modesRow.AddChild(filterBtn);

        var fillBtn = new Button { Text = "Fill" };
        fillBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModeFill));
        modesRow.AddChild(fillBtn);

        var panicBtn = new Button { Text = "Panic Siphon" };
        panicBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModePanic));
        modesRow.AddChild(panicBtn);

        var offBtn = new Button { Text = "Off" };
        offBtn.OnPressed += _ => _send(new AtmosControlUiMessageEvent(AtmosControlUiAction.SetModeNone));
        modesRow.AddChild(offBtn);

        AddChild(modesRow);
        AddChild(_statusLabel);
    }

    public void UpdateState(AtmosControlUiState state)
    {
        _alarmNameLabel.Text = $"Target Air Alarm: {state.AlarmName} [{state.DangerLevel}]";
        _telemetryLabel.Text = $"Pressure: {state.PressureKpa:F1} kPa | Temperature: {state.TemperatureKelvin:F1} K";
        _modeLabel.Text = $"Operating Mode: {state.CurrentMode}";
        _statusLabel.Text = state.StatusMessage;
    }
}
