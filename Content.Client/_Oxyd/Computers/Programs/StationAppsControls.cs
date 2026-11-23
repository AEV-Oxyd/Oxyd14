using System.Globalization;
using System.Linq;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// Notekeeper (.txt files on HDD)
// =============================================================================

public sealed class NotekeeperUiControl : ProgramUiControl
{
    private readonly ContentWrapContainer _fileBar;
    private readonly ContentLineEdit _newNameInput;
    private readonly ContentLineEdit _editorLine;
    private readonly ContentLabel _bodyPreview;
    private readonly ContentLabel _statusLabel;
    private string _currentPath = "/notes/shift_notes.txt";
    private string _currentText = string.Empty;

    public NotekeeperUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        _fileBar = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_fileBar);

        var newRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _newNameInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "New file name..." };
        void SubmitCreate()
        {
            if (string.IsNullOrWhiteSpace(_newNameInput.Text))
                return;
            SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.CreateFile, _newNameInput.Text, ""));
            _newNameInput.Text = string.Empty;
        }
        _newNameInput.OnTextEntered += _ => SubmitCreate();
        var createBtn = new ContentButton { Text = "Create .txt", MinWidth = 85, Margin = new Thickness(4, 0, 0, 0) };
        createBtn.OnPressed += _ => SubmitCreate();
        var delBtn = new ContentButton { Text = "Delete", MinWidth = 60, Margin = new Thickness(4, 0, 0, 0) };
        delBtn.OnPressed += _ => SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.DeleteFile, _currentPath));
        newRow.AddChild(_newNameInput);
        newRow.AddChild(createBtn);
        newRow.AddChild(delBtn);
        root.AddChild(newRow);

        _statusLabel = new ContentLabel { Text = "shift_notes.txt", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF"), Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_statusLabel);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _bodyPreview = new ContentLabel { HorizontalExpand = true, FontColorOverride = Color.FromHex("#E0F7FA"), VerticalAlignment = VAlignment.Top };
        scroll.AddChild(_bodyPreview);
        root.AddChild(scroll);

        var editRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
        _editorLine = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Set/edit file text or append line..." };
        void SubmitSave()
        {
            var nextText = string.IsNullOrEmpty(_currentText) ? _editorLine.Text : $"{_currentText}\n{_editorLine.Text}";
            _editorLine.Text = string.Empty;
            SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.SaveFile, _currentPath, nextText));
        }
        _editorLine.OnTextEntered += _ => SubmitSave();
        var saveBtn = new ContentButton { Text = "Save", MinWidth = 58, Margin = new Thickness(4, 0, 0, 0) };
        saveBtn.OnPressed += _ => SubmitSave();
        var replaceBtn = new ContentButton { Text = "Overwrite", MinWidth = 76, Margin = new Thickness(4, 0, 0, 0) };
        replaceBtn.OnPressed += _ =>
        {
            var text = _editorLine.Text;
            _editorLine.Text = string.Empty;
            SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.SaveFile, _currentPath, text));
        };
        editRow.AddChild(_editorLine);
        editRow.AddChild(saveBtn);
        editRow.AddChild(replaceBtn);
        root.AddChild(editRow);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NotekeeperUiState state)
            return;

        _currentPath = state.CurrentFilePath;
        _currentText = state.FileContent;
        _statusLabel.Text = state.HasHardDrive ? $"{state.CurrentFilePath}{(state.IsReadOnly ? " [READ ONLY]" : "")}" : "No Hard Drive installed";
        _bodyPreview.Text = state.FileContent;

        _fileBar.RemoveAllChildren();
        foreach (var path in state.AvailableFiles)
        {
            var btn = new ContentButton { Text = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path, Margin = new Thickness(2, 0) };
            btn.OnPressed += _ => SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.OpenFile, path));
            _fileBar.AddChild(btn);
        }
    }
}

// =============================================================================
// NanoTask (/tasks/tasks.tsk on HDD)
// =============================================================================

public sealed class NanoTaskUiControl : ProgramUiControl
{
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _descInput;
    private readonly ContentLineEdit _forInput;
    private readonly ContentBoxContainer _tasksContainer;

    public NanoTaskUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        _statusLabel = new ContentLabel { Text = "NanoTask Checklist (/tasks/tasks.tsk)", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        root.AddChild(_statusLabel);

        var addRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4) };
        _descInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Task description..." };
        _forInput = new ContentLineEdit { MinWidth = 90, PlaceHolder = "For..." };
        var addBtn = new ContentButton { Text = "+ Task", MinWidth = 65 };
        addBtn.OnPressed += _ =>
        {
            if (string.IsNullOrWhiteSpace(_descInput.Text))
                return;
            var desc = _descInput.Text[..Math.Min(_descInput.Text.Length, NanoTaskItem.MaximumStringLength)];
            var req = _forInput.Text[..Math.Min(_forInput.Text.Length, NanoTaskItem.MaximumStringLength)];
            _descInput.Text = string.Empty;
            SendProgramMessage(new NanoTaskActionMessage(new NanoTaskAddTask(new NanoTaskItem(desc, req, false, NanoTaskPriority.Medium))));
        };
        addRow.AddChild(_descInput);
        addRow.AddChild(_forInput);
        addRow.AddChild(addBtn);
        root.AddChild(addRow);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _tasksContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_tasksContainer);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NanoTaskUiState state)
            return;

        _statusLabel.Text = state.HasHardDrive ? $"NanoTask ({state.Tasks.Count} saved in /tasks/tasks.tsk)" : "No Hard Drive installed!";
        _tasksContainer.RemoveAllChildren();
        foreach (var entry in state.Tasks)
        {
            var row = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 2) };
            row.AddChild(new ContentLabel
            {
                Text = $"[{(entry.Data.IsTaskDone ? "DONE" : "TODO")}] {entry.Data.Description} ({entry.Data.TaskIsFor})",
                HorizontalExpand = true
            });

            var toggleBtn = new ContentButton { Text = entry.Data.IsTaskDone ? "Undo" : "Done", MinWidth = 52, Margin = new Thickness(1, 0) };
            toggleBtn.OnPressed += _ => SendProgramMessage(new NanoTaskActionMessage(new NanoTaskUpdateTask(new NanoTaskItemAndId(
                entry.Id,
                new NanoTaskItem(entry.Data.Description, entry.Data.TaskIsFor, !entry.Data.IsTaskDone, entry.Data.Priority)))));

            var printBtn = new ContentButton { Text = "Print", MinWidth = 52, Margin = new Thickness(1, 0) };
            printBtn.OnPressed += _ => SendProgramMessage(new NanoTaskActionMessage(new NanoTaskPrintTask(entry.Data)));

            var delBtn = new ContentButton { Text = "Del", MinWidth = 42 };
            delBtn.OnPressed += _ => SendProgramMessage(new NanoTaskActionMessage(new NanoTaskDeleteTask(entry.Id)));

            row.AddChild(toggleBtn);
            row.AddChild(printBtn);
            row.AddChild(delBtn);
            _tasksContainer.AddChild(row);
        }
    }
}

// =============================================================================
// Crew Manifest (/records/crew_manifest.db)
// =============================================================================

public sealed class CrewManifestUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentBoxContainer _listing;

    public CrewManifestUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        var topRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _headerLabel = new ContentLabel { Text = "Crew Manifest", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var refBtn = new ContentButton { Text = "Sync Net", MinWidth = 75 };
        refBtn.OnPressed += _ => SendProgramMessage(new CrewManifestRefreshMessage());
        topRow.AddChild(_headerLabel);
        topRow.AddChild(refBtn);
        root.AddChild(topRow);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _listing = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_listing);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CrewManifestUiState state)
            return;

        var tsText = state.FileTimestamp is { } ts ? $" [File: {ts:hh\\:mm\\:ss}]" : string.Empty;
        _headerLabel.Text = $"{state.StationName}{tsText} — {state.StatusMessage}";
        _listing.RemoveAllChildren();
        if (state.Entries?.Entries != null)
        {
            foreach (var entry in state.Entries.Entries)
            {
                _listing.AddChild(new ContentLabel
                {
                    Text = $"{entry.Name} — {entry.JobTitle}",
                    HorizontalExpand = true,
                    FontColorOverride = Color.FromHex("#E0F7FA")
                });
            }
        }
    }
}

// =============================================================================
// News Reader (/news/articles.db)
// =============================================================================

public sealed class NewsReaderUiControl : ProgramUiControl
{
    private readonly ContentLabel _titleLabel;
    private readonly ContentLabel _metaLabel;
    private readonly ContentLabel _bodyLabel;

    public NewsReaderUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(6) };
        var navRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4, Margin = new Thickness(0, 0, 0, 4) };
        var prevBtn = new ContentButton { Text = "< Prev", MinWidth = 65 };
        prevBtn.OnPressed += _ => SendProgramMessage(new NewsReaderActionMessage(NewsReaderUiAction.Prev));
        var nextBtn = new ContentButton { Text = "Next >", MinWidth = 65 };
        nextBtn.OnPressed += _ => SendProgramMessage(new NewsReaderActionMessage(NewsReaderUiAction.Next));
        var syncBtn = new ContentButton { Text = "Sync", MinWidth = 55 };
        syncBtn.OnPressed += _ => SendProgramMessage(new NewsReaderActionMessage(NewsReaderUiAction.SyncNetwork));
        navRow.AddChild(prevBtn);
        navRow.AddChild(nextBtn);
        navRow.AddChild(syncBtn);
        root.AddChild(navRow);

        _titleLabel = new ContentLabel { Text = "Station News", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FFE066") };
        _metaLabel = new ContentLabel { Text = "", HorizontalExpand = true, FontColorOverride = Color.FromHex("#88AAAA"), Margin = new Thickness(0, 2, 0, 6) };
        root.AddChild(_titleLabel);
        root.AddChild(_metaLabel);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _bodyLabel = new ContentLabel { HorizontalExpand = true, FontColorOverride = Color.FromHex("#E0F7FA"), VerticalAlignment = VAlignment.Top };
        scroll.AddChild(_bodyLabel);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NewsReaderBoundUserInterfaceState state)
            return;

        var tsText = state.FileTimestamp is { } ts ? $" [File: {ts:hh\\:mm\\:ss}]" : string.Empty;
        if (state.Article == null)
        {
            _titleLabel.Text = "No News Articles";
            _metaLabel.Text = $"{state.StatusMessage}{tsText}";
            _bodyLabel.Text = string.Empty;
            return;
        }

        _titleLabel.Text = $"[{state.TargetNum}/{state.TotalNum}] {state.Article.Value.Title}";
        _metaLabel.Text = $"Author: {state.Article.Value.Author ?? "Anonymous"}{tsText} | {state.StatusMessage}";
        _bodyLabel.Text = FormattedMessage.RemoveMarkupPermissive(state.Article.Value.Content);
    }
}

// =============================================================================
// Wanted List (/records/wanted_list.db)
// =============================================================================

public sealed class WantedListUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentBoxContainer _listContainer;

    public WantedListUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        var top = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _headerLabel = new ContentLabel { Text = "Security Wanted List", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FF5555") };
        var refBtn = new ContentButton { Text = "Sync Net", MinWidth = 75 };
        refBtn.OnPressed += _ => SendProgramMessage(new WantedListRefreshMessage());
        top.AddChild(_headerLabel);
        top.AddChild(refBtn);
        root.AddChild(top);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _listContainer = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_listContainer);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not WantedListUiState state)
            return;

        var tsText = state.FileTimestamp is { } ts ? $" [File: {ts:hh\\:mm\\:ss}]" : string.Empty;
        _headerLabel.Text = $"Wanted List ({state.Records.Count}){tsText} | {state.StatusMessage}";
        _listContainer.RemoveAllChildren();
        foreach (var rec in state.Records)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = $"[{rec.Status}] {rec.TargetInfo.Name} ({rec.TargetInfo.JobTitle}) - {rec.Reason ?? "No reason"}",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#FFE066")
            });
        }
    }
}

// =============================================================================
// NetProbe & LogProbe & Utility Programs
// =============================================================================

public sealed class NetProbeUiControl : ProgramUiControl
{
    private readonly ContentBoxContainer _list;

    public NetProbeUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        var top = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        top.AddChild(new ContentLabel { Text = "NetProbe Scanner (/logs/netprobe.db)", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") });
        var clrBtn = new ContentButton { Text = "Clear", MinWidth = 60 };
        clrBtn.OnPressed += _ => SendProgramMessage(new NetProbeClearMessage());
        top.AddChild(clrBtn);
        root.AddChild(top);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _list = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_list);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NetProbeUiState state)
            return;

        _list.RemoveAllChildren();
        foreach (var d in state.ProbedDevices)
            _list.AddChild(new ContentLabel { Text = $"{d.Name} | {d.Address} | {d.Frequency} | {d.NetId}", HorizontalExpand = true });
    }
}

public sealed class LogProbeUiControl : ProgramUiControl
{
    private readonly ContentLabel _entityLabel;
    private readonly ContentBoxContainer _list;

    public LogProbeUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        var top = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _entityLabel = new ContentLabel { Text = "LogProbe (/logs/logprobe.db)", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var printBtn = new ContentButton { Text = "Print", MinWidth = 60 };
        printBtn.OnPressed += _ => SendProgramMessage(new LogProbePrintMessage());
        top.AddChild(_entityLabel);
        top.AddChild(printBtn);
        root.AddChild(top);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _list = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_list);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not LogProbeUiState state)
            return;

        _entityLabel.Text = string.IsNullOrEmpty(state.EntityName) ? "Scan an AccessReader in world..." : $"Logs: {state.EntityName}";
        _list.RemoveAllChildren();
        var n = 1;
        foreach (var l in state.PulledLogs)
            _list.AddChild(new ContentLabel { Text = $"{n++}. [{TimeSpan.FromSeconds(Math.Truncate(l.Time.TotalSeconds))}] {l.Accessor}", HorizontalExpand = true });
    }
}

public sealed class UtilityProgramUiControl : ProgramUiControl
{
    private readonly ContentLabel _titleLabel;
    private readonly ContentLabel _descLabel;

    public UtilityProgramUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(12) };
        _titleLabel = new ContentLabel { Text = "Peripheral Module", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00FF88") };
        _descLabel = new ContentLabel { Text = "Active.", HorizontalExpand = true, Margin = new Thickness(0, 6) };
        root.AddChild(_titleLabel);
        root.AddChild(_descLabel);
        scroll.AddChild(root);
        AddChild(scroll);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not UtilityProgramUiState state)
            return;

        _titleLabel.Text = $"{state.Title} [{state.Status}]";
        _descLabel.Text = state.Description;
    }
}

// =============================================================================
// Cargo Orders UI (/bin/cargo_orders.nte) — Bank Account ID + PIN Auth & Withdrawal
// =============================================================================

public sealed class CargoOrdersUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLabel _bankAuthStatusLabel;
    private readonly ContentLineEdit _accountIdEdit;
    private readonly ContentLineEdit _pinEdit;
    private readonly ContentButton _authBtn;
    private readonly ContentButton _logoutBtn;
    private readonly ContentLineEdit _withdrawAmountEdit;
    private readonly ContentButton _withdrawBtn;

    private readonly ContentLineEdit _searchEdit;
    private readonly ContentLineEdit _requesterEdit;
    private readonly ContentLineEdit _reasonEdit;
    private readonly ContentLineEdit _quantityEdit;
    private readonly ContentWrapContainer _categoriesWrap;
    private readonly ContentBoxContainer _productsContainer;
    private readonly ContentBoxContainer _ordersContainer;

    private CargoOrdersUiState? _lastState;

    public CargoOrdersUiControl()
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
            SeparationOverride = 6
        };
        _headerLabel = new ContentLabel
        {
            Name = "CargoOrdersHeaderLabel",
            Text = "CARGO SUPPLY & BANK ORDER CONSOLE",
            FontColorOverride = Color.FromHex("#FFD166"),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center
        };
        var refreshBtn = new ContentButton { Name = "CargoOrdersRefreshBtn", Text = "Sync", MinWidth = 72 };
        refreshBtn.OnPressed += _ => SendProgramMessage(new CargoOrdersActionMessage(CargoOrdersAction.Refresh));
        topRow.AddChild(_headerLabel);
        topRow.AddChild(refreshBtn);
        root.AddChild(topRow);

        _statusLabel = new ContentLabel
        {
            Name = "CargoOrdersStatusLabel",
            Text = "Enter Bank Account ID and PIN to authenticate and withdraw funds for orders.",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);

        // Bank Account ID + PIN Authentication & Withdrawal Bar
        var bankPanel = new ContentPanelContainer
        {
            Name = "CargoBankAuthPanel",
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#141A24"),
                BorderColor = Color.FromHex("#8A6E28"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };
        var bankCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };

        _bankAuthStatusLabel = new ContentLabel
        {
            Name = "CargoBankStatusLabel",
            Text = "BANK AUTH: LOCKED — Enter Account ID & PIN",
            FontColorOverride = Color.FromHex("#FFD166"),
            HorizontalExpand = true
        };
        bankCol.AddChild(_bankAuthStatusLabel);

        var bankInputsRow = new ContentWrapContainer
        {
            HorizontalSeparation = 6,
            VerticalSeparation = 4
        };
        _accountIdEdit = new ContentLineEdit
        {
            Name = "CargoBankAccountIdEdit",
            PlaceHolder = "Account ID (e.g. CARGO-01)...",
            MinWidth = 175f,
            MinHeight = 25f
        };
        _pinEdit = new ContentLineEdit
        {
            Name = "CargoBankPinEdit",
            PlaceHolder = "PIN (e.g. 1111)...",
            MinWidth = 115f,
            MinHeight = 25f
        };
        _authBtn = new ContentButton
        {
            Name = "CargoBankAuthButton",
            Text = "Auth Account",
            MinWidth = 104
        };
        void SubmitBankAuth()
        {
            SendProgramMessage(new CargoOrdersActionMessage(
                CargoOrdersAction.AuthenticateBank,
                accountId: _accountIdEdit.Text,
                pin: _pinEdit.Text));
        }
        _accountIdEdit.OnTextEntered += _ => SubmitBankAuth();
        _pinEdit.OnTextEntered += _ => SubmitBankAuth();
        _authBtn.OnPressed += _ => SubmitBankAuth();

        _logoutBtn = new ContentButton
        {
            Name = "CargoBankLogoutButton",
            Text = "Logout",
            MinWidth = 68,
            Disabled = true
        };
        _logoutBtn.OnPressed += _ => SendProgramMessage(new CargoOrdersActionMessage(CargoOrdersAction.LogoutBank));

        _withdrawAmountEdit = new ContentLineEdit
        {
            Name = "CargoWithdrawAmountEdit",
            PlaceHolder = "Withdraw $...",
            MinWidth = 96f,
            MinHeight = 25f
        };
        _withdrawBtn = new ContentButton
        {
            Name = "CargoWithdrawCashButton",
            Text = "Withdraw Cash",
            MinWidth = 108,
            Disabled = true
        };
        _withdrawBtn.OnPressed += _ =>
        {
            var amt = int.TryParse(_withdrawAmountEdit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : 100;
            SendProgramMessage(new CargoOrdersActionMessage(
                CargoOrdersAction.WithdrawCash,
                accountId: _accountIdEdit.Text,
                pin: _pinEdit.Text,
                quantity: amt));
        };

        bankInputsRow.AddChild(_accountIdEdit);
        bankInputsRow.AddChild(_pinEdit);
        bankInputsRow.AddChild(_authBtn);
        bankInputsRow.AddChild(_logoutBtn);
        bankInputsRow.AddChild(_withdrawAmountEdit);
        bankInputsRow.AddChild(_withdrawBtn);
        bankCol.AddChild(bankInputsRow);
        bankPanel.AddChild(bankCol);
        root.AddChild(bankPanel);

        // Order Parameters Row (Quantity, Requester, Reason, Search)
        var orderMetaRow = new ContentWrapContainer
        {
            HorizontalSeparation = 6,
            VerticalSeparation = 4
        };
        _searchEdit = new ContentLineEdit
        {
            Name = "CargoProductSearchEdit",
            PlaceHolder = "Filter catalog...",
            MinWidth = 150f,
            MinHeight = 25f
        };
        _searchEdit.OnTextChanged += _ => RebuildCatalogList();

        _quantityEdit = new ContentLineEdit
        {
            Name = "CargoOrderQuantityEdit",
            Text = "1",
            PlaceHolder = "Qty",
            MinWidth = 54f,
            MinHeight = 25f
        };
        _requesterEdit = new ContentLineEdit
        {
            Name = "CargoOrderRequesterEdit",
            PlaceHolder = "Requester name...",
            MinWidth = 130f,
            MinHeight = 25f
        };
        _reasonEdit = new ContentLineEdit
        {
            Name = "CargoOrderReasonEdit",
            PlaceHolder = "Order reason...",
            MinWidth = 150f,
            MinHeight = 25f
        };
        orderMetaRow.AddChild(_searchEdit);
        orderMetaRow.AddChild(_quantityEdit);
        orderMetaRow.AddChild(_requesterEdit);
        orderMetaRow.AddChild(_reasonEdit);
        root.AddChild(orderMetaRow);

        _categoriesWrap = new ContentWrapContainer
        {
            Name = "CargoCategoriesWrap",
            HorizontalSeparation = 4,
            VerticalSeparation = 3
        };
        root.AddChild(_categoriesWrap);

        var split = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 8
        };

        // Left: Product Catalog
        var catalogScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _productsContainer = new ContentBoxContainer
        {
            Name = "CargoProductsContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        catalogScroll.AddChild(_productsContainer);
        split.AddChild(catalogScroll);

        // Right: Active Orders Queue
        var ordersPanel = new ContentPanelContainer
        {
            MinWidth = 280f,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1620"),
                BorderColor = Color.FromHex("#244864"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5
            }
        };
        var ordersCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 4
        };
        ordersCol.AddChild(new ContentLabel
        {
            Text = "ORDERS & APPROVAL QUEUE",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true
        });
        var ordersScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _ordersContainer = new ContentBoxContainer
        {
            Name = "CargoOrdersQueueContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        ordersScroll.AddChild(_ordersContainer);
        ordersCol.AddChild(ordersScroll);
        ordersPanel.AddChild(ordersCol);
        split.AddChild(ordersPanel);

        root.AddChild(split);
        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CargoOrdersUiState state)
            return;

        _lastState = state;
        _statusLabel.Text = state.StatusMessage;
        _statusLabel.FontColorOverride = state.IsAuthenticated ? Color.FromHex("#66EEAA") : Color.FromHex("#FFD166");

        _bankAuthStatusLabel.Text = state.IsAuthenticated
            ? $"BANK AUTH: {state.AuthenticatedAccountId} ({state.AuthenticatedAccountName}) — Balance: ${state.AuthenticatedBalance}"
            : "BANK AUTH: LOCKED — Enter Bank Account ID & PIN to authorize & withdraw order funds";
        _bankAuthStatusLabel.FontColorOverride = state.IsAuthenticated ? Color.FromHex("#66EEAA") : Color.FromHex("#FFD166");

        _logoutBtn.Disabled = !state.IsAuthenticated;
        _withdrawBtn.Disabled = !state.IsAuthenticated;

        _categoriesWrap.RemoveAllChildren();
        foreach (var cat in state.Categories)
        {
            var catCopy = cat;
            var btn = new ContentButton
            {
                Text = catCopy,
                MinWidth = 56,
                Pressed = string.Equals(catCopy, state.SelectedCategory, StringComparison.OrdinalIgnoreCase)
            };
            btn.OnPressed += _ => SendProgramMessage(new CargoOrdersActionMessage(CargoOrdersAction.SelectCategory, category: catCopy));
            _categoriesWrap.AddChild(btn);
        }

        RebuildCatalogList();
        RebuildOrdersList(state);
    }

    private int GetRequestedQuantity()
    {
        return int.TryParse(_quantityEdit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) && q > 0
            ? Math.Clamp(q, 1, 20)
            : 1;
    }

    private void RebuildCatalogList()
    {
        _productsContainer.RemoveAllChildren();
        if (_lastState == null)
            return;

        var q = (_searchEdit.Text ?? string.Empty).Trim();
        var catFilter = _lastState.SelectedCategory;
        var shown = 0;

        foreach (var prod in _lastState.Products)
        {
            if (!string.IsNullOrEmpty(catFilter) &&
                !catFilter.Equals("All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(prod.Category, catFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(q) &&
                !prod.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !prod.ProductId.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !prod.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;
            var prodId = prod.ProductId;
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            row.AddChild(new ContentLabel
            {
                Text = $"{prod.Name} (${prod.Cost})",
                ToolTip = prod.Description,
                HorizontalExpand = true,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#E6F4FA")
            });

            var buyBtn = new ContentButton
            {
                Name = $"CargoOrderBuyBtn_{prodId}",
                Text = "Order & Pay",
                MinWidth = 96
            };
            buyBtn.OnPressed += _ => SendProgramMessage(new CargoOrdersActionMessage(
                CargoOrdersAction.PlaceAndPayOrder,
                accountId: _accountIdEdit.Text,
                pin: _pinEdit.Text,
                productId: prodId,
                quantity: GetRequestedQuantity(),
                requester: _requesterEdit.Text,
                reason: _reasonEdit.Text));

            var queueBtn = new ContentButton
            {
                Name = $"CargoOrderQueueBtn_{prodId}",
                Text = "Request",
                MinWidth = 68
            };
            queueBtn.OnPressed += _ => SendProgramMessage(new CargoOrdersActionMessage(
                CargoOrdersAction.QueueOrder,
                productId: prodId,
                quantity: GetRequestedQuantity(),
                requester: _requesterEdit.Text,
                reason: _reasonEdit.Text));

            row.AddChild(buyBtn);
            row.AddChild(queueBtn);
            _productsContainer.AddChild(row);
        }

        if (shown == 0)
        {
            _productsContainer.AddChild(new ContentLabel
            {
                Text = "No matching cargo products.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
        }
    }

    private void RebuildOrdersList(CargoOrdersUiState state)
    {
        _ordersContainer.RemoveAllChildren();
        if (state.Orders.Count == 0)
        {
            _ordersContainer.AddChild(new ContentLabel
            {
                Text = "No active orders in queue.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
            return;
        }

        foreach (var o in state.Orders)
        {
            var orderId = o.OrderId;
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4
            };

            var statusTag = o.Approved ? "[PAID]" : "[PENDING]";
            row.AddChild(new ContentLabel
            {
                Text = $"#{o.OrderId} {statusTag} {o.Quantity}x {o.ProductName} (${o.TotalCost})",
                HorizontalExpand = true,
                FontColorOverride = o.Approved ? Color.FromHex("#66EEAA") : Color.FromHex("#FFD166")
            });

            if (!o.Approved)
            {
                var appBtn = new ContentButton
                {
                    Name = $"CargoOrderApproveBtn_{orderId}",
                    Text = "Pay",
                    MinWidth = 48
                };
                appBtn.OnPressed += _ => SendProgramMessage(new CargoOrdersActionMessage(
                    CargoOrdersAction.ApproveOrder,
                    accountId: _accountIdEdit.Text,
                    pin: _pinEdit.Text,
                    orderId: orderId));
                row.AddChild(appBtn);
            }

            var delBtn = new ContentButton
            {
                Name = $"CargoOrderRemoveBtn_{orderId}",
                Text = "X",
                MinWidth = 26
            };
            delBtn.OnPressed += _ => SendProgramMessage(new CargoOrdersActionMessage(
                CargoOrdersAction.RemoveOrder,
                orderId: orderId));
            row.AddChild(delBtn);

            _ordersContainer.AddChild(row);
        }
    }
}

// =============================================================================
// Crew Monitor UI (/bin/crew_monitor.nte)
// =============================================================================

public sealed class CrewMonitorUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentBoxContainer _listContainer;

    public CrewMonitorUiControl()
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

        var top = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true
        };
        _headerLabel = new ContentLabel
        {
            Text = "CREW SUIT SENSOR TELEMETRY",
            FontColorOverride = Color.FromHex("#66EEAA"),
            HorizontalExpand = true
        };
        var refBtn = new ContentButton { Text = "Refresh", MinWidth = 72 };
        refBtn.OnPressed += _ => SendProgramMessage(new CrewMonitorRefreshMessage());
        top.AddChild(_headerLabel);
        top.AddChild(refBtn);
        root.AddChild(top);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _listContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        scroll.AddChild(_listContainer);
        root.AddChild(scroll);
        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CrewMonitorUiState state)
            return;

        _headerLabel.Text = $"{state.StationName} — SUIT SENSORS ({state.Sensors.Count})";
        _listContainer.RemoveAllChildren();
        if (state.Sensors.Count == 0)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = "No active crew suit sensors transmitting on this map.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
            return;
        }

        foreach (var s in state.Sensors)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = $"{s.Name} ({s.JobTitle}) | {(s.IsAlive ? "ALIVE" : "DECEASED")} | Dmg: {s.TotalDamage} | Pos: {s.CoordinatesText}",
                FontColorOverride = s.IsAlive ? Color.FromHex("#88EECC") : Color.FromHex("#FF6666"),
                HorizontalExpand = true
            });
        }
    }
}

// =============================================================================
// Communications Console UI (/bin/comms_console.nte)
// =============================================================================

public sealed class CommsConsoleUiControl : ProgramUiControl
{
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _announcementEdit;
    private readonly ContentButton _broadcastBtn;
    private readonly ContentWrapContainer _alertLevelsWrap;

    public CommsConsoleUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
            SeparationOverride = 6
        };

        root.AddChild(new ContentLabel
        {
            Text = "STATION COMMUNICATIONS & ALERT CONSOLE",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true
        });

        _statusLabel = new ContentLabel
        {
            Name = "CommsStatusLabel",
            Text = "Ready",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);

        _announcementEdit = new ContentLineEdit
        {
            Name = "CommsAnnouncementEdit",
            PlaceHolder = "Enter station-wide announcement...",
            HorizontalExpand = true,
            MinHeight = 28f
        };
        _broadcastBtn = new ContentButton
        {
            Name = "CommsBroadcastButton",
            Text = "Broadcast Announcement",
            MinWidth = 160
        };
        _broadcastBtn.OnPressed += _ =>
        {
            SendProgramMessage(new CommsConsoleActionMessage(CommsConsoleAction.BroadcastAnnouncement, _announcementEdit.Text));
            _announcementEdit.Text = string.Empty;
        };

        root.AddChild(_announcementEdit);
        root.AddChild(_broadcastBtn);

        root.AddChild(new ContentLabel
        {
            Text = "STATION ALERT LEVEL:",
            FontColorOverride = Color.FromHex("#FFD166")
        });
        _alertLevelsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 6,
            VerticalSeparation = 4
        };
        root.AddChild(_alertLevelsWrap);
        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CommsConsoleUiState state)
            return;

        _statusLabel.Text = $"Current Alert: {state.CurrentAlertLevel.ToUpperInvariant()} | {state.StatusMessage}";
        _broadcastBtn.Disabled = !state.CanBroadcast;

        _alertLevelsWrap.RemoveAllChildren();
        foreach (var lvl in state.AvailableAlertLevels)
        {
            var lvlCopy = lvl;
            var btn = new ContentButton
            {
                Name = $"CommsAlertBtn_{lvlCopy}",
                Text = lvlCopy.ToUpperInvariant(),
                Pressed = string.Equals(lvlCopy, state.CurrentAlertLevel, StringComparison.OrdinalIgnoreCase),
                Disabled = !state.CanBroadcast,
                MinWidth = 76
            };
            btn.OnPressed += _ => SendProgramMessage(new CommsConsoleActionMessage(CommsConsoleAction.SetAlertLevel, lvlCopy));
            _alertLevelsWrap.AddChild(btn);
        }
    }
}

// =============================================================================
// Solar Control Console UI (/bin/solar_control.nte)
// =============================================================================

public sealed class SolarControlUiControl : ProgramUiControl
{
    private readonly ContentLabel _telemetryLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _angleEdit;
    private readonly ContentLineEdit _velocityEdit;

    public SolarControlUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
            SeparationOverride = 6
        };

        root.AddChild(new ContentLabel
        {
            Text = "SOLAR ARRAY TRACKING CONTROLLER",
            FontColorOverride = Color.FromHex("#FFE066"),
            HorizontalExpand = true
        });

        _telemetryLabel = new ContentLabel
        {
            Name = "SolarTelemetryLabel",
            Text = "Syncing solar array...",
            FontColorOverride = Color.FromHex("#E0F7FA"),
            HorizontalExpand = true
        };
        root.AddChild(_telemetryLabel);

        var angleRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _angleEdit = new ContentLineEdit
        {
            Name = "SolarAngleEdit",
            PlaceHolder = "Panel Angle (deg)...",
            HorizontalExpand = true
        };
        var setAngleBtn = new ContentButton { Name = "SolarSetAngleBtn", Text = "Set Angle", MinWidth = 96 };
        setAngleBtn.OnPressed += _ =>
        {
            if (float.TryParse(_angleEdit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var deg))
                SendProgramMessage(new SolarControlActionMessage(SolarControlAction.SetAngle, deg));
        };
        angleRow.AddChild(_angleEdit);
        angleRow.AddChild(setAngleBtn);
        root.AddChild(angleRow);

        var velRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _velocityEdit = new ContentLineEdit
        {
            Name = "SolarVelocityEdit",
            PlaceHolder = "Tracking Velocity (deg/min)...",
            HorizontalExpand = true
        };
        var setVelBtn = new ContentButton { Name = "SolarSetVelocityBtn", Text = "Set Velocity", MinWidth = 96 };
        setVelBtn.OnPressed += _ =>
        {
            if (float.TryParse(_velocityEdit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vel))
                SendProgramMessage(new SolarControlActionMessage(SolarControlAction.SetVelocity, vel));
        };
        velRow.AddChild(_velocityEdit);
        velRow.AddChild(setVelBtn);
        root.AddChild(velRow);

        _statusLabel = new ContentLabel
        {
            Text = "",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);
        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not SolarControlUiState state)
            return;

        _telemetryLabel.Text =
            $"Panel Angle: {state.PanelAngleDegrees:0.0}°\n" +
            $"Tracking Velocity: {state.AngularVelocityDegreesPerMin:0.00}°/min\n" +
            $"Sun Direction: {state.SunAngleDegrees:0.0}°\n" +
            $"Total Solar Output: {state.TotalPowerWatts:0} W";
        _statusLabel.Text = state.StatusMessage;
    }
}
