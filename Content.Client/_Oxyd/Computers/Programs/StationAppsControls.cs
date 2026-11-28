using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.CrewManifest;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// Notekeeper (.txt files on HDD)
// =============================================================================

public sealed class NotekeeperUiControl : ProgramUiControl
{
    private readonly ContentWrapContainer _fileBar;
    private readonly ContentLineEdit _newNameInput;
    private readonly TextEdit _editor;
    private readonly ContentButton _saveBtn;
    private readonly ContentButton _printBtn;
    private readonly ContentButton _delBtn;
    private readonly ContentLabel _statusLabel;
    private string _currentPath = "/notes/shift_notes.txt";
    private string _loadedPath = string.Empty;
    private string _lastServerContent = string.Empty;
    private bool _hasUnsavedEdits;
    private bool _suppressEditorChanged;
    private bool _hasLoadedOnce;

    public NotekeeperUiControl()
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
        _fileBar = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        root.AddChild(_fileBar);

        var toolbarRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        _newNameInput = new ContentLineEdit { HorizontalExpand = true, PlaceHolder = "New file name..." };
        void SubmitCreate()
        {
            if (string.IsNullOrWhiteSpace(_newNameInput.Text))
                return;
            SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.CreateFile, _newNameInput.Text, string.Empty));
            _newNameInput.Text = string.Empty;
        }
        _newNameInput.OnTextEntered += _ => SubmitCreate();

        var createBtn = new ContentButton { Name = "NotekeeperCreateButton", Text = "Create .txt", MinWidth = 82 };
        createBtn.OnPressed += _ => SubmitCreate();

        _saveBtn = new ContentButton { Name = "NotekeeperSaveButton", Text = "Save", MinWidth = 58 };
        _saveBtn.OnPressed += _ => SubmitSave();

        _printBtn = new ContentButton
        {
            Name = "NotekeeperPrintButton",
            Text = "Print",
            MinWidth = 58,
            MouseFilter = MouseFilterMode.Stop
        };
        _printBtn.OnPressed += _ => SubmitPrint();

        _delBtn = new ContentButton { Name = "NotekeeperDeleteButton", Text = "Delete", MinWidth = 60 };
        _delBtn.OnPressed += _ => SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.DeleteFile, _currentPath));

        toolbarRow.AddChild(_newNameInput);
        toolbarRow.AddChild(createBtn);
        toolbarRow.AddChild(_saveBtn);
        toolbarRow.AddChild(_printBtn);
        toolbarRow.AddChild(_delBtn);
        root.AddChild(toolbarRow);

        _statusLabel = new ContentLabel
        {
            Name = "NotekeeperStatusLabel",
            Text = "shift_notes.txt",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        };
        root.AddChild(_statusLabel);

        var editorPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0A131E"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        _editor = new TextEdit
        {
            Name = "NotekeeperEditor",
            HorizontalExpand = true,
            VerticalExpand = true,
            Editable = true
        };
        _editor.OnTextChanged += _ =>
        {
            if (_suppressEditorChanged)
                return;
            _hasUnsavedEdits = true;
            if (!_statusLabel.Text.Contains("[MODIFIED]", StringComparison.Ordinal))
                _statusLabel.Text = $"{_currentPath} [MODIFIED]";
        };
        _editor.OnKeyBindDown += args =>
        {
            if (args.Function == EngineKeyFunctions.MultilineTextSubmit && !_saveBtn.Disabled)
            {
                SubmitSave();
                args.Handle();
            }
        };

        editorPanel.AddChild(_editor);
        root.AddChild(editorPanel);

        AddChild(root);
    }

    private void SubmitSave()
    {
        var text = Rope.Collapse(_editor.TextRope);
        _hasUnsavedEdits = false;
        _lastServerContent = text;
        SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.SaveFile, _currentPath, text));
    }

    private void SubmitPrint()
    {
        var text = Rope.Collapse(_editor.TextRope);
        SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.PrintFile, _currentPath, text));
    }

    private void SetEditorText(string text)
    {
        _suppressEditorChanged = true;
        try
        {
            _editor.TextRope = Rope.Leaf.Empty;
            _editor.CursorPosition = new TextEdit.CursorPos();
            if (!string.IsNullOrEmpty(text))
                _editor.InsertAtCursor(text);
        }
        finally
        {
            _suppressEditorChanged = false;
        }
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not NotekeeperUiState state)
            return;

        var switchedFile = !_hasLoadedOnce || !string.Equals(_loadedPath, state.CurrentFilePath, StringComparison.Ordinal);
        var serverChanged = !string.Equals(_lastServerContent, state.FileContent, StringComparison.Ordinal);

        _currentPath = state.CurrentFilePath;
        _editor.Editable = state.HasHardDrive && !state.IsReadOnly;
        _saveBtn.Disabled = !state.HasHardDrive || state.IsReadOnly;
        _delBtn.Disabled = !state.HasHardDrive || state.IsReadOnly;

        _printBtn.Disabled = !state.HasHardDrive || !state.HasPrinterModule;
        _printBtn.ToolTip = !state.HasHardDrive
            ? "No Hard Drive installed."
            : state.HasPrinterModule
                ? "Print current note to paper."
                : "Paper printer module required.";

        if (switchedFile || (!_hasUnsavedEdits && serverChanged))
        {
            SetEditorText(state.FileContent);
            _loadedPath = state.CurrentFilePath;
            _lastServerContent = state.FileContent;
            _hasUnsavedEdits = false;
            _hasLoadedOnce = true;
        }
        else if (serverChanged)
        {
            _lastServerContent = state.FileContent;
        }

        if (!state.HasHardDrive)
        {
            _statusLabel.Text = "No Hard Drive installed";
        }
        else
        {
            var roTag = state.IsReadOnly ? " [READ ONLY]" : _hasUnsavedEdits ? " [MODIFIED]" : string.Empty;
            var msgSuffix = !string.IsNullOrWhiteSpace(state.StatusMessage) ? $" — {state.StatusMessage}" : string.Empty;
            _statusLabel.Text = $"{state.CurrentFilePath}{roTag}{msgSuffix}";
        }

        _fileBar.RemoveAllChildren();
        foreach (var path in state.AvailableFiles)
        {
            var isCurrent = string.Equals(path, state.CurrentFilePath, StringComparison.Ordinal);
            var btn = new ContentButton
            {
                Text = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path,
                Margin = new Thickness(2, 0),
                Pressed = isCurrent
            };
            var targetPath = path;
            btn.OnPressed += _ =>
            {
                _hasUnsavedEdits = false;
                SendProgramMessage(new NotekeeperActionMessage(NotekeeperUiAction.OpenFile, targetPath));
            };
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
        _statusLabel = new ContentLabel { Text = "NanoTask", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
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

        var statusSuffix = !string.IsNullOrWhiteSpace(state.StatusMessage) ? $" — {state.StatusMessage}" : string.Empty;
        _statusLabel.Text = state.HasHardDrive
            ? $"NanoTask ({state.Tasks.Count}){statusSuffix}"
            : "No Hard Drive installed!";
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

            var printBtn = new ContentButton
            {
                Name = $"NanoTaskPrintButton_{entry.Id}",
                Text = "Print",
                MinWidth = 52,
                Margin = new Thickness(1, 0),
                Disabled = !state.HasPrinterModule,
                MouseFilter = MouseFilterMode.Stop,
                ToolTip = state.HasPrinterModule ? "Print task slip." : "Paper printer module required."
            };
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
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    private readonly SpriteSystem _sprite;

    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _searchInput;
    private readonly ContentBoxContainer _listing;
    private CrewManifestUiState? _lastState;

    public CrewManifestUiControl()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entMan.System<SpriteSystem>();

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
            Name = "CrewManifestHeaderLabel",
            Text = "CREW MANIFEST",
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#66EEFF")
        };
        _searchInput = new ContentLineEdit
        {
            Name = "CrewManifestSearchInput",
            PlaceHolder = "Filter name or job...",
            MinWidth = 148f,
            MinHeight = 24f,
            VerticalAlignment = VAlignment.Center
        };
        _searchInput.OnTextChanged += _ => RebuildManifestList();
        var refBtn = new ContentButton
        {
            Name = "CrewManifestSyncButton",
            Text = "Sync Net",
            MinWidth = 72,
            MinHeight = 24f
        };
        refBtn.OnPressed += _ => SendProgramMessage(new CrewManifestRefreshMessage());
        topRow.AddChild(_headerLabel);
        topRow.AddChild(_searchInput);
        topRow.AddChild(refBtn);
        root.AddChild(topRow);

        _statusLabel = new ContentLabel
        {
            Name = "CrewManifestStatusLabel",
            Text = string.Empty,
            HorizontalExpand = true,
            WordWrap = false,
            ClipText = true,
            FontColorOverride = Color.FromHex("#88AAB8")
        };
        root.AddChild(_statusLabel);

        var scroll = new ContentScrollContainer
        {
            Name = "CrewManifestScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            AlwaysShowBackground = true
        };
        _listing = new ContentBoxContainer
        {
            Name = "CrewManifestListing",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
            Margin = new Thickness(4)
        };
        scroll.AddChild(_listing);
        root.AddChild(scroll);

        AddChild(root);
    }

    private Texture ResolveJobIconTexture(CrewManifestEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.JobIcon) &&
            !string.Equals(entry.JobIcon, "JobIconUnknown", StringComparison.OrdinalIgnoreCase) &&
            _proto.TryIndex<JobIconPrototype>(entry.JobIcon, out var iconProto))
        {
            return _sprite.Frame0(iconProto.Icon);
        }

        if (!string.IsNullOrWhiteSpace(entry.JobPrototype) &&
            _proto.TryIndex<JobPrototype>(entry.JobPrototype, out var jobProto) &&
            _proto.TryIndex<JobIconPrototype>(jobProto.Icon, out var fromJobProto))
        {
            return _sprite.Frame0(fromJobProto.Icon);
        }

        if (!string.IsNullOrWhiteSpace(entry.JobTitle))
        {
            foreach (var job in _proto.EnumeratePrototypes<JobPrototype>())
            {
                if ((string.Equals(job.LocalizedName, entry.JobTitle, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(job.ID, entry.JobTitle, StringComparison.OrdinalIgnoreCase)) &&
                    _proto.TryIndex<JobIconPrototype>(job.Icon, out var byTitleIcon))
                {
                    return _sprite.Frame0(byTitleIcon.Icon);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.JobIcon) &&
            _proto.TryIndex<JobIconPrototype>(entry.JobIcon, out var unknownProto))
        {
            return _sprite.Frame0(unknownProto.Icon);
        }

        if (_proto.TryIndex<JobIconPrototype>("JobIconNoId", out var fallbackProto))
            return _sprite.Frame0(fallbackProto.Icon);

        return _sprite.Frame0(new SpriteSpecifier.Rsi(new ResPath("Interface/Misc/job_icons.rsi"), "NoId"));
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CrewManifestUiState state)
            return;

        _lastState = state;
        var total = state.Entries?.Entries?.Length ?? 0;
        _headerLabel.Text = $"{state.StationName} — CREW MANIFEST ({total})";
        var tsText = state.FileTimestamp is { } ts ? $" [{ts:hh\\:mm\\:ss}]" : string.Empty;
        _statusLabel.Text = $"{state.StatusMessage}{tsText}";
        _statusLabel.FontColorOverride = state.IsOnlineSync ? Color.FromHex("#77D4B0") : Color.FromHex("#FFD166");

        RebuildManifestList();
    }

    private void RebuildManifestList()
    {
        _listing.RemoveAllChildren();
        var entries = _lastState?.Entries?.Entries;
        if (entries == null || entries.Length == 0)
        {
            _listing.AddChild(new ContentLabel
            {
                Text = "No crew members registered on the station manifest.",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#8899AA"),
                Margin = new Thickness(4)
            });
            return;
        }

        var query = (_searchInput.Text ?? string.Empty).Trim();
        var shown = 0;

        foreach (var entry in entries)
        {
            var jobTitle = string.IsNullOrWhiteSpace(entry.JobTitle) ? "Crew" : entry.JobTitle;
            if (!string.IsNullOrEmpty(query) &&
                !entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !jobTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;
            var card = new ContentPanelContainer
            {
                Name = "CrewManifestEntryCard",
                HorizontalExpand = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#0F1B28"),
                    BorderColor = Color.FromHex("#1E4660"),
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

            var iconFrame = new ContentPanelContainer
            {
                MinSize = new Vector2(26, 26),
                MaxSize = new Vector2(26, 26),
                VerticalAlignment = VAlignment.Center,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#09111A"),
                    BorderColor = Color.FromHex("#244E68"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 2,
                    ContentMarginRightOverride = 2,
                    ContentMarginTopOverride = 2,
                    ContentMarginBottomOverride = 2
                }
            };
            iconFrame.AddChild(new ContentTextureRect
            {
                Name = "CrewManifestJobIcon",
                Texture = ResolveJobIconTexture(entry),
                DisplaySize = new Vector2(20, 20),
                MinSize = new Vector2(20, 20),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center
            });
            row.AddChild(iconFrame);

            row.AddChild(new ContentLabel
            {
                Name = "CrewManifestNameLabel",
                Text = entry.Name,
                HorizontalExpand = true,
                WordWrap = false,
                ClipText = true,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#EAF8FF")
            });

            var jobBadge = new ContentPanelContainer
            {
                VerticalAlignment = VAlignment.Center,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#132A3C"),
                    BorderColor = Color.FromHex("#2B6280"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 8,
                    ContentMarginRightOverride = 8,
                    ContentMarginTopOverride = 2,
                    ContentMarginBottomOverride = 2
                }
            };
            jobBadge.AddChild(new ContentLabel
            {
                Name = "CrewManifestJobLabel",
                Text = jobTitle,
                WordWrap = false,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#88E6FF")
            });
            row.AddChild(jobBadge);

            card.AddChild(row);
            _listing.AddChild(card);
        }

        if (shown == 0)
        {
            _listing.AddChild(new ContentLabel
            {
                Text = "No crew entries match your filter.",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#8899AA"),
                Margin = new Thickness(4)
            });
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
        if (state.Records.Count == 0)
        {
            var emptyCard = new ContentPanelContainer
            {
                Name = "WantedListEmptyPanel",
                HorizontalExpand = true,
                Margin = new Thickness(0, 8, 0, 0),
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#0D1824"),
                    BorderColor = Color.FromHex("#244257"),
                    BorderThickness = new Thickness(1),
                    ContentMarginLeftOverride = 12,
                    ContentMarginRightOverride = 12,
                    ContentMarginTopOverride = 14,
                    ContentMarginBottomOverride = 14
                }
            };
            emptyCard.AddChild(new ContentLabel
            {
                Name = "WantedListEmptyLabel",
                Text = "No criminals currently marked.",
                HorizontalAlignment = HAlignment.Center,
                FontColorOverride = Color.FromHex("#88B8CC")
            });
            _listContainer.AddChild(emptyCard);
            return;
        }

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
        top.AddChild(new ContentLabel { Text = "NetProbe Scanner", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") });
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
        _entityLabel = new ContentLabel { Text = "LogProbe", HorizontalExpand = true, FontColorOverride = Color.FromHex("#00D4FF") };
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

        _entityLabel.Text = string.IsNullOrEmpty(state.EntityName) ? "No access log loaded." : $"Logs: {state.EntityName}";
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
    private readonly IPrototypeManager _proto;

    private CargoOrdersUiState? _lastState;

    public CargoOrdersUiControl()
    {
        _proto = IoCManager.Resolve<IPrototypeManager>();
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
            Text = "BANK & SUPPLY CONSOLE",
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
            Text = string.Empty,
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true,
            Visible = false
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
            Text = "Account: Locked — enter Account ID & PIN",
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
            PlaceHolder = "Account ID...",
            MinWidth = 175f,
            MinHeight = 25f
        };
        _pinEdit = new ContentLineEdit
        {
            Name = "CargoBankPinEdit",
            PlaceHolder = "PIN...",
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
        _statusLabel.Visible = !string.IsNullOrWhiteSpace(state.StatusMessage);
        _statusLabel.FontColorOverride = state.IsAuthenticated ? Color.FromHex("#66EEAA") : Color.FromHex("#FFD166");

        _bankAuthStatusLabel.Text = state.IsAuthenticated
            ? $"Account: {state.AuthenticatedAccountId} ({state.AuthenticatedAccountName}) | Balance: ${state.AuthenticatedBalance}"
            : "Account: Locked — enter Account ID & PIN";
        _bankAuthStatusLabel.FontColorOverride = state.IsAuthenticated ? Color.FromHex("#66EEAA") : Color.FromHex("#FFD166");

        _logoutBtn.Disabled = !state.IsAuthenticated;
        _withdrawBtn.Disabled = !state.IsAuthenticated;

        var allowedGroups = state.AllowedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All" };
        foreach (var prod in _proto.EnumeratePrototypes<CargoProductPrototype>())
        {
            if (allowedGroups.Count > 0 && !allowedGroups.Contains(prod.Group.Id))
                continue;

            var cat = string.IsNullOrWhiteSpace(prod.Category) ? "General" : Loc.GetString(prod.Category);
            categorySet.Add(cat);
        }

        _categoriesWrap.RemoveAllChildren();
        foreach (var catCopy in categorySet.OrderBy(c => c == "All" ? string.Empty : c, StringComparer.OrdinalIgnoreCase))
        {
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

        var allowedGroups = _lastState.AllowedGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var q = (_searchEdit.Text ?? string.Empty).Trim();
        var catFilter = _lastState.SelectedCategory;
        var shown = 0;

        var products = _proto.EnumeratePrototypes<CargoProductPrototype>()
            .Where(p => allowedGroups.Count == 0 || allowedGroups.Contains(p.Group.Id))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var prod in products)
        {
            var cat = string.IsNullOrWhiteSpace(prod.Category) ? "General" : Loc.GetString(prod.Category);
            if (!string.IsNullOrEmpty(catFilter) &&
                !catFilter.Equals("All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cat, catFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(q) &&
                !prod.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !prod.ID.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !prod.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;
            var prodId = prod.ID;
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

            var prodName = _proto.TryIndex<CargoProductPrototype>(o.ProductId, out var pp) ? pp.Name : o.ProductId;
            var totalCost = (pp?.Cost ?? 0) * o.Quantity;
            var statusTag = o.Approved ? "[PAID]" : "[PENDING]";
            row.AddChild(new ContentLabel
            {
                Text = $"#{o.OrderId} {statusTag} {o.Quantity}x {prodName} (${totalCost})",
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
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    private readonly SpriteSystem _sprite;

    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _selectedInfoLabel;
    private readonly ContentButton _listModeBtn;
    private readonly ContentButton _mapModeBtn;
    private readonly ContentScrollContainer _listScroll;
    private readonly ContentBoxContainer _listContainer;
    private readonly OxydDeviceNavMapControl _mapSelector;
    private CrewMonitorUiState? _lastState;
    private bool _mapMode;
    private string _selectedCrewName = string.Empty;

    public CrewMonitorUiControl()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entMan.System<SpriteSystem>();

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
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        _headerLabel = new ContentLabel
        {
            Text = "CREW SUIT SENSOR TELEMETRY",
            FontColorOverride = Color.FromHex("#66EEAA"),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center
        };
        _listModeBtn = new ContentButton
        {
            Name = "CrewViewModeListButton",
            Text = "List Mode",
            Pressed = true,
            MinWidth = 84
        };
        _listModeBtn.OnPressed += _ => SetMapMode(false);

        _mapModeBtn = new ContentButton
        {
            Name = "CrewViewModeToggleButton",
            Text = "Map Mode",
            Pressed = false,
            MinWidth = 84
        };
        _mapModeBtn.OnPressed += _ => SetMapMode(true);

        top.AddChild(_headerLabel);
        top.AddChild(_listModeBtn);
        top.AddChild(_mapModeBtn);
        root.AddChild(top);

        _selectedInfoLabel = new ContentLabel
        {
            Name = "CrewMonitorSelectedLabel",
            Text = "Syncing suit sensors...",
            FontColorOverride = Color.FromHex("#88D4EE"),
            HorizontalExpand = true
        };
        root.AddChild(_selectedInfoLabel);

        _listScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _listContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
        };
        _listScroll.AddChild(_listContainer);
        root.AddChild(_listScroll);

        _mapSelector = new OxydDeviceNavMapControl("Crew")
        {
            Visible = false
        };
        _mapSelector.MarkerSelected += key =>
        {
            _selectedCrewName = key;
            if (_lastState != null)
                UpdateProgramState(_lastState);
        };
        root.AddChild(_mapSelector);
        AddChild(root);
    }

    private void SetMapMode(bool mapMode)
    {
        _mapMode = mapMode;
        _listModeBtn.Pressed = !mapMode;
        _mapModeBtn.Pressed = mapMode;
        _listScroll.Visible = !mapMode;
        _mapSelector.Visible = mapMode;
        if (mapMode)
            _mapSelector.RequestFit();
        if (_lastState != null)
            UpdateProgramState(_lastState);
    }

    private Texture ResolveJobIconTexture(string jobIconId)
    {
        if (_proto.TryIndex<JobIconPrototype>(jobIconId, out var iconProto))
            return _sprite.Frame0(iconProto.Icon);
        if (_proto.TryIndex<JobIconPrototype>("JobIconNoId", out var fallbackProto))
            return _sprite.Frame0(fallbackProto.Icon);
        return _sprite.Frame0(new SpriteSpecifier.Rsi(new ResPath("Interface/Misc/job_icons.rsi"), "NoId"));
    }

    private Texture ResolveHealthRsiTexture(CrewSensorEntryDto entry)
    {
        string stateName;
        if (!entry.IsAlive)
            stateName = "dead";
        else if (entry.IsCritical)
            stateName = "critical";
        else if (!entry.HasVitals)
            stateName = "alive";
        else
        {
            var dmgFrac = 1f - Math.Clamp(entry.HealthRatio, 0f, 1f);
            var step = (int) Math.Clamp(MathF.Round(dmgFrac * 4f), 0f, 4f);
            stateName = $"health{step}";
        }

        return _sprite.Frame0(new SpriteSpecifier.Rsi(new ResPath("Interface/Alerts/human_crew_monitoring.rsi"), stateName));
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CrewMonitorUiState state)
            return;

        _lastState = state;
        _headerLabel.Text = $"{state.StationName} — SUIT SENSORS ({state.Sensors.Count})";

        _listContainer.RemoveAllChildren();
        var markers = new List<DeviceNavMapMarker>();
        CrewSensorEntryDto? selectedEntry = null;

        if (!state.IsOnline)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = "Network offline — active Station Network Router connection required.",
                FontColorOverride = Color.FromHex("#FF8877")
            });
            _selectedInfoLabel.Text = "Network offline.";
            _mapSelector.SetMarkers(markers, "Network offline.", null);
            return;
        }

        if (state.Sensors.Count == 0)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = "No active suit sensors detected.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
        }
        else
        {
            foreach (var s in state.Sensors)
            {
                var isSelected = !string.IsNullOrEmpty(_selectedCrewName) &&
                                 string.Equals(s.Name, _selectedCrewName, StringComparison.OrdinalIgnoreCase);
                if (isSelected)
                    selectedEntry = s;

                var accent = !s.IsAlive
                    ? Color.FromHex("#FF5555")
                    : s.IsCritical
                        ? Color.FromHex("#FF8844")
                        : s.HealthRatio < 0.65f
                            ? Color.FromHex("#FFD166")
                            : Color.FromHex("#44E088");

                var card = new ContentPanelContainer
                {
                    HorizontalExpand = true,
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = isSelected ? Color.FromHex("#122638") : Color.FromHex("#0D1824"),
                        BorderColor = isSelected ? Color.FromHex("#00D4FF") : accent.WithAlpha(0.55f),
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

                var vitalIcon = new ContentTextureRect
                {
                    Texture = ResolveHealthRsiTexture(s),
                    DisplaySize = new Vector2(28f, 16f),
                    VerticalAlignment = VAlignment.Center
                };
                row.AddChild(vitalIcon);

                var jobIconRect = new ContentTextureRect
                {
                    Texture = ResolveJobIconTexture(s.JobIcon),
                    DisplaySize = new Vector2(18f, 18f),
                    VerticalAlignment = VAlignment.Center,
                    ToolTip = s.JobTitle
                };
                row.AddChild(jobIconRect);

                var identityCol = new ContentBoxContainer
                {
                    Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    SizeFlagsStretchRatio = 1.2f,
                    SeparationOverride = 1
                };
                identityCol.AddChild(new ContentLabel
                {
                    Text = s.Name,
                    FontColorOverride = Color.FromHex("#EAF8FF"),
                    HorizontalExpand = true,
                    ClipText = true
                });
                identityCol.AddChild(new ContentLabel
                {
                    Text = s.JobTitle,
                    FontColorOverride = Color.FromHex("#7CC4DE"),
                    HorizontalExpand = true,
                    ClipText = true
                });
                row.AddChild(identityCol);

                var hpPct = (int) MathF.Round(Math.Clamp(s.HealthRatio, 0f, 1f) * 100f);
                var hpText = !s.IsAlive
                    ? "DEAD"
                    : s.IsCritical
                        ? $"CRIT ({s.TotalDamage} DMG)"
                        : !s.HasVitals
                            ? "VITALS: ON"
                            : s.TotalDamage > 0
                                ? $"{hpPct}% (-{s.TotalDamage})"
                                : "100% HEALTHY";
                var hpBar = new ContentProgressBar
                {
                    Value = !s.IsAlive ? 0f : (s.HasVitals ? s.HealthRatio : 1f),
                    OverlayText = hpText,
                    MinWidth = 132f,
                    MinHeight = 20f,
                    FillColor = accent.WithAlpha(0.75f),
                    BorderColor = accent.WithAlpha(0.85f),
                    BackgroundColor = Color.FromHex("#081018"),
                    VerticalAlignment = VAlignment.Center
                };
                row.AddChild(hpBar);

                var capturedName = s.Name;
                var coordsBtn = new ContentButton
                {
                    Text = s.HasCoordinates ? s.CoordinatesText : "No GPS",
                    Disabled = !s.HasCoordinates,
                    MinWidth = 104f,
                    ToolTip = s.HasCoordinates ? "Locate on station map" : "Suit sensor coordinates disabled"
                };
                coordsBtn.OnPressed += _ =>
                {
                    _selectedCrewName = capturedName;
                    SetMapMode(true);
                };
                row.AddChild(coordsBtn);

                card.AddChild(row);
                _listContainer.AddChild(card);

                if (s.HasCoordinates && s.Entity is { } entNet)
                {
                    var isSel = selectedEntry == s;
                    var markerColor = isSel ? Color.FromHex("#00FFCC") : accent;
                    markers.Add(new DeviceNavMapMarker(
                        entNet,
                        s.Name,
                        $"{s.Name} ({s.JobTitle})",
                        s.GridEntity,
                        s.LocalPosition,
                        markerColor,
                        Selected: isSel,
                        Active: s.IsAlive,
                        Blinks: !s.IsAlive || s.IsCritical));
                }
            }
        }

        var summary = selectedEntry != null
            ? $"Selected: {selectedEntry.Name} ({selectedEntry.JobTitle}) | {(!selectedEntry.IsAlive ? "DEAD" : selectedEntry.IsCritical ? "CRIT" : "ALIVE")} | Dmg: {selectedEntry.TotalDamage} | Pos: {selectedEntry.CoordinatesText}"
            : $"Auto-syncing suit sensors ({markers.Count} with coordinates).";
        _selectedInfoLabel.Text = summary;
        _mapSelector.SetMarkers(markers, summary, state.StationGrid);
    }
}

// =============================================================================
// Communications Console UI (/bin/comms_console.nte)
// =============================================================================

public sealed class CommsConsoleUiControl : ProgramUiControl
{
    private readonly ContentLabel _statusLabel;
    private readonly ContentBoxContainer _authorizedBody;
    private readonly ContentPanelContainer _lockedBanner;
    private readonly ContentLabel _lockedBannerLabel;
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

        var headerRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        headerRow.AddChild(new ContentLabel
        {
            Text = "STATION COMMUNICATIONS & ALERT CONSOLE",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center
        });
        root.AddChild(headerRow);

        _statusLabel = new ContentLabel
        {
            Name = "CommsStatusLabel",
            Text = "Ready",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);

        _lockedBanner = new ContentPanelContainer
        {
            Name = "CommsLockedBanner",
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1520"),
                BorderColor = Color.FromHex("#AA5533"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 16,
                ContentMarginRightOverride = 16,
                ContentMarginTopOverride = 16,
                ContentMarginBottomOverride = 16
            }
        };
        _lockedBannerLabel = new ContentLabel
        {
            Text = "COMMAND AUTHORIZATION REQUIRED — Swipe Command ID card to unlock.",
            FontColorOverride = Color.FromHex("#FFD166"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center
        };
        _lockedBanner.AddChild(_lockedBannerLabel);
        root.AddChild(_lockedBanner);

        _authorizedBody = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6
        };

        _announcementEdit = new ContentLineEdit
        {
            Name = "CommsAnnouncementEdit",
            PlaceHolder = "Enter station-wide announcement...",
            HorizontalExpand = true,
            MinHeight = 32f
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

        _authorizedBody.AddChild(_announcementEdit);
        _authorizedBody.AddChild(_broadcastBtn);

        _authorizedBody.AddChild(new ContentLabel
        {
            Text = "STATION ALERT LEVEL:",
            FontColorOverride = Color.FromHex("#FFD166")
        });
        _alertLevelsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 6,
            VerticalSeparation = 4
        };
        _authorizedBody.AddChild(_alertLevelsWrap);
        root.AddChild(_authorizedBody);
        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CommsConsoleUiState state)
            return;

        if (!state.IsOnline || !state.CanBroadcast)
        {
            _authorizedBody.Visible = false;
            _lockedBanner.Visible = true;
            _lockedBannerLabel.Text = !state.IsOnline
                ? "NETWORK OFFLINE — Active Station Network Router connection required."
                : "COMMAND AUTHORIZATION REQUIRED — Swipe Command ID card to unlock.";
            _statusLabel.Text = state.StatusMessage;
            _alertLevelsWrap.RemoveAllChildren();
            return;
        }

        _authorizedBody.Visible = true;
        _lockedBanner.Visible = false;
        _statusLabel.Text = $"Current Alert: {state.CurrentAlertLevel.ToUpperInvariant()} | {state.StatusMessage}";
        _broadcastBtn.Disabled = false;

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
