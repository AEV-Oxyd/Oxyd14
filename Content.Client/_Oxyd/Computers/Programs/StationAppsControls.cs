using Content.Client._Oxyd.Computers.UI;
using Content.Client.CrewManifest.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// Notekeeper (.txt files on HDD)
// =============================================================================

public sealed class NotekeeperUiControl : ProgramUiControl
{
    private readonly ContentBoxContainer _fileBar;
    private readonly ContentLineEdit _newNameInput;
    private readonly ContentLineEdit _editorLine;
    private readonly Label _bodyPreview;
    private readonly Label _statusLabel;
    private string _currentPath = "/notes/shift_notes.txt";
    private string _currentText = string.Empty;

    public NotekeeperUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        _fileBar = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_fileBar);

        var newRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        _newNameInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "New file name..." };
        var createBtn = new ContentStandardButton { Text = "Create .txt", MinWidth = 85 };
        createBtn.OnPressed += _ =>
        {
            if (string.IsNullOrWhiteSpace(_newNameInput.Text))
                return;
            SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.CreateFile, _newNameInput.Text, ""));
            _newNameInput.Text = string.Empty;
        };
        var delBtn = new ContentStandardButton { Text = "Delete", MinWidth = 60, Margin = new Thickness(2, 0) };
        delBtn.OnPressed += _ => SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.DeleteFile, _currentPath));
        newRow.AddChild(_newNameInput);
        newRow.AddChild(createBtn);
        newRow.AddChild(delBtn);
        root.AddChild(newRow);

        _statusLabel = new Label { Text = "shift_notes.txt", FontColorOverride = Color.FromHex("#00D4FF"), Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_statusLabel);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _bodyPreview = new Label { FontColorOverride = Color.FromHex("#E0F7FA") };
        scroll.AddChild(_bodyPreview);
        root.AddChild(scroll);

        var editRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
        _editorLine = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Set/edit file text or append line..." };
        var saveBtn = new ContentStandardButton { Text = "Save", MinWidth = 60, Margin = new Thickness(2, 0) };
        saveBtn.OnPressed += _ =>
        {
            var nextText = string.IsNullOrEmpty(_currentText) ? _editorLine.Text : $"{_currentText}\n{_editorLine.Text}";
            _editorLine.Text = string.Empty;
            SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.SaveFile, _currentPath, nextText));
        };
        var replaceBtn = new ContentStandardButton { Text = "Overwrite", MinWidth = 80 };
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
            var btn = new ContentStandardButton { Text = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path, Margin = new Thickness(2, 0) };
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
    private readonly Label _statusLabel;
    private readonly ContentLineEdit _descInput;
    private readonly ContentLineEdit _forInput;
    private readonly ContentBoxContainer _tasksContainer;

    public NanoTaskUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        _statusLabel = new Label { Text = "NanoTask Checklist (/tasks/tasks.tsk)", FontColorOverride = Color.FromHex("#00D4FF") };
        root.AddChild(_statusLabel);

        var addRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4) };
        _descInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "Task description..." };
        _forInput = new ContentLineEdit { MinWidth = 90, PlaceHolder = "For..." };
        var addBtn = new ContentStandardButton { Text = "+ Task", MinWidth = 65 };
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
            row.AddChild(new Label
            {
                Text = $"[{(entry.Data.IsTaskDone ? "DONE" : "TODO")}] {entry.Data.Description} ({entry.Data.TaskIsFor})",
                HorizontalExpand = true
            });

            var toggleBtn = new ContentStandardButton { Text = entry.Data.IsTaskDone ? "Undo" : "Done", MinWidth = 52, Margin = new Thickness(1, 0) };
            toggleBtn.OnPressed += _ => SendProgramMessage(new NanoTaskActionMessage(new NanoTaskUpdateTask(new NanoTaskItemAndId(
                entry.Id,
                new NanoTaskItem(entry.Data.Description, entry.Data.TaskIsFor, !entry.Data.IsTaskDone, entry.Data.Priority)))));

            var printBtn = new ContentStandardButton { Text = "Print", MinWidth = 52, Margin = new Thickness(1, 0) };
            printBtn.OnPressed += _ => SendProgramMessage(new NanoTaskActionMessage(new NanoTaskPrintTask(entry.Data)));

            var delBtn = new ContentStandardButton { Text = "Del", MinWidth = 42 };
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
    private readonly Label _headerLabel;
    private readonly CrewManifestListing _listing;

    public CrewManifestUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        var topRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _headerLabel = new Label { Text = "Crew Manifest", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var refBtn = new ContentStandardButton { Text = "Sync Net", MinWidth = 75 };
        refBtn.OnPressed += _ => SendProgramMessage(new CrewManifestRefreshMessage());
        topRow.AddChild(_headerLabel);
        topRow.AddChild(refBtn);
        root.AddChild(topRow);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _listing = new CrewManifestListing { HorizontalExpand = true };
        scroll.AddChild(_listing);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CrewManifestUiState state)
            return;

        _headerLabel.Text = $"{state.StationName} — {state.StatusMessage}";
        _listing.RemoveAllChildren();
        if (state.Entries != null)
            _listing.AddCrewManifestEntries(state.Entries);
    }
}

// =============================================================================
// News Reader (/news/articles.db)
// =============================================================================

public sealed class NewsReaderUiControl : ProgramUiControl
{
    private readonly Label _titleLabel;
    private readonly Label _metaLabel;
    private readonly Label _bodyLabel;

    public NewsReaderUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(6) };
        var navRow = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        var prevBtn = new ContentStandardButton { Text = "< Prev", MinWidth = 65 };
        prevBtn.OnPressed += _ => SendProgramMessage(new NewsReaderActionMessage(NewsReaderUiAction.Prev));
        var nextBtn = new ContentStandardButton { Text = "Next >", MinWidth = 65, Margin = new Thickness(4, 0) };
        nextBtn.OnPressed += _ => SendProgramMessage(new NewsReaderActionMessage(NewsReaderUiAction.Next));
        var syncBtn = new ContentStandardButton { Text = "Sync", MinWidth = 55 };
        syncBtn.OnPressed += _ => SendProgramMessage(new NewsReaderActionMessage(NewsReaderUiAction.SyncNetwork));
        navRow.AddChild(prevBtn);
        navRow.AddChild(nextBtn);
        navRow.AddChild(syncBtn);
        root.AddChild(navRow);

        _titleLabel = new Label { Text = "Station News", FontColorOverride = Color.FromHex("#FFE066") };
        _metaLabel = new Label { Text = "", FontColorOverride = Color.FromHex("#88AAAA"), Margin = new Thickness(0, 2, 0, 6) };
        root.AddChild(_titleLabel);
        root.AddChild(_metaLabel);

        var scroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _bodyLabel = new Label { FontColorOverride = Color.FromHex("#E0F7FA") };
        scroll.AddChild(_bodyLabel);
        root.AddChild(scroll);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NewsReaderBoundUserInterfaceState state)
            return;

        if (state.Article == null)
        {
            _titleLabel.Text = "No News Articles";
            _metaLabel.Text = state.StatusMessage;
            _bodyLabel.Text = string.Empty;
            return;
        }

        _titleLabel.Text = $"[{state.TargetNum}/{state.TotalNum}] {state.Article.Value.Title}";
        _metaLabel.Text = $"Author: {state.Article.Value.Author ?? "Anonymous"} | {state.StatusMessage}";
        _bodyLabel.Text = FormattedMessage.RemoveMarkupOrThrow(state.Article.Value.Content);
    }
}

// =============================================================================
// Wanted List (/records/wanted_list.db)
// =============================================================================

public sealed class WantedListUiControl : ProgramUiControl
{
    private readonly Label _headerLabel;
    private readonly ContentBoxContainer _listContainer;

    public WantedListUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        var top = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _headerLabel = new Label { Text = "Security Wanted List", HorizontalExpand = true, FontColorOverride = Color.FromHex("#FF5555") };
        var refBtn = new ContentStandardButton { Text = "Sync Net", MinWidth = 75 };
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

        _headerLabel.Text = $"Wanted List ({state.Records.Count}) | {state.StatusMessage}";
        _listContainer.RemoveAllChildren();
        foreach (var rec in state.Records)
        {
            _listContainer.AddChild(new Label
            {
                Text = $"[{rec.Status}] {rec.TargetInfo.Name} ({rec.TargetInfo.JobTitle}) - {rec.Reason ?? "No reason"}",
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
        top.AddChild(new Label { Text = "NetProbe Scanner (/logs/netprobe.db)", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") });
        var clrBtn = new ContentStandardButton { Text = "Clear", MinWidth = 60 };
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
            _list.AddChild(new Label { Text = $"{d.Name} | {d.Address} | {d.Frequency} | {d.NetId}" });
    }
}

public sealed class LogProbeUiControl : ProgramUiControl
{
    private readonly Label _entityLabel;
    private readonly ContentBoxContainer _list;

    public LogProbeUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(4) };
        var top = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _entityLabel = new Label { Text = "LogProbe (/logs/logprobe.db)", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
        var printBtn = new ContentStandardButton { Text = "Print", MinWidth = 60 };
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
            _list.AddChild(new Label { Text = $"{n++}. [{TimeSpan.FromSeconds(Math.Truncate(l.Time.TotalSeconds))}] {l.Accessor}" });
    }
}

public sealed class UtilityProgramUiControl : ProgramUiControl
{
    private readonly Label _titleLabel;
    private readonly Label _descLabel;

    public UtilityProgramUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer { Orientation = ContentBoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(12) };
        _titleLabel = new Label { Text = "Peripheral Module", FontColorOverride = Color.FromHex("#00FF88") };
        _descLabel = new Label { Text = "Active.", Margin = new Thickness(0, 6) };
        root.AddChild(_titleLabel);
        root.AddChild(_descLabel);
        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not UtilityProgramUiState state)
            return;

        _titleLabel.Text = $"{state.Title} [{state.Status}]";
        _descLabel.Text = state.Description;
    }
}
