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

        _pathLabel.Text = $"{state.CurrentDirectory} | HDD: {state.StorageUsedBytes / 1024}/{state.StorageTotalBytes / 1024}KB";
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
                Text = $"{file.Name}.{file.Extension} ({file.Size}B) [{file.LastModified:hh\\:mm\\:ss}]{flags}",
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
        _statusLabel = new ContentLabel { Text = "Ready", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFE066"), Margin = new Thickness(0, 2, 0, 4) };
        root.AddChild(_headerLabel);
        root.AddChild(_statusLabel);

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

        _headerLabel.Text = $"Server: {state.ServerName} [{state.ServerGuid}] | DL:{state.DownloadBps / 1024}KB/s UL:{state.UploadBps / 1024}KB/s | {(state.IsServerOnline ? "ONLINE" : "OFFLINE")}";
        _statusLabel.Text = state.StatusMessage;

        _listContainer.RemoveAllChildren();
        _listContainer.AddChild(new ContentLabel { Text = "--- REMOTE SERVER REPOSITORY ---", FontColorOverride = Color.FromHex("#00D4FF") });

        if (!state.IsServerOnline)
        {
            _listContainer.AddChild(new ContentLabel { Text = "(Cannot reach online data server — check NIC & router)", FontColorOverride = Color.FromHex("#FF6666") });
        }
        else
        {
            foreach (var item in state.Items)
            {
                var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
                row.AddChild(new ContentLabel { Text = $"{item.Name} [{item.Category}] ({item.Size}B)", HorizontalExpand = true });

                var dlBtn = new ContentButton
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
            _listContainer.AddChild(new ContentLabel { Text = "--- UPLOAD LOCAL FILES TO SERVER ---", FontColorOverride = Color.FromHex("#00FF88"), Margin = new Thickness(0, 8, 0, 2) });
            foreach (var local in state.LocalUploadables)
            {
                var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
                row.AddChild(new ContentLabel { Text = $"{local.Path} ({local.Size}B)", HorizontalExpand = true });

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

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

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
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not RouterUiState state)
            return;

        _headerLabel.Text = $"ROUTER [{state.RouterGuid}] | {(state.IsPowered ? "ONLINE" : "OFFLINE")} | DL:{state.BandwidthBps / 1024}KB/s UL:{state.UploadBps / 1024}KB/s";
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
    private readonly ContentLineEdit _targetInput;
    private readonly ContentLineEdit _payloadInput;

    // Inline Packet Editor Panel
    private readonly ContentPanelContainer _editorPanel;
    private readonly ContentLabel _editorHeaderLabel;
    private readonly ContentLineEdit _editTargetInput;
    private readonly ContentBoxContainer _editEntriesContainer;
    private readonly ContentLineEdit _newEntryKeyInput;
    private readonly ContentLineEdit _newEntryValInput;

    private PacketSnifferUiState? _lastState;
    private bool _relevantOnly = true;
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

        var emitRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _targetInput = new ContentLineEdit { MinWidth = 90, PlaceHolder = "Target GUID" };
        _payloadInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Raw packet payload...", Margin = new Thickness(4, 0, 0, 0) };
        var emitBtn = new ContentButton { Text = "Emit", MinWidth = 55, Margin = new Thickness(4, 0, 0, 0) };
        emitBtn.OnPressed += _ => SendProgramMessage(new PacketSnifferActionMessage(PacketSnifferAction.EmitPacket, targetGuid: _targetInput.Text, payload: _payloadInput.Text));
        emitRow.AddChild(_targetInput);
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
        _editTargetInput = new ContentLineEdit { Name = "SnifferEditTargetInput", HorizontalExpand = true, PlaceHolder = "Destination GUID..." };
        targetRow.AddChild(_editTargetInput);
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
        _newEntryKeyInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Key (e.g. Name)..." };
        _newEntryValInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Value (e.g. Job Title)..." };
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
        var sendTargetBtn = new ContentButton { Name = "SnifferTransmitModifiedButton", Text = "Transmit to Target" };
        sendTargetBtn.OnPressed += _ => SubmitEditedPacket(applyToSource: false);

        var poisonSrvBtn = new ContentButton { Name = "SnifferPoisonServerButton", Text = "Poison Server Copy" };
        poisonSrvBtn.OnPressed += _ => SubmitEditedPacket(applyToSource: true);

        var closeEdBtn = new ContentButton { Text = "Close Editor" };
        closeEdBtn.OnPressed += _ => CloseEditor();

        edActions.AddChild(sendTargetBtn);
        edActions.AddChild(poisonSrvBtn);
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
        _editTargetInput.Text = pkt.TargetAddress;

        _editingEntries.Clear();
        foreach (var (k, v) in pkt.EditableEntries)
            _editingEntries[k] = v;

        _editorHeaderLabel.Text = $"EDIT [{pkt.PacketId}] {pkt.Protocol} ({pkt.FilePath}) | Src: {pkt.SourceAddress}";
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
                Text = "(No entries — add a key/value entry below)",
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

    private void SubmitEditedPacket(bool applyToSource)
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
            applyToSource: applyToSource));
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not PacketSnifferUiState state)
            return;

        _lastState = state;
        var relCount = state.Packets.Count(p => p.IsPlayerRelevant);
        _headerLabel.Text = $"Sniffer [{state.RouterGuid}] | {(state.HasNetworkCard ? "NIC UP" : "NO NIC")} | Relevant: {relCount}/{state.Packets.Count} | {state.StatusMessage}";
        RebuildPacketList();
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
                    ? "(No player-relevant packets captured yet — click 'Filter: Relevant Only' to inspect all traffic)"
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
