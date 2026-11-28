using System.Linq;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Network;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// File Browser Program UI
// =============================================================================

public sealed class FileBrowserUiControl : ProgramUiControl
{
    private readonly ContentLabel _pathLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentBoxContainer _fileListContainer;
    private readonly ContentBoxContainer _browserPanel;
    private readonly ContentBoxContainer _previewPanel;
    private readonly ContentLabel _previewTitle;
    private readonly ContentLabel _previewBody;
    private readonly ContentButton _ejectDiskBtn;

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

        var dirBar = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 0, 0, 4) };
        void AddDirBtn(string label, string dir)
        {
            var b = new ContentButton { Text = label, MinWidth = 48 };
            b.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.ChangeDirectory, dir));
            dirBar.AddChild(b);
        }

        AddDirBtn("/", "/");
        AddDirBtn("/bin", "/bin/");
        AddDirBtn("/notes", "/notes/");
        AddDirBtn("/tasks", "/tasks/");
        AddDirBtn("/keys", "/keys/");
        AddDirBtn("/blueprints", "/blueprints/");
        AddDirBtn("/records", "/records/");
        AddDirBtn("/logs", "/logs/");
        AddDirBtn("/disk", "/disk/");
        root.AddChild(dirBar);

        var ctrlBar = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _pathLabel = new ContentLabel { Text = "/", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var newBtn = new ContentButton { Text = "+ Note", Margin = new Thickness(2, 0), MinWidth = 65 };
        newBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.CreateFile));
        _ejectDiskBtn = new ContentButton { Text = "Eject Disk", Margin = new Thickness(2, 0), MinWidth = 80, Visible = false };
        _ejectDiskBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.EjectDisk));
        ctrlBar.AddChild(_pathLabel);
        ctrlBar.AddChild(newBtn);
        ctrlBar.AddChild(_ejectDiskBtn);
        root.AddChild(ctrlBar);

        _statusLabel = new ContentLabel { Text = "Ready", HorizontalExpand = true, FontColorOverride = Color.FromHex("#88CCCC"), Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_statusLabel);

        _browserPanel = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _fileListContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_fileListContainer);
        _browserPanel.AddChild(scroll);
        root.AddChild(_browserPanel);

        _previewPanel = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Visible = false };
        var pHead = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _previewTitle = new ContentLabel { Text = "File", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFE066") };
        var backBtn = new ContentButton { Text = "Back", MinWidth = 55 };
        backBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.CloseView));
        pHead.AddChild(_previewTitle);
        pHead.AddChild(backBtn);
        _previewPanel.AddChild(pHead);
        var pScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _previewBody = new ContentLabel { HorizontalExpand = true, FontColorOverride = Color.FromHex("#E0F7FA"), VerticalAlignment = VAlignment.Top };
        pScroll.AddChild(_previewBody);
        _previewPanel.AddChild(pScroll);
        root.AddChild(_previewPanel);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not FileBrowserUiState state)
            return;

        _pathLabel.Text = $"{state.CurrentDirectory} | HDD: {Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(state.StorageUsedBytes)} / {Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(state.StorageTotalBytes)}";
        _statusLabel.Text = state.HasHardDrive ? state.StatusMessage : "NO HARD DRIVE INSTALLED";
        _ejectDiskBtn.Visible = state.HasDisk;

        if (!string.IsNullOrEmpty(state.ViewedFileName))
        {
            _browserPanel.Visible = false;
            _previewPanel.Visible = true;
            var tsLabel = state.ViewedFileLastModified is { } modTs
                ? $" [Modified {modTs:hh\\:mm\\:ss}]"
                : string.Empty;
            _previewTitle.Text = $"{state.ViewedFileName}{tsLabel}";
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
            _fileListContainer.AddChild(new ContentLabel
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
            var isCopyProtected = file.Extension.Equals("lathe", StringComparison.OrdinalIgnoreCase) &&
                                  (file.IsEncrypted || file.RemainingUses != null);

            var flags = string.Empty;
            if (file.IsEncrypted)
                flags += " [ENC]";
            if (file.RemainingUses != null)
                flags += $" [{file.RemainingUses} uses]";
            if (file.IsReadOnly)
                flags += " [RO]";

            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
            row.AddChild(new ContentLabel
            {
                Text = $"{file.Name}.{file.Extension} ({Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(file.Size)}) [{file.LastModified:hh\\:mm\\:ss}]{flags}",
                HorizontalExpand = true
            });

            var viewBtn = new ContentButton { Text = "View", MinWidth = 48, Margin = new Thickness(1, 0) };
            viewBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.ViewFile, fullPath));
            row.AddChild(viewBtn);

            if (file.Extension.Equals("nte", StringComparison.OrdinalIgnoreCase) && !file.IsOnDisk)
            {
                var runBtn = new ContentButton { Text = "Run", MinWidth = 44, Margin = new Thickness(1, 0) };
                runBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(FileBrowserAction.RunProgram, fullPath));
                row.AddChild(runBtn);
            }

            if (state.HasDisk && !isCopyProtected)
            {
                var cpBtn = new ContentButton { Text = file.IsOnDisk ? "->HDD" : "->Disk", MinWidth = 54, Margin = new Thickness(1, 0) };
                cpBtn.OnPressed += _ => SendProgramMessage(new FileBrowserActionMessage(
                    file.IsOnDisk ? FileBrowserAction.CopyToInternal : FileBrowserAction.CopyToDisk, fullPath));
                row.AddChild(cpBtn);
            }

            if (!file.IsReadOnly)
            {
                var delBtn = new ContentButton { Text = "Del", MinWidth = 42, Margin = new Thickness(1, 0) };
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
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentButton _cancelBtn;
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

        _headerLabel = new ContentLabel { Text = "NTOS Package & File Transfer", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var statusRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2, 0, 4), SeparationOverride = 6 };
        _statusLabel = new ContentLabel { Text = "Ready", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFE066") };
        _cancelBtn = new ContentButton { Name = "DownloaderCancelTransferBtn", Text = "Cancel Transfer", MinWidth = 110, Visible = false };
        _cancelBtn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.CancelTransfer));
        statusRow.AddChild(_statusLabel);
        statusRow.AddChild(_cancelBtn);
        root.AddChild(_headerLabel);
        root.AddChild(statusRow);

        var catRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 0, 0, 4) };
        void AddCat(string c)
        {
            var btn = new ContentButton { Text = c, MinWidth = 60 };
            btn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.SelectCategory, c));
            catRow.AddChild(btn);
        }
        AddCat("All");
        AddCat("Programs");
        AddCat("Blueprints");
        AddCat("Databases");
        AddCat("Documents");
        var refreshBtn = new ContentButton { Text = "Refresh", MinWidth = 65 };
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

        _headerLabel.Text = $"Server: {state.ServerName} | DL:{Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatRateQb(state.DownloadBps)} UL:{Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatRateQb(state.UploadBps)} | {(state.IsServerOnline ? "ONLINE" : "OFFLINE")}";
        _statusLabel.Text = state.StatusMessage;
        _cancelBtn.Visible = state.ActiveTransfers.Count > 0;
        _cancelBtn.Text = state.ActiveTransfers.Count > 1 ? $"Cancel All ({state.ActiveTransfers.Count})" : "Cancel Transfer";

        var activeDownloadPaths = new HashSet<string>(
            state.ActiveTransfers.Where(t => !t.IsUpload).Select(t => t.Path),
            StringComparer.OrdinalIgnoreCase);

        _listContainer.RemoveAllChildren();
        if (state.ActiveTransfers.Count > 0)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = $"--- TRANSFER QUEUE ({state.ActiveTransfers.Count}) ---",
                FontColorOverride = Color.FromHex("#FFE066")
            });
            foreach (var xfer in state.ActiveTransfers)
            {
                var xferPath = xfer.Path;
                var qRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
                qRow.AddChild(new ContentLabel
                {
                    Text = $"{(xfer.IsUpload ? "UP" : "DL")} {xfer.Path} — {xfer.Percent}% ({Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(xfer.TransferredBytes)} / {Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(xfer.TotalBytes)})",
                    HorizontalExpand = true,
                    FontColorOverride = Color.FromHex("#B8F2FF")
                });
                var cancelOneBtn = new ContentButton { Text = "Cancel", MinWidth = 68 };
                cancelOneBtn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.CancelItem, xferPath));
                qRow.AddChild(cancelOneBtn);
                _listContainer.AddChild(qRow);
            }
        }

        _listContainer.AddChild(new ContentLabel { Text = "--- REMOTE SERVER REPOSITORY ---", FontColorOverride = Color.FromHex("#00D4FF"), Margin = new Thickness(0, state.ActiveTransfers.Count > 0 ? 6 : 0, 0, 0) });

        if (!state.IsServerOnline)
        {
            _listContainer.AddChild(new ContentLabel { Text = "Data server unreachable.", FontColorOverride = Color.FromHex("#FF6666") });
        }
        else
        {
            foreach (var item in state.Items)
            {
                var itemPath = item.RemotePath;
                var isQueued = activeDownloadPaths.Contains(itemPath);
                var reqTip = string.IsNullOrEmpty(item.RequiredAccessSummary)
                    ? null
                    : $"Required Access: {item.RequiredAccessSummary}";

                var row = new ContentBoxContainer
                {
                    Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    Margin = new Thickness(0, 2),
                    MouseFilter = MouseFilterMode.Pass,
                    ToolTip = reqTip
                };
                row.AddChild(new ContentLabel
                {
                    Text = $"{item.Name} [{item.Category}] ({Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(item.Size)})",
                    HorizontalExpand = true,
                    MouseFilter = MouseFilterMode.Pass,
                    ToolTip = reqTip,
                    FontColorOverride = item.HasDownloadAccess ? Color.White : Color.FromHex("#9A8888")
                });

                var dlBtn = new ContentButton
                {
                    Text = !item.HasDownloadAccess
                        ? "Locked"
                        : isQueued
                            ? "Queued"
                            : item.IsDownloaded
                                ? "Re-Download"
                                : "Download",
                    Disabled = !item.HasDownloadAccess || isQueued,
                    ToolTip = reqTip,
                    MinWidth = 90
                };
                dlBtn.OnPressed += _ => SendProgramMessage(new DownloaderActionMessage(DownloaderAction.DownloadItem, itemPath));
                row.AddChild(dlBtn);
                _listContainer.AddChild(row);
            }
        }

        if (state.LocalUploadables.Count > 0)
        {
            _listContainer.AddChild(new ContentLabel { Text = "--- UPLOAD LOCAL FILES TO SERVER ---", FontColorOverride = Color.FromHex("#00FF88"), Margin = new Thickness(0, 8, 0, 2) });
            foreach (var local in state.LocalUploadables)
            {
                var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
                row.AddChild(new ContentLabel { Text = $"{local.Path} ({Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatStorageQb(local.Size)})", HorizontalExpand = true });

                var upBtn = new ContentButton { Text = "Upload", MinWidth = 75 };
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
    private readonly ContentBoxContainer _mainRoot;
    private readonly ContentPanelContainer _lockBanner;
    private readonly ContentLabel _lockBannerLabel;
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentBoxContainer _contentList;
    private readonly ContentLineEdit _guidInput;
    private readonly ContentLineEdit _dnsNameInput;
    private readonly ContentLineEdit _dnsGuidInput;

    public RouterUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        _mainRoot = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };
        var root = _mainRoot;

        _lockBanner = new ContentPanelContainer
        {
            Name = "RouterLockBanner",
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0A1018"),
                BorderColor = Color.FromHex("#AA3333"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 16,
                ContentMarginRightOverride = 16,
                ContentMarginTopOverride = 16,
                ContentMarginBottomOverride = 16
            }
        };
        _lockBannerLabel = new ContentLabel
        {
            Text = "ROUTER LOCKED — Swipe authorized ID card to unlock.",
            FontColorOverride = Color.FromHex("#FF8888"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center
        };
        _lockBanner.AddChild(_lockBannerLabel);

        _headerLabel = new ContentLabel { Text = "Network Router", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        _statusLabel = new ContentLabel { Text = "Ready", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 2, 0, 4) };
        root.AddChild(_headerLabel);
        root.AddChild(_statusLabel);

        // Quick Blacklist & DNS Controls
        _guidInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Target GUID...", Margin = new Thickness(0, 0, 0, 2) };
        root.AddChild(_guidInput);
        var ctrlRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 0, 0, 4) };
        var blkBtn = new ContentButton { Text = "Blacklist" };
        blkBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.BlacklistGuid, _guidInput.Text));
        var unblkBtn = new ContentButton { Text = "Unblock" };
        unblkBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.UnblacklistGuid, _guidInput.Text));
        var refBtn = new ContentButton { Text = "Refresh" };
        refBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.Refresh));
        ctrlRow.AddChild(blkBtn);
        ctrlRow.AddChild(unblkBtn);
        ctrlRow.AddChild(refBtn);
        root.AddChild(ctrlRow);

        var dnsRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _dnsNameInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "DNS Hostname..." };
        _dnsGuidInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Target GUID...", Margin = new Thickness(4, 0, 0, 0) };
        var setDnsBtn = new ContentButton { Text = "Add DNS", Margin = new Thickness(4, 0, 0, 0) };
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
        AddChild(_lockBanner);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not RouterUiState state)
            return;

        if (state.IsLocked)
        {
            _mainRoot.Visible = false;
            _lockBanner.Visible = true;
            _lockBannerLabel.Text = string.IsNullOrWhiteSpace(state.StatusMessage)
                ? "ROUTER LOCKED — Swipe authorized ID card to unlock."
                : state.StatusMessage;
            _contentList.RemoveAllChildren();
            return;
        }

        _mainRoot.Visible = true;
        _lockBanner.Visible = false;

        _headerLabel.Text = $"ROUTER [{state.RouterGuid}] | {(state.IsPowered ? "ONLINE" : "OFFLINE")} | DL:{Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatRateQb(state.BandwidthBps)} UL:{Content.Shared._Oxyd.Computers.FileSystem.ComputerSizeFormat.FormatRateQb(state.UploadBps)}";
        _statusLabel.Text = state.StatusMessage;
        _contentList.RemoveAllChildren();

        _contentList.AddChild(new ContentLabel { Text = $"CONNECTED DEVICES ({state.ConnectedDevices.Count}):", FontColorOverride = Color.FromHex("#00D4FF") });
        foreach (var dev in state.ConnectedDevices)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new ContentLabel { Text = $"{dev.Guid} - {dev.Name} ({(dev.IsBlacklisted ? "BLOCKED" : dev.IsOnline ? "UP" : "DOWN")})", HorizontalExpand = true });
            var btn = new ContentButton { Text = dev.IsBlacklisted ? "Unblock" : "Block", MinWidth = 65 };
            btn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(dev.IsBlacklisted ? RouterUiAction.UnblacklistGuid : RouterUiAction.BlacklistGuid, dev.Guid));
            row.AddChild(btn);
            _contentList.AddChild(row);
        }

        _contentList.AddChild(new ContentLabel { Text = $"ACTIVE SESSIONS ({state.Connections.Count}):", FontColorOverride = Color.FromHex("#00FF88"), Margin = new Thickness(0, 6, 0, 0) });
        foreach (var conn in state.Connections)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
            row.AddChild(new ContentLabel { Text = $"[{conn.ConnectionType}] {conn.SourceGuid} -> {conn.TargetGuid}", HorizontalExpand = true });
            var killBtn = new ContentButton { Text = "Drop", MinWidth = 50 };
            killBtn.OnPressed += _ => SendProgramMessage(new RouterActionMessage(RouterUiAction.KillConnection, conn.ConnectionId));
            row.AddChild(killBtn);
            _contentList.AddChild(row);
        }

        _contentList.AddChild(new ContentLabel { Text = $"DNS RECORDS ({state.DnsRecords.Count}):", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 6, 0, 0) });
        foreach (var (k, v) in state.DnsRecords)
            _contentList.AddChild(new ContentLabel { Text = $"  {k} -> {v}", HorizontalExpand = true });
    }
}

// =============================================================================
// Packet Sniffer Program UI (Player-Relevant Capture & Live Packet Editor)
// =============================================================================

public sealed class PacketSnifferUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentButton _filterRelevantBtn;
    private readonly ContentBoxContainer _packetsContainer;
    private readonly ContentLineEdit _lookupGuidInput;
    private readonly ContentLineEdit _targetInput;
    private readonly ContentLineEdit _sinkPortInput;
    private readonly ContentLineEdit _payloadInput;
    private readonly ContentWrapContainer _sinkTargetsWrap;

    // Inline Packet Editor Panel
    private readonly ContentPanelContainer _editorPanel;
    private readonly ContentLabel _editorHeaderLabel;
    private readonly ContentLineEdit _editTargetInput;
    private readonly ContentLineEdit _editSinkPortInput;
    private readonly ContentBoxContainer _editEntriesContainer;
    private readonly ContentLineEdit _newEntryKeyInput;
    private readonly ContentLineEdit _newEntryValInput;

    private PacketSnifferUiState? _lastState;
    private bool _relevantOnly = true;
    private bool _selectedSinkIsHost = true;
    private string? _editingPacketId;
    private string _editingSourceGuid = string.Empty;
    private string _editingProtocol = string.Empty;
    private string _editingFilePath = string.Empty;
    private readonly List<(string Key, ContentLineEdit ValueEdit)> _entryEditors = new();
    private readonly Dictionary<string, string> _editingEntries = new();

    public PacketSnifferUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4),
            SeparationOverride = 4
        };

        _headerLabel = new ContentLabel { Text = "Packet Sniffer", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        root.AddChild(_headerLabel);

        var btnBar = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        var refBtn = new ContentButton { Text = "Refresh" };
        refBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.Refresh));

        _filterRelevantBtn = new ContentButton { Name = "SnifferFilterRelevantButton", Text = "Filter: Relevant Only" };
        _filterRelevantBtn.OnPressed += _ =>
        {
            _relevantOnly = !_relevantOnly;
            _filterRelevantBtn.Text = _relevantOnly ? "Filter: Relevant Only" : "Filter: All Packets";
            RebuildPacketList();
        };

        var saveBtn = new ContentButton { Text = "Save to HDD" };
        saveBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.SaveLogsToFile));
        var clrBtn = new ContentButton { Text = "Clear" };
        clrBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.ClearLogs));
        btnBar.AddChild(refBtn);
        btnBar.AddChild(_filterRelevantBtn);
        btnBar.AddChild(saveBtn);
        btnBar.AddChild(clrBtn);
        root.AddChild(btnBar);

        var targetLookupRow = new ContentBoxContainer
        {
            Name = "SnifferTargetLookupRow",
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        targetLookupRow.AddChild(new ContentLabel
        {
            Text = "Target GUID:",
            WordWrap = false,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#88CCCC")
        });
        _lookupGuidInput = new ContentLineEdit
        {
            Name = "SnifferTargetGuidInput",
            HorizontalExpand = true,
            PlaceHolder = "Target GUID or DNS..."
        };
        void SubmitTargetLookup()
        {
            SendProgramMessage(new PacketSnifferActionMessage(
                PacketSnifferAction.SelectTargetDevice,
                targetGuid: (_lookupGuidInput.Text ?? string.Empty).Trim()));
        }
        _lookupGuidInput.OnTextEntered += _ => SubmitTargetLookup();

        var queryTargetBtn = new ContentButton
        {
            Name = "SnifferQueryTargetButton",
            Text = "Query Ports",
            MinWidth = 92
        };
        queryTargetBtn.OnPressed += _ => SubmitTargetLookup();

        var useLocalBtn = new ContentButton
        {
            Name = "SnifferUseLocalButton",
            Text = "Local",
            MinWidth = 54
        };
        useLocalBtn.OnPressed += _ =>
        {
            if (_lastState != null)
                _lookupGuidInput.Text = _lastState.LocalGuid;
            SendProgramMessage(new PacketSnifferActionMessage(
                PacketSnifferAction.SelectTargetDevice,
                targetGuid: "local"));
        };

        targetLookupRow.AddChild(_lookupGuidInput);
        targetLookupRow.AddChild(queryTargetBtn);
        targetLookupRow.AddChild(useLocalBtn);
        root.AddChild(targetLookupRow);

        _sinkTargetsWrap = new ContentWrapContainer
        {
            Name = "SnifferSinkTargetsWrap",
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        root.AddChild(_sinkTargetsWrap);

        var emitRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 4 };
        _targetInput = new ContentLineEdit { Name = "SnifferEmitTargetInput", MinWidth = 96, PlaceHolder = "Target GUID" };
        _sinkPortInput = new ContentLineEdit { Name = "SnifferEmitSinkPortInput", MinWidth = 148, Text = "MachineCommandReceiver", PlaceHolder = "Sink Port ID" };
        _payloadInput = new ContentLineEdit { Name = "SnifferEmitPayloadInput", HorizontalExpand = true, PlaceHolder = "Command & arguments..." };
        var emitBtn = new ContentButton { Name = "SnifferEmitToSinkButton", Text = "Send to Sink Port", MinWidth = 128 };
        emitBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(
            PacketSnifferAction.EmitPacket,
            targetGuid: (_targetInput.Text ?? string.Empty).Trim(),
            payload: _payloadInput.Text ?? string.Empty,
            sinkPortId: (_sinkPortInput.Text ?? string.Empty).Trim(),
            isHostPort: _selectedSinkIsHost));
        emitRow.AddChild(_targetInput);
        emitRow.AddChild(_sinkPortInput);
        emitRow.AddChild(_payloadInput);
        emitRow.AddChild(emitBtn);
        root.AddChild(emitRow);

        // Inline Player-Editable Packet Modification Panel
        _editorPanel = new ContentPanelContainer
        {
            Name = "SnifferPacketEditorPanel",
            HorizontalExpand = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#101D2A"),
                BorderColor = Color.FromHex("#00C8A0"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        var edRoot = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };

        _editorHeaderLabel = new ContentLabel
        {
            Text = "PACKET EDITOR",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00FFCC")
        };
        edRoot.AddChild(_editorHeaderLabel);

        var targetRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        targetRow.AddChild(new ContentLabel { Text = "Target GUID:", WordWrap = false, FontColorOverride = Color.FromHex("#88CCCC") });
        _editTargetInput = new ContentLineEdit { Name = "SnifferEditTargetInput", HorizontalExpand = true, PlaceHolder = "Target GUID..." };
        targetRow.AddChild(_editTargetInput);
        targetRow.AddChild(new ContentLabel { Text = "Sink Port:", WordWrap = false, FontColorOverride = Color.FromHex("#88CCCC") });
        _editSinkPortInput = new ContentLineEdit { Name = "SnifferEditSinkPortInput", MinWidth = 148, Text = "MachineCommandReceiver", PlaceHolder = "Sink Port ID..." };
        targetRow.AddChild(_editSinkPortInput);
        edRoot.AddChild(targetRow);

        var edScroll = new ContentScrollContainer { HorizontalExpand = true, MaxHeight = 140 };
        _editEntriesContainer = new ContentBoxContainer
        {
            Name = "SnifferEditEntriesContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        edScroll.AddChild(_editEntriesContainer);
        edRoot.AddChild(edScroll);

        var addRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        _newEntryKeyInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Key..." };
        _newEntryValInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Value..." };
        var addEntryBtn = new ContentButton { Text = "+ Add", MinWidth = 54 };
        addEntryBtn.OnPressed += _ =>
        {
            var k = (_newEntryKeyInput.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(k))
                return;
            SyncEditorsToDictionary();
            _editingEntries[k] = (_newEntryValInput.Text ?? string.Empty).Trim();
            _newEntryKeyInput.Text = string.Empty;
            _newEntryValInput.Text = string.Empty;
            RebuildEditorRows();
        };
        addRow.AddChild(_newEntryKeyInput);
        addRow.AddChild(_newEntryValInput);
        addRow.AddChild(addEntryBtn);
        edRoot.AddChild(addRow);

        var edActions = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        var sendSinkBtn = new ContentButton { Name = "SnifferTransmitModifiedButton", Text = "Send Packet to Sink Port" };
        sendSinkBtn.OnPressed += _ => SubmitEditedPacket();

        var closeEdBtn = new ContentButton { Text = "Close Editor" };
        closeEdBtn.OnPressed += _ => CloseEditor();

        edActions.AddChild(sendSinkBtn);
        edActions.AddChild(closeEdBtn);
        edRoot.AddChild(edActions);

        _editorPanel.AddChild(edRoot);
        root.AddChild(_editorPanel);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _packetsContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        scroll.AddChild(_packetsContainer);
        root.AddChild(scroll);

        AddChild(root);
    }

    private void OpenEditor(NetworkDataPacket pkt)
    {
        _editingPacketId = pkt.PacketId;
        _editingSourceGuid = pkt.SourceAddress;
        _editingProtocol = pkt.Protocol;
        _editingFilePath = pkt.FilePath;

        var targetAddr = pkt.TargetAddress ?? string.Empty;
        var colonIdx = targetAddr.IndexOf(':');
        if (colonIdx > 0)
        {
            _editTargetInput.Text = targetAddr[..colonIdx];
            _editSinkPortInput.Text = targetAddr[(colonIdx + 1)..];
        }
        else
        {
            _editTargetInput.Text = targetAddr;
            if (pkt.Parameters.TryGetValue("SinkPort", out var sinkPort) && !string.IsNullOrWhiteSpace(sinkPort))
                _editSinkPortInput.Text = sinkPort;
        }

        _editingEntries.Clear();
        foreach (var (k, v) in pkt.EditableEntries)
        {
            if (k.StartsWith("__", StringComparison.Ordinal) || k.Equals("tampered", StringComparison.OrdinalIgnoreCase))
                continue;
            _editingEntries[k] = v;
        }

        _editorHeaderLabel.Text = string.IsNullOrWhiteSpace(pkt.FilePath)
            ? $"EDIT [{pkt.PacketId}] {pkt.Protocol} | Src: {pkt.SourceAddress}"
            : $"EDIT [{pkt.PacketId}] {pkt.Protocol} ({pkt.FilePath}) | Src: {pkt.SourceAddress}";
        _editorPanel.Visible = true;
        RebuildEditorRows();
    }

    private void CloseEditor()
    {
        _editingPacketId = null;
        _editorPanel.Visible = false;
        _entryEditors.Clear();
        _editingEntries.Clear();
        _editEntriesContainer.RemoveAllChildren();
    }

    private void SyncEditorsToDictionary()
    {
        foreach (var (key, edit) in _entryEditors)
            _editingEntries[key] = edit.Text ?? string.Empty;
    }

    private void RebuildEditorRows()
    {
        _entryEditors.Clear();
        _editEntriesContainer.RemoveAllChildren();

        if (_editingEntries.Count == 0)
        {
            _editEntriesContainer.AddChild(new ContentLabel
            {
                Text = "No entries.",
                FontColorOverride = Color.FromHex("#8899AA"),
                HorizontalExpand = true
            });
            return;
        }

        foreach (var (k, v) in _editingEntries.ToList())
        {
            var capturedKey = k;
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            row.AddChild(new ContentLabel
            {
                Text = $"{capturedKey}:",
                MinWidth = 110,
                FontColorOverride = Color.FromHex("#FFE066")
            });

            var valEdit = new ContentLineEdit
            {
                Text = v,
                HorizontalExpand = true
            };
            _entryEditors.Add((capturedKey, valEdit));
            row.AddChild(valEdit);

            var remBtn = new ContentButton { Text = "x", MinWidth = 24 };
            remBtn.OnPressed += _ =>
            {
                SyncEditorsToDictionary();
                _editingEntries.Remove(capturedKey);
                RebuildEditorRows();
            };
            row.AddChild(remBtn);

            _editEntriesContainer.AddChild(row);
        }
    }

    private void SubmitEditedPacket()
    {
        if (string.IsNullOrEmpty(_editingPacketId))
            return;

        SyncEditorsToDictionary();
        var payloadDict = new Dictionary<string, string>(_editingEntries);
        SendProgramMessage(new PacketSnifferActionMessage(
            PacketSnifferAction.ModifyAndTransmitPacket,
            sourceGuid: _editingSourceGuid,
            targetGuid: (_editTargetInput.Text ?? string.Empty).Trim(),
            protocol: _editingProtocol,
            payload: string.Empty,
            packetId: _editingPacketId,
            modifiedEntries: payloadDict,
            sinkPortId: (_editSinkPortInput.Text ?? string.Empty).Trim(),
            isHostPort: _selectedSinkIsHost));
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not PacketSnifferUiState state)
            return;

        _lastState = state;
        var relCount = state.Packets.Count(p => p.IsPlayerRelevant);
        _headerLabel.Text = $"Sniffer [{state.RouterGuid}] | {(state.HasNetworkCard ? "NIC UP" : "NO NIC")} | Relevant: {relCount}/{state.Packets.Count} | {state.StatusMessage}";

        if (!string.IsNullOrEmpty(state.SelectedTargetGuid))
        {
            if (string.IsNullOrWhiteSpace(_lookupGuidInput.Text))
                _lookupGuidInput.Text = state.SelectedTargetGuid;
            _targetInput.Text = state.SelectedTargetGuid;
            if (string.IsNullOrWhiteSpace(_editTargetInput.Text))
                _editTargetInput.Text = state.SelectedTargetGuid;
        }

        RebuildTargetPortSelector(state);
        RebuildPacketList();
    }

    private void RebuildTargetPortSelector(PacketSnifferUiState state)
    {
        _sinkTargetsWrap.RemoveAllChildren();

        if (string.IsNullOrWhiteSpace(state.SelectedTargetGuid))
        {
            _sinkTargetsWrap.AddChild(new ContentLabel
            {
                Name = "SnifferNoTargetHintLabel",
                Text = "No target device selected.",
                FontColorOverride = Color.FromHex("#88AAB8"),
                VerticalAlignment = VAlignment.Center
            });
            return;
        }

        _sinkTargetsWrap.AddChild(new ContentLabel
        {
            Name = "SnifferSelectedTargetPortsLabel",
            Text = $"Ports on {state.SelectedTargetName} [{state.SelectedTargetGuid}]:",
            FontColorOverride = Color.FromHex("#66EEFF"),
            VerticalAlignment = VAlignment.Center
        });

        if (state.SinkTargets.Count == 0)
        {
            _sinkTargetsWrap.AddChild(new ContentLabel
            {
                Text = "No sink ports available.",
                FontColorOverride = Color.FromHex("#8899AA"),
                VerticalAlignment = VAlignment.Center
            });
            return;
        }

        foreach (var sink in state.SinkTargets)
        {
            var targetCopy = sink;
            var isSelected = string.Equals(_sinkPortInput.Text?.Trim(), targetCopy.SinkPortId, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(_targetInput.Text?.Trim(), targetCopy.TargetGuid, StringComparison.OrdinalIgnoreCase);
            var btn = new ContentButton
            {
                Name = $"SnifferSinkTarget_{targetCopy.TargetGuid}_{targetCopy.SinkPortId}",
                Text = targetCopy.SinkPortId,
                ToolTip = $"{targetCopy.DeviceName} [{targetCopy.TargetGuid}] — {targetCopy.SinkPortName} ({(targetCopy.IsHostPort ? "Host Port" : "Computer Port")})",
                Pressed = isSelected,
                MinHeight = 22
            };
            btn.OnPressed += _ =>
            {
                _selectedSinkIsHost = targetCopy.IsHostPort;
                _targetInput.Text = targetCopy.TargetGuid;
                _sinkPortInput.Text = targetCopy.SinkPortId;
                _editTargetInput.Text = targetCopy.TargetGuid;
                _editSinkPortInput.Text = targetCopy.SinkPortId;
                if (_lastState != null)
                    RebuildTargetPortSelector(_lastState);
            };
            _sinkTargetsWrap.AddChild(btn);
        }
    }

    private void RebuildPacketList()
    {
        _packetsContainer.RemoveAllChildren();
        if (_lastState == null)
            return;

        IEnumerable<NetworkDataPacket> filtered = _relevantOnly
            ? _lastState.Packets.Where(p => p.IsPlayerRelevant)
            : _lastState.Packets;

        var displayList = filtered.TakeLast(40).Reverse().ToList();
        if (displayList.Count == 0)
        {
            _packetsContainer.AddChild(new ContentLabel
            {
                Text = _relevantOnly
                    ? "No relevant packets captured."
                    : "(No network packets captured yet)",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#8899AA")
            });
            return;
        }

        foreach (var p in displayList)
        {
            var pkt = p;
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            var tag = pkt.IsEditable ? " [EDITABLE]" : pkt.IsPlayerRelevant ? " [RELEVANT]" : string.Empty;
            var color = pkt.Dropped
                ? Color.FromHex("#FF5555")
                : pkt.IsEditable
                    ? Color.FromHex("#00FFCC")
                    : Color.FromHex("#E0F7FA");

            row.AddChild(new ContentLabel
            {
                Text = $"[{pkt.Timestamp:mm\\:ss}] {pkt.SourceAddress}->{pkt.TargetAddress} [{pkt.Protocol}] {pkt.ByteSize}B{tag}: {pkt.PayloadSummary}",
                HorizontalExpand = true,
                FontColorOverride = color
            });

            if (pkt.IsPlayerRelevant && pkt.IsEditable)
            {
                var editBtn = new ContentButton
                {
                    Name = "SnifferEditPacketButton",
                    Text = "Edit",
                    MinWidth = 48,
                    VerticalAlignment = VAlignment.Center
                };
                editBtn.OnPressed += _ => OpenEditor(pkt);
                row.AddChild(editBtn);
            }

            _packetsContainer.AddChild(row);
        }
    }
}

// =============================================================================
// NetNavigator Program UI (/bin/net_navigator.nte)
// =============================================================================

public sealed class NetNavigatorUiControl : ProgramUiControl
{
    private const float GuidColWidth = 86f;
    private const float TypeColWidth = 96f;
    private const float AuthColWidth = 132f;

    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentButton _probeBtn;
    private readonly ContentButton _discoverableBtn;
    private readonly ContentLineEdit _searchInput;
    private readonly ContentBoxContainer _deviceListContainer;
    private readonly Dictionary<string, ContentButton> _filterButtons = new(StringComparer.OrdinalIgnoreCase);

    private NetNavigatorUiState? _lastState;
    private string _categoryFilter = "All";

    public NetNavigatorUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 5
        };

        var topRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 5
        };

        _headerLabel = new ContentLabel
        {
            Name = "NetNavigatorHeaderLabel",
            Text = "NETNAVIGATOR — Network Discovery",
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#66EEFF")
        };

        _discoverableBtn = new ContentButton
        {
            Name = "NetNavigatorDiscoverableButton",
            Text = "Visible: ON",
            MinWidth = 88,
            MinHeight = 24f
        };
        _discoverableBtn.OnPressed += _ => SendProgramMessage(new NetNavigatorActionMessage(NetNavigatorAction.ToggleLocalDiscoverable));

        _probeBtn = new ContentButton
        {
            Name = "NetNavigatorProbeButton",
            Text = "Probe Network",
            MinWidth = 104,
            MinHeight = 24f
        };
        _probeBtn.OnPressed += _ => SendProgramMessage(new NetNavigatorActionMessage(NetNavigatorAction.Probe));

        topRow.AddChild(_headerLabel);
        topRow.AddChild(_discoverableBtn);
        topRow.AddChild(_probeBtn);
        root.AddChild(topRow);

        _statusLabel = new ContentLabel
        {
            Name = "NetNavigatorStatusLabel",
            Text = "Ready.",
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            FontColorOverride = Color.FromHex("#88AAB8")
        };
        root.AddChild(_statusLabel);

        var filterRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 5
        };

        _searchInput = new ContentLineEdit
        {
            Name = "NetNavigatorSearchInput",
            PlaceHolder = "Filter GUID, name, type, auth, or services...",
            HorizontalExpand = true,
            MinHeight = 24f
        };
        _searchInput.OnTextChanged += _ => RebuildDeviceTable();
        filterRow.AddChild(_searchInput);

        var catWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 3,
            VerticalSeparation = 2,
            VerticalAlignment = VAlignment.Center
        };
        foreach (var cat in new[] { "All", "Servers", "Power/Atmos", "Cameras", "Computers", "Auth Req" })
        {
            var captured = cat;
            var btn = new ContentButton
            {
                Name = $"NetNavigatorFilter_{cat.Replace("/", "_")}",
                Text = cat,
                MinHeight = 24f,
                Pressed = cat == "All"
            };
            btn.Label.Margin = new Thickness(5, 2);
            btn.OnPressed += _ =>
            {
                _categoryFilter = captured;
                foreach (var (k, b) in _filterButtons)
                    b.Pressed = string.Equals(k, _categoryFilter, StringComparison.OrdinalIgnoreCase);
                RebuildDeviceTable();
            };
            _filterButtons[cat] = btn;
            catWrap.AddChild(btn);
        }
        filterRow.AddChild(catWrap);
        root.AddChild(filterRow);

        var colHeaderPanel = new ContentPanelContainer
        {
            Name = "NetNavigatorColumnHeader",
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1B28"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4
            }
        };
        var colHeaderRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8
        };
        colHeaderRow.AddChild(MakeHeaderCell("GUID", GuidColWidth));
        colHeaderRow.AddChild(MakeHeaderCell("TYPE", TypeColWidth));
        colHeaderRow.AddChild(MakeHeaderCell("AUTH", AuthColWidth));
        colHeaderRow.AddChild(new ContentLabel
        {
            Text = "DEVICE / DETAILS",
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            FontColorOverride = Color.FromHex("#88CCCC")
        });
        colHeaderPanel.AddChild(colHeaderRow);
        root.AddChild(colHeaderPanel);

        var scroll = new ContentScrollContainer
        {
            Name = "NetNavigatorScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            AlwaysShowBackground = true
        };
        _deviceListContainer = new ContentBoxContainer
        {
            Name = "NetNavigatorDeviceList",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
            Margin = new Thickness(2)
        };
        scroll.AddChild(_deviceListContainer);
        root.AddChild(scroll);

        AddChild(root);
    }

    private static ContentLabel MakeHeaderCell(string text, float width) => new()
    {
        Text = text,
        MinWidth = width,
        MaxWidth = width,
        WordWrap = false,
        ClipText = true,
        FontColorOverride = Color.FromHex("#88CCCC")
    };

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NetNavigatorUiState state)
            return;

        _lastState = state;
        var netStatus = state.IsOnline ? $"Router [{state.RouterGuid}]" : "OFFLINE";
        _headerLabel.Text = $"NETNAVIGATOR — {netStatus} ({state.Devices.Count} devices)";
        _discoverableBtn.Text = state.LocalDiscoverable ? "Visible: ON" : "Visible: OFF";
        _discoverableBtn.Pressed = state.LocalDiscoverable;

        var tsStr = state.LastProbeTime is { } ts ? $" [{ts:hh\\:mm\\:ss}]" : string.Empty;
        _statusLabel.Text = $"{state.StatusMessage}{tsStr}";
        _statusLabel.FontColorOverride = state.IsOnline ? Color.FromHex("#77D4B0") : Color.FromHex("#FFD166");

        RebuildDeviceTable();
    }

    private bool MatchesCategory(DiscoverableNetworkDeviceDto dev)
    {
        return _categoryFilter switch
        {
            "Servers" => dev.DeviceType.Equals("Data Server", StringComparison.OrdinalIgnoreCase) ||
                         dev.DeviceType.Equals("Router", StringComparison.OrdinalIgnoreCase),
            "Power/Atmos" => dev.DeviceType.Equals("APC", StringComparison.OrdinalIgnoreCase) ||
                             dev.DeviceType.Equals("Air Alarm", StringComparison.OrdinalIgnoreCase),
            "Cameras" => dev.DeviceType.Equals("Camera", StringComparison.OrdinalIgnoreCase),
            "Computers" => dev.DeviceType.Equals("Workstation", StringComparison.OrdinalIgnoreCase) ||
                           dev.DeviceType.Equals("PDA", StringComparison.OrdinalIgnoreCase) ||
                           dev.DeviceType.Equals("Autolathe", StringComparison.OrdinalIgnoreCase) ||
                           dev.DeviceType.Equals("Embedded", StringComparison.OrdinalIgnoreCase),
            "Auth Req" => dev.AuthRequired,
            _ => true
        };
    }

    private void RebuildDeviceTable()
    {
        _deviceListContainer.RemoveAllChildren();
        if (_lastState == null)
            return;

        if (!_lastState.IsOnline)
        {
            _deviceListContainer.AddChild(new ContentLabel
            {
                Text = "Network offline — no router connection.",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#FFD166"),
                Margin = new Thickness(6)
            });
            return;
        }

        var query = (_searchInput.Text ?? string.Empty).Trim();
        var shown = 0;

        foreach (var dev in _lastState.Devices)
        {
            if (!MatchesCategory(dev))
                continue;

            if (!string.IsNullOrEmpty(query) &&
                !dev.Guid.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !dev.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !dev.DeviceType.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !dev.AuthSummary.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !dev.ExtraInfo.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;
            var card = new ContentPanelContainer
            {
                Name = "NetNavigatorDeviceRow",
                HorizontalExpand = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = dev.IsLocal ? Color.FromHex("#112636") : Color.FromHex("#0D1924"),
                    BorderColor = dev.IsLocal ? Color.FromHex("#00D4FF") : Color.FromHex("#1D435C"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 8,
                    ContentMarginRightOverride = 8,
                    ContentMarginTopOverride = 5,
                    ContentMarginBottomOverride = 5
                }
            };

            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 8
            };

            row.AddChild(new ContentLabel
            {
                Name = "NetNavigatorGuidLabel",
                Text = dev.Guid,
                MinWidth = GuidColWidth,
                MaxWidth = GuidColWidth,
                WordWrap = false,
                ClipText = true,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#00E5FF")
            });

            row.AddChild(new ContentLabel
            {
                Name = "NetNavigatorTypeLabel",
                Text = dev.DeviceType,
                MinWidth = TypeColWidth,
                MaxWidth = TypeColWidth,
                WordWrap = false,
                ClipText = true,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#BFEFFF")
            });

            row.AddChild(new ContentLabel
            {
                Name = "NetNavigatorAuthLabel",
                Text = dev.AuthSummary,
                MinWidth = AuthColWidth,
                MaxWidth = AuthColWidth,
                WordWrap = false,
                ClipText = true,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = dev.AuthRequired ? Color.FromHex("#FFD166") : Color.FromHex("#66EEAA")
            });

            var detailText = string.IsNullOrEmpty(dev.ExtraInfo)
                ? dev.Name
                : $"{dev.Name}  —  {dev.ExtraInfo}";
            row.AddChild(new ContentLabel
            {
                Name = "NetNavigatorDetailLabel",
                Text = detailText,
                ToolTip = $"{dev.Name} [{dev.Guid}] | Type: {dev.DeviceType} | Auth: {dev.AuthSummary}{(string.IsNullOrEmpty(dev.ExtraInfo) ? string.Empty : $" | {dev.ExtraInfo}")}",
                HorizontalExpand = true,
                WordWrap = false,
                ClipText = true,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#EAF8FF")
            });

            card.AddChild(row);
            _deviceListContainer.AddChild(card);
        }

        if (shown == 0)
        {
            _deviceListContainer.AddChild(new ContentLabel
            {
                Text = _lastState.Devices.Count == 0
                    ? "No discoverable devices responded to probe."
                    : "No discovered devices match your filter.",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#8899AA"),
                Margin = new Thickness(6)
            });
        }
    }
}
