using System.Linq;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// File Browser Program UI
// =============================================================================

public sealed class FileBrowserUiControl : ProgramUiControl
{
    private readonly Label _pathLabel;
    private readonly Label _statusLabel;
    private readonly ContentBoxContainer _fileListContainer;
    private readonly ContentBoxContainer _browserPanel;
    private readonly ContentBoxContainer _previewPanel;
    private readonly Label _previewTitle;
    private readonly Label _previewBody;
    private readonly ContentStandardButton _ejectDiskBtn;

    public FileBrowserUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

        var dirBar = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        void AddDirBtn(string label, string dir)
        {
            var b = new ContentStandardButton { Text = label, Margin = new Thickness(2, 0), MinWidth = 52 };
            b.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.ChangeDirectory, dir));
            dirBar.AddChild(b);
        }

        AddDirBtn("/", "/");
        AddDirBtn("/bin", "/bin/");
        AddDirBtn("/notes", "/notes/");
        AddDirBtn("/tasks", "/tasks/");
        AddDirBtn("/blueprints", "/blueprints/");
        AddDirBtn("/records", "/records/");
        AddDirBtn("/logs", "/logs/");
        AddDirBtn("/disk", "/disk/");
        root.AddChild(dirBar);

        var ctrlBar = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _pathLabel = new Label { Text = "/", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var newBtn = new ContentStandardButton { Text = "+ Note", Margin = new Thickness(2, 0), MinWidth = 65 };
        newBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.CreateFile));
        _ejectDiskBtn = new ContentStandardButton { Text = "Eject Disk", Margin = new Thickness(2, 0), MinWidth = 80, Visible = false };
        _ejectDiskBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.EjectDisk));
        ctrlBar.AddChild(_pathLabel);
        ctrlBar.AddChild(newBtn);
        ctrlBar.AddChild(_ejectDiskBtn);
        root.AddChild(ctrlBar);

        _statusLabel = new Label { Text = "Ready", FontColorOverride = Color.FromHex("#88CCCC"), Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_statusLabel);

        _browserPanel = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _fileListContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_fileListContainer);
        _browserPanel.AddChild(scroll);
        root.AddChild(_browserPanel);

        _previewPanel = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Visible = false };
        var pHead = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _previewTitle = new Label { Text = "File", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFE066") };
        var backBtn = new ContentStandardButton { Text = "Back", MinWidth = 55 };
        backBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.CloseView));
        pHead.AddChild(_previewTitle);
        pHead.AddChild(backBtn);
        _previewPanel.AddChild(pHead);
        var pScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _previewBody = new Label { FontColorOverride = Color.FromHex("#E0F7FA") };
        pScroll.AddChild(_previewBody);
        _previewPanel.AddChild(pScroll);
        root.AddChild(_previewPanel);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not FileBrowserUiState state)
            return;

        _pathLabel.Text = $"{state.CurrentDirectory} | HDD: {state.StorageUsedBytes / 1024}/{state.StorageTotalBytes / 1024}KB";
        _statusLabel.Text = state.HasHardDrive ? state.StatusMessage : "NO HARD DRIVE INSTALLED";
        _ejectDiskBtn.Visible = state.HasDisk;

        if (!string.IsNullOrEmpty(state.ViewedFileName))
        {
            _browserPanel.Visible = false;
            _previewPanel.Visible = true;
            _previewTitle.Text = state.ViewedFileName;
            _previewBody.Text = state.ViewedFileContent ?? string.Empty;
            return;
        }

        _previewPanel.Visible = false;
        _browserPanel.Visible = true;
        _fileListContainer.RemoveAllChildren();

        var cur = state.CurrentDirectory.TrimEnd('/');
        var visible = state.Files.Where(f =>
            state.CurrentDirectory == "/disk/" ? f.IsOnDisk :
            state.CurrentDirectory == "/" ? !f.IsOnDisk :
            !f.IsOnDisk && f.Directory.TrimEnd('/').Equals(cur, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (visible.Count == 0)
        {
            _fileListContainer.AddChild(new Label
            {
                Text = state.CurrentDirectory == "/disk/" && !state.HasDisk ? "(No disk inserted)" : "(Directory empty)",
                FontColorOverride = Color.Gray
            });
            return;
        }

        foreach (var file in visible)
        {
            var dir = file.Directory.EndsWith('/') ? file.Directory : file.Directory + "/";
            var fullPath = $"{dir}{file.Name}.{file.Extension}";

            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
            row.AddChild(new Label
            {
                Text = $"{file.Name}.{file.Extension} ({file.Size}B){(file.IsReadOnly ? " [RO]" : "")}",
                HorizontalExpand = true
            });

            var viewBtn = new ContentStandardButton { Text = "View", MinWidth = 48, Margin = new Thickness(1, 0) };
            viewBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.ViewFile, fullPath));
            row.AddChild(viewBtn);

            if (file.Extension.Equals("nte", StringComparison.OrdinalIgnoreCase) && !file.IsOnDisk)
            {
                var runBtn = new ContentStandardButton { Text = "Run", MinWidth = 44, Margin = new Thickness(1, 0) };
                runBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.RunProgram, fullPath));
                row.AddChild(runBtn);
            }

            if (state.HasDisk)
            {
                var cpBtn = new ContentStandardButton { Text = file.IsOnDisk ? "->HDD" : "->Disk", MinWidth = 54, Margin = new Thickness(1, 0) };
                cpBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(
                    file.IsOnDisk ? FileBrowserAction.CopyToInternal : FileBrowserAction.CopyToDisk, fullPath));
                row.AddChild(cpBtn);
            }

            if (!file.IsReadOnly)
            {
                var delBtn = new ContentStandardButton { Text = "Del", MinWidth = 42, Margin = new Thickness(1, 0) };
                delBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.DeleteFile, fullPath));
                row.AddChild(delBtn);
            }

            _fileListContainer.AddChild(row);
        }
    }
}

// =============================================================================
// Network Downloader & Uploader Program UI
// =============================================================================

public sealed class DownloaderUiControl : ProgramUiControl
{
    private readonly Label _headerLabel;
    private readonly Label _statusLabel;
    private readonly ContentBoxContainer _listContainer;

    public DownloaderUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

        _headerLabel = new Label { Text = "NTOS Package & File Transfer", FontColorOverride = Color.FromHex("#00D4FF") };
        _statusLabel = new Label { Text = "Ready", FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 2, 0, 4) };
        root.AddChild(_headerLabel);
        root.AddChild(_statusLabel);

        var catRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        void AddCat(string c)
        {
            var btn = new ContentStandardButton { Text = c, Margin = new Thickness(2, 0), MinWidth = 65 };
            btn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.SelectCategory, c));
            catRow.AddChild(btn);
        }
        AddCat("All");
        AddCat("Programs");
        AddCat("Blueprints");
        AddCat("Databases");
        AddCat("Documents");
        var refreshBtn = new ContentStandardButton { Text = "Refresh", Margin = new Thickness(4, 0), MinWidth = 65 };
        refreshBtn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.Refresh));
        catRow.AddChild(refreshBtn);
        root.AddChild(catRow);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _listContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_listContainer);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not DownloaderUiState state)
            return;

        _headerLabel.Text = $"Server: {state.ServerName} [{state.ServerGuid}] | DL:{state.DownloadBps / 1024}KB/s UL:{state.UploadBps / 1024}KB/s | {(state.IsServerOnline ? "ONLINE" : "OFFLINE")}";
        _statusLabel.Text = state.StatusMessage;

        _listContainer.RemoveAllChildren();
        _listContainer.AddChild(new Label { Text = "--- REMOTE SERVER REPOSITORY ---", FontColorOverride = Color.FromHex("#00D4FF") });

        if (!state.IsServerOnline)
        {
            _listContainer.AddChild(new Label { Text = "(Cannot reach online data server — check NIC & router)", FontColorOverride = Color.FromHex("#FF6666") });
        }
        else
        {
            foreach (var item in state.Items)
            {
                var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
                row.AddChild(new Label { Text = $"{item.Name} [{item.Category}] ({item.Size}B)", HorizontalExpand = true });

                var dlBtn = new ContentStandardButton
                {
                    Text = item.IsDownloaded ? "Re-Download" : "Download",
                    MinWidth = 90
                };
                dlBtn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.DownloadItem, item.RemotePath));
                row.AddChild(dlBtn);
                _listContainer.AddChild(row);
            }
        }

        if (state.LocalUploadables.Count > 0)
        {
            _listContainer.AddChild(new Label { Text = "--- UPLOAD LOCAL FILES TO SERVER ---", FontColorOverride = Color.FromHex("#00FF88"), Margin = new Thickness(0, 8, 0, 2) });
            foreach (var local in state.LocalUploadables)
            {
                var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
                row.AddChild(new Label { Text = $"{local.Path} ({local.Size}B)", HorizontalExpand = true });

                var upBtn = new ContentStandardButton { Text = "Upload", MinWidth = 75 };
                upBtn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.UploadFile, local.Path));
                row.AddChild(upBtn);
                _listContainer.AddChild(row);
            }
        }
    }
}

// =============================================================================
// Router Admin Program UI
// =============================================================================

public sealed class RouterUiControl : ProgramUiControl
{
    private readonly Label _headerLabel;
    private readonly Label _statusLabel;
    private readonly ContentBoxContainer _contentList;
    private readonly ContentLineEdit _guidInput;
    private readonly ContentLineEdit _dnsNameInput;
    private readonly ContentLineEdit _dnsGuidInput;

    public RouterUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

        _headerLabel = new Label { Text = "Network Router", FontColorOverride = Color.FromHex("#00D4FF") };
        _statusLabel = new Label { Text = "Ready", FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 2, 0, 4) };
        root.AddChild(_headerLabel);
        root.AddChild(_statusLabel);

        // Quick Blacklist & DNS Controls
        var ctrlRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _guidInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Target GUID..." };
        var blkBtn = new ContentStandardButton { Text = "Blacklist", Margin = new Thickness(2, 0) };
        blkBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.BlacklistGuid, _guidInput.Text));
        var unblkBtn = new ContentStandardButton { Text = "Unblock", Margin = new Thickness(2, 0) };
        unblkBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.UnblacklistGuid, _guidInput.Text));
        var refBtn = new ContentStandardButton { Text = "Refresh", Margin = new Thickness(2, 0) };
        refBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.Refresh));
        ctrlRow.AddChild(_guidInput);
        ctrlRow.AddChild(blkBtn);
        ctrlRow.AddChild(unblkBtn);
        ctrlRow.AddChild(refBtn);
        root.AddChild(ctrlRow);

        var dnsRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _dnsNameInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "DNS Hostname..." };
        _dnsGuidInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Target GUID..." };
        var setDnsBtn = new ContentStandardButton { Text = "Add DNS", Margin = new Thickness(2, 0) };
        setDnsBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.SetDns, _dnsNameInput.Text, _dnsGuidInput.Text));
        dnsRow.AddChild(_dnsNameInput);
        dnsRow.AddChild(_dnsGuidInput);
        dnsRow.AddChild(setDnsBtn);
        root.AddChild(dnsRow);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _contentList = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_contentList);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not RouterUiState state)
            return;

        _headerLabel.Text = $"ROUTER [{state.RouterGuid}] | {(state.IsPowered ? "ONLINE" : "OFFLINE")} | DL:{state.BandwidthBps / 1024}KB/s UL:{state.UploadBps / 1024}KB/s";
        _statusLabel.Text = state.StatusMessage;
        _contentList.RemoveAllChildren();

        _contentList.AddChild(new Label { Text = $"CONNECTED DEVICES ({state.ConnectedDevices.Count}):", FontColorOverride = Color.FromHex("#00D4FF") });
        foreach (var dev in state.ConnectedDevices)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label { Text = $"{dev.Guid} - {dev.Name} ({(dev.IsBlacklisted ? "BLOCKED" : dev.IsOnline ? "UP" : "DOWN")})", HorizontalExpand = true });
            var btn = new ContentStandardButton { Text = dev.IsBlacklisted ? "Unblock" : "Block", MinWidth = 65 };
            btn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(dev.IsBlacklisted ? RouterUiAction.UnblacklistGuid : RouterUiAction.BlacklistGuid, dev.Guid));
            row.AddChild(btn);
            _contentList.AddChild(row);
        }

        _contentList.AddChild(new Label { Text = $"ACTIVE SESSIONS ({state.Connections.Count}):", FontColorOverride = Color.FromHex("#00FF88"), Margin = new Thickness(0, 6, 0, 0) });
        foreach (var conn in state.Connections)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new Label { Text = $"[{conn.ConnectionType}] {conn.SourceGuid} -> {conn.TargetGuid}", HorizontalExpand = true });
            var killBtn = new ContentStandardButton { Text = "Drop", MinWidth = 50 };
            killBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.KillConnection, conn.ConnectionId));
            row.AddChild(killBtn);
            _contentList.AddChild(row);
        }

        _contentList.AddChild(new Label { Text = $"DNS RECORDS ({state.DnsRecords.Count}):", FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 6, 0, 0) });
        foreach (var (k, v) in state.DnsRecords)
            _contentList.AddChild(new Label { Text = $"  {k} -> {v}" });
    }
}

// =============================================================================
// Packet Sniffer Program UI
// =============================================================================

public sealed class PacketSnifferUiControl : ProgramUiControl
{
    private readonly Label _headerLabel;
    private readonly ContentBoxContainer _packetsContainer;
    private readonly ContentLineEdit _targetInput;
    private readonly ContentLineEdit _payloadInput;

    public PacketSnifferUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

        _headerLabel = new Label { Text = "Packet Sniffer", FontColorOverride = Color.FromHex("#00D4FF") };
        root.AddChild(_headerLabel);

        var btnBar = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2, 0, 4) };
        var refBtn = new ContentStandardButton { Text = "Refresh", Margin = new Thickness(2, 0) };
        refBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.Refresh));
        var saveBtn = new ContentStandardButton { Text = "Save to HDD", Margin = new Thickness(2, 0) };
        saveBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.SaveLogsToFile));
        var clrBtn = new ContentStandardButton { Text = "Clear", Margin = new Thickness(2, 0) };
        clrBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.ClearLogs));
        btnBar.AddChild(refBtn);
        btnBar.AddChild(saveBtn);
        btnBar.AddChild(clrBtn);
        root.AddChild(btnBar);

        var emitRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _targetInput = new ContentLineEdit { MinWidth = 100, PlaceHolder = "Target GUID" };
        _payloadInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Raw packet payload..." };
        var emitBtn = new ContentStandardButton { Text = "Emit", MinWidth = 55 };
        emitBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.EmitPacket, targetGuid: _targetInput.Text, payload: _payloadInput.Text));
        emitRow.AddChild(_targetInput);
        emitRow.AddChild(_payloadInput);
        emitRow.AddChild(emitBtn);
        root.AddChild(emitRow);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _packetsContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_packetsContainer);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not PacketSnifferUiState state)
            return;

        _headerLabel.Text = $"Sniffer [{state.RouterGuid}] | {(state.HasNetworkCard ? "NIC UP" : "NO NIC")} | {state.StatusMessage}";
        _packetsContainer.RemoveAllChildren();
        foreach (var p in state.Packets.TakeLast(40).Reverse())
        {
            _packetsContainer.AddChild(new Label
            {
                Text = $"[{p.Timestamp:mm\\:ss}] {p.SourceAddress}->{p.TargetAddress} [{p.Protocol}] {p.ByteSize}B: {p.PayloadSummary}",
                FontColorOverride = p.Dropped ? Color.FromHex("#FF5555") : Color.FromHex("#E0F7FA")
            });
        }
    }
}
