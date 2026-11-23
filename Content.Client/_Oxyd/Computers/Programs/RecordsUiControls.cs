using System.Globalization;
using System.Linq;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Security;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// STATION RECORDS MANAGER UI (/bin/station_records.nte)
// =============================================================================

public sealed class StationRecordsUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _searchEdit;
    private readonly ContentBoxContainer _listContainer;

    private readonly ContentLineEdit _nameEdit;
    private readonly ContentLineEdit _jobEdit;
    private readonly ContentLineEdit _ageEdit;
    private readonly ContentLineEdit _speciesEdit;
    private readonly ContentLineEdit _genderEdit;
    private readonly ContentLineEdit _fingerprintEdit;
    private readonly ContentLineEdit _dnaEdit;
    private readonly ContentLabel _crimStatusLabel;
    private readonly ContentButton _saveBtn;
    private readonly ContentButton _newBtn;
    private readonly ContentButton _deleteBtn;

    private StationRecordsUiState? _lastState;
    private string _loadedKey = string.Empty;

    public StationRecordsUiControl()
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
            Name = "StationRecordsHeaderLabel",
            Text = "STATION PERSONNEL RECORDS",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center
        };
        var refreshBtn = new ContentButton { Name = "StationRecordsRefreshBtn", Text = "Sync Network", MinWidth = 104 };
        refreshBtn.OnPressed += _ => SendProgramMessage(new StationRecordsActionMessage(StationRecordsAction.Refresh));
        topRow.AddChild(_headerLabel);
        topRow.AddChild(refreshBtn);
        root.AddChild(topRow);

        _statusLabel = new ContentLabel
        {
            Text = "Connecting to Data Server...",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);

        var split = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 8
        };

        // Left pane: Search + Crew Roster
        var leftPane = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            MinWidth = 220f,
            VerticalExpand = true,
            SeparationOverride = 4
        };
        _searchEdit = new ContentLineEdit
        {
            Name = "StationRecordsSearchEdit",
            PlaceHolder = "Filter name, job, DNA...",
            HorizontalExpand = true,
            MinHeight = 26f
        };
        _searchEdit.OnTextChanged += _ => RebuildRoster();
        leftPane.AddChild(_searchEdit);

        var rosterScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _listContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        rosterScroll.AddChild(_listContainer);
        leftPane.AddChild(rosterScroll);
        split.AddChild(leftPane);

        // Right pane: Personnel Record Editor / Inspector
        var rightPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1824"),
                BorderColor = Color.FromHex("#1F4B66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };
        var rightScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var form = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
        };

        ContentLineEdit MakeField(string label, string ctrlName, string placeholder)
        {
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6
            };
            row.AddChild(new ContentLabel
            {
                Text = label,
                MinWidth = 88f,
                WordWrap = false,
                VerticalAlignment = VAlignment.Center,
                FontColorOverride = Color.FromHex("#88B8CC")
            });
            var edit = new ContentLineEdit
            {
                Name = ctrlName,
                PlaceHolder = placeholder,
                HorizontalExpand = true,
                MinHeight = 25f
            };
            row.AddChild(edit);
            form.AddChild(row);
            return edit;
        }

        _nameEdit = MakeField("Name:", "StationRecordsNameEdit", "Crewmember full name");
        _jobEdit = MakeField("Job Title:", "StationRecordsJobEdit", "Job title");
        _ageEdit = MakeField("Age:", "StationRecordsAgeEdit", "25");
        _speciesEdit = MakeField("Species:", "StationRecordsSpeciesEdit", "Human");
        _genderEdit = MakeField("Gender:", "StationRecordsGenderEdit", "Epicene");
        _fingerprintEdit = MakeField("Fingerprint:", "StationRecordsFingerprintEdit", "Fingerprint hash");
        _dnaEdit = MakeField("DNA:", "StationRecordsDnaEdit", "DNA hash");

        _crimStatusLabel = new ContentLabel
        {
            Text = "Security Status: None",
            FontColorOverride = Color.FromHex("#66EEAA"),
            HorizontalExpand = true
        };
        form.AddChild(_crimStatusLabel);

        var btnRow = new ContentWrapContainer { HorizontalSeparation = 6, VerticalSeparation = 4 };
        _saveBtn = new ContentButton { Name = "StationRecordsSaveBtn", Text = "Save Changes", MinWidth = 104 };
        _saveBtn.OnPressed += _ => SendEdit(StationRecordsAction.SaveEntry);

        _newBtn = new ContentButton { Name = "StationRecordsNewBtn", Text = "Add New Record", MinWidth = 114 };
        _newBtn.OnPressed += _ => SendEdit(StationRecordsAction.CreateEntry);

        _deleteBtn = new ContentButton { Name = "StationRecordsDeleteBtn", Text = "Delete", MinWidth = 70 };
        _deleteBtn.OnPressed += _ =>
        {
            if (!string.IsNullOrEmpty(_loadedKey))
                SendProgramMessage(new StationRecordsActionMessage(StationRecordsAction.DeleteEntry, key: _loadedKey));
        };

        btnRow.AddChild(_saveBtn);
        btnRow.AddChild(_newBtn);
        btnRow.AddChild(_deleteBtn);
        form.AddChild(btnRow);

        rightScroll.AddChild(form);
        rightPanel.AddChild(rightScroll);
        split.AddChild(rightPanel);
        root.AddChild(split);

        AddChild(root);
    }

    private void SendEdit(StationRecordsAction action)
    {
        var age = int.TryParse(_ageEdit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 25;

        SendProgramMessage(new StationRecordsActionMessage(
            action,
            key: action == StationRecordsAction.CreateEntry ? string.Empty : _loadedKey,
            name: _nameEdit.Text,
            jobTitle: _jobEdit.Text,
            age: age,
            species: _speciesEdit.Text,
            gender: _genderEdit.Text,
            fingerprint: _fingerprintEdit.Text,
            dna: _dnaEdit.Text));
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not StationRecordsUiState state)
            return;

        _lastState = state;
        var tsText = state.CacheTimestamp.HasValue
            ? $" [File: {TimeSpan.FromSeconds(Math.Truncate(state.CacheTimestamp.Value.TotalSeconds)):hh\\:mm\\:ss}]"
            : string.Empty;
        _headerLabel.Text = $"{state.StationName} — PERSONNEL RECORDS ({state.Entries.Count}){tsText}";
        _statusLabel.Text = state.StatusMessage;
        _statusLabel.FontColorOverride = state.Online ? Color.FromHex("#66EEAA") : Color.FromHex("#FFAA66");

        _saveBtn.Disabled = !state.CanEdit;
        _newBtn.Disabled = !state.CanEdit;
        _deleteBtn.Disabled = !state.CanEdit;

        RebuildRoster();

        var selected = state.Entries.FirstOrDefault(e => e.Key == state.SelectedKey) ?? state.Entries.FirstOrDefault();
        if (selected != null)
        {
            _loadedKey = selected.Key;
            _nameEdit.Text = selected.Name;
            _jobEdit.Text = selected.JobTitle;
            _ageEdit.Text = selected.Age.ToString(CultureInfo.InvariantCulture);
            _speciesEdit.Text = selected.Species;
            _genderEdit.Text = selected.Gender;
            _fingerprintEdit.Text = selected.Fingerprint;
            _dnaEdit.Text = selected.Dna;
            _crimStatusLabel.Text = $"Security Status: {selected.CriminalStatus}";
            _crimStatusLabel.FontColorOverride = selected.CriminalStatus.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? Color.FromHex("#66EEAA")
                : Color.FromHex("#FF7A7A");
        }
        else
        {
            _loadedKey = string.Empty;
            _crimStatusLabel.Text = "Security Status: --";
        }
    }

    private void RebuildRoster()
    {
        _listContainer.RemoveAllChildren();
        if (_lastState == null)
            return;

        var q = (_searchEdit.Text ?? string.Empty).Trim();
        var shown = 0;
        foreach (var e in _lastState.Entries)
        {
            if (!string.IsNullOrEmpty(q) &&
                !e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !e.JobTitle.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !e.Fingerprint.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !e.Dna.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;
            var key = e.Key;
            var btn = new ContentButton
            {
                Text = $"{e.Name} — {e.JobTitle}",
                HorizontalExpand = true,
                Pressed = e.Key == _lastState.SelectedKey
            };
            btn.OnPressed += _ => SendProgramMessage(new StationRecordsActionMessage(StationRecordsAction.Select, key: key));
            _listContainer.AddChild(btn);
        }

        if (shown == 0)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = "No matching personnel records.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
        }
    }
}

// =============================================================================
// CRIMINAL RECORDS MANAGER UI (/bin/criminal_records.nte)
// =============================================================================

public sealed class CriminalRecordsUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _searchEdit;
    private readonly ContentBoxContainer _listContainer;

    private readonly ContentLabel _selectedNameLabel;
    private readonly ContentLabel _selectedStatusBadge;
    private readonly Dictionary<SecurityStatus, ContentButton> _statusButtons = new();
    private readonly ContentLineEdit _reasonEdit;
    private readonly ContentButton _applyStatusBtn;
    private readonly ContentLineEdit _historyNoteEdit;
    private readonly ContentButton _addHistoryBtn;
    private readonly ContentBoxContainer _historyContainer;

    private CriminalRecordsUiState? _lastState;
    private SecurityStatus? _statusFilter;
    private SecurityStatus _pendingStatus = SecurityStatus.None;
    private string _selectedKey = string.Empty;

    public CriminalRecordsUiControl()
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
            Name = "CriminalRecordsHeaderLabel",
            Text = "SECURITY CRIMINAL RECORDS",
            FontColorOverride = Color.FromHex("#FF6666"),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center
        };
        var refreshBtn = new ContentButton { Name = "CriminalRecordsRefreshBtn", Text = "Sync Network", MinWidth = 104 };
        refreshBtn.OnPressed += _ => SendProgramMessage(new CriminalRecordsActionMessage(CriminalRecordsAction.Refresh));
        topRow.AddChild(_headerLabel);
        topRow.AddChild(refreshBtn);
        root.AddChild(topRow);

        _statusLabel = new ContentLabel
        {
            Text = "Connecting to Data Server...",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);

        // Filter chips
        var filterRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 3 };
        void AddFilterChip(string label, SecurityStatus? filterVal)
        {
            var btn = new ContentButton { Text = label, MinWidth = 58 };
            btn.OnPressed += _ =>
            {
                _statusFilter = filterVal;
                RebuildList();
            };
            filterRow.AddChild(btn);
        }
        AddFilterChip("All", null);
        AddFilterChip("Wanted", SecurityStatus.Wanted);
        AddFilterChip("Suspected", SecurityStatus.Suspected);
        AddFilterChip("Detained", SecurityStatus.Detained);
        AddFilterChip("Paroled", SecurityStatus.Paroled);
        AddFilterChip("None", SecurityStatus.None);
        root.AddChild(filterRow);

        var split = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 8
        };

        // Left list
        var leftPane = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            MinWidth = 220f,
            VerticalExpand = true,
            SeparationOverride = 4
        };
        _searchEdit = new ContentLineEdit
        {
            PlaceHolder = "Filter name, job, reason...",
            HorizontalExpand = true,
            MinHeight = 26f
        };
        _searchEdit.OnTextChanged += _ => RebuildList();
        leftPane.AddChild(_searchEdit);

        var listScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _listContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        listScroll.AddChild(_listContainer);
        leftPane.AddChild(listScroll);
        split.AddChild(leftPane);

        // Right inspector
        var rightPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#14121A"),
                BorderColor = Color.FromHex("#6E2832"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };
        var rightBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6
        };

        _selectedNameLabel = new ContentLabel
        {
            Name = "CriminalSelectedNameLabel",
            Text = "Select a crewmember",
            FontColorOverride = Color.FromHex("#F0F4F8"),
            HorizontalExpand = true
        };
        _selectedStatusBadge = new ContentLabel
        {
            Name = "CriminalSelectedStatusLabel",
            Text = "Status: None",
            FontColorOverride = Color.FromHex("#66EEAA"),
            HorizontalExpand = true
        };
        rightBox.AddChild(_selectedNameLabel);
        rightBox.AddChild(_selectedStatusBadge);

        var statusBtnRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        foreach (var status in Enum.GetValues<SecurityStatus>())
        {
            var captured = status;
            var btn = new ContentButton
            {
                Name = $"CriminalStatusBtn_{status}",
                Text = status.ToString(),
                MinWidth = 72
            };
            btn.OnPressed += _ =>
            {
                _pendingStatus = captured;
                UpdateStatusButtonHighlights();
            };
            _statusButtons[status] = btn;
            statusBtnRow.AddChild(btn);
        }
        rightBox.AddChild(statusBtnRow);

        var reasonRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _reasonEdit = new ContentLineEdit
        {
            Name = "CriminalReasonEdit",
            PlaceHolder = "Reason (e.g. Grand theft, assault)...",
            HorizontalExpand = true,
            MinHeight = 26f
        };
        _applyStatusBtn = new ContentButton
        {
            Name = "CriminalApplyStatusBtn",
            Text = "Set Status",
            MinWidth = 88
        };
        _applyStatusBtn.OnPressed += _ =>
        {
            if (!string.IsNullOrEmpty(_selectedKey))
            {
                SendProgramMessage(new CriminalRecordsActionMessage(
                    CriminalRecordsAction.SetStatus,
                    key: _selectedKey,
                    status: _pendingStatus,
                    text: _reasonEdit.Text));
            }
        };
        reasonRow.AddChild(_reasonEdit);
        reasonRow.AddChild(_applyStatusBtn);
        rightBox.AddChild(reasonRow);

        rightBox.AddChild(new ContentLabel
        {
            Text = "CRIME HISTORY LOG:",
            FontColorOverride = Color.FromHex("#CC9999")
        });

        var addHistRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _historyNoteEdit = new ContentLineEdit
        {
            Name = "CriminalHistoryNoteEdit",
            PlaceHolder = "Add incident note...",
            HorizontalExpand = true,
            MinHeight = 25f
        };
        _addHistoryBtn = new ContentButton
        {
            Name = "CriminalAddHistoryBtn",
            Text = "Add Note",
            MinWidth = 78
        };
        _addHistoryBtn.OnPressed += _ =>
        {
            if (!string.IsNullOrEmpty(_selectedKey) && !string.IsNullOrWhiteSpace(_historyNoteEdit.Text))
            {
                var note = _historyNoteEdit.Text;
                _historyNoteEdit.Text = string.Empty;
                SendProgramMessage(new CriminalRecordsActionMessage(
                    CriminalRecordsAction.AddHistory,
                    key: _selectedKey,
                    text: note));
            }
        };
        addHistRow.AddChild(_historyNoteEdit);
        addHistRow.AddChild(_addHistoryBtn);
        rightBox.AddChild(addHistRow);

        var histScroll = new ContentScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        _historyContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        histScroll.AddChild(_historyContainer);
        rightBox.AddChild(histScroll);

        rightPanel.AddChild(rightBox);
        split.AddChild(rightPanel);
        root.AddChild(split);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CriminalRecordsUiState state)
            return;

        _lastState = state;
        var tsText = state.CacheTimestamp.HasValue
            ? $" [File: {TimeSpan.FromSeconds(Math.Truncate(state.CacheTimestamp.Value.TotalSeconds)):hh\\:mm\\:ss}]"
            : string.Empty;
        _headerLabel.Text = $"SECURITY CRIMINAL RECORDS ({state.Entries.Count}){tsText}";
        _statusLabel.Text = state.StatusMessage;
        _statusLabel.FontColorOverride = state.Online ? Color.FromHex("#88EECC") : Color.FromHex("#FFAA66");

        _applyStatusBtn.Disabled = !state.CanEdit;
        _addHistoryBtn.Disabled = !state.CanEdit;
        foreach (var btn in _statusButtons.Values)
            btn.Disabled = !state.CanEdit;

        RebuildList();

        var sel = state.Entries.FirstOrDefault(e => e.Key == state.SelectedKey) ?? state.Entries.FirstOrDefault();
        _historyContainer.RemoveAllChildren();

        if (sel == null)
        {
            _selectedKey = string.Empty;
            _selectedNameLabel.Text = "No criminal records found.";
            _selectedStatusBadge.Text = "Status: --";
            return;
        }

        _selectedKey = sel.Key;
        _pendingStatus = sel.Status;
        UpdateStatusButtonHighlights();

        _selectedNameLabel.Text = $"{sel.Name} ({sel.JobTitle})";
        var reasonSuffix = string.IsNullOrWhiteSpace(sel.Reason) ? string.Empty : $" — {sel.Reason}";
        _selectedStatusBadge.Text = $"Status: {sel.Status}{reasonSuffix}";
        _selectedStatusBadge.FontColorOverride = GetStatusColor(sel.Status);
        _reasonEdit.Text = sel.Reason;

        if (sel.History.Count == 0)
        {
            _historyContainer.AddChild(new ContentLabel
            {
                Text = "Clean record — no incident entries.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
            return;
        }

        for (var i = 0; i < sel.History.Count; i++)
        {
            var h = sel.History[i];
            var idx = i;
            var row = new ContentBoxContainer
            {
                Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 6
            };
            var initSuffix = string.IsNullOrWhiteSpace(h.Initiator) ? string.Empty : $" ({h.Initiator})";
            row.AddChild(new ContentLabel
            {
                Text = $"[{h.TimestampText}] {h.Line}{initSuffix}",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#E8D8D8")
            });
            var delBtn = new ContentButton { Text = "X", MinWidth = 24, Disabled = !state.CanEdit };
            delBtn.OnPressed += _ => SendProgramMessage(new CriminalRecordsActionMessage(
                CriminalRecordsAction.DeleteHistory,
                key: _selectedKey,
                historyIndex: idx));
            row.AddChild(delBtn);
            _historyContainer.AddChild(row);
        }
    }

    private void UpdateStatusButtonHighlights()
    {
        foreach (var (st, btn) in _statusButtons)
            btn.Pressed = st == _pendingStatus;
    }

    private void RebuildList()
    {
        _listContainer.RemoveAllChildren();
        if (_lastState == null)
            return;

        var q = (_searchEdit.Text ?? string.Empty).Trim();
        var shown = 0;
        foreach (var e in _lastState.Entries)
        {
            if (_statusFilter.HasValue && e.Status != _statusFilter.Value)
                continue;

            if (!string.IsNullOrEmpty(q) &&
                !e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !e.JobTitle.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !e.Reason.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;
            var key = e.Key;
            var tag = e.Status == SecurityStatus.None ? string.Empty : $" [{e.Status.ToString().ToUpperInvariant()}]";
            var btn = new ContentButton
            {
                Text = $"{e.Name}{tag}",
                HorizontalExpand = true,
                Pressed = e.Key == _lastState.SelectedKey
            };
            btn.OnPressed += _ => SendProgramMessage(new CriminalRecordsActionMessage(CriminalRecordsAction.Select, key: key));
            _listContainer.AddChild(btn);
        }

        if (shown == 0)
        {
            _listContainer.AddChild(new ContentLabel
            {
                Text = "No matching records.",
                FontColorOverride = Color.FromHex("#8899AA")
            });
        }
    }

    private static Color GetStatusColor(SecurityStatus status) => status switch
    {
        SecurityStatus.Wanted => Color.FromHex("#FF5555"),
        SecurityStatus.Hostile => Color.FromHex("#FF3355"),
        SecurityStatus.Detained => Color.FromHex("#FFAA44"),
        SecurityStatus.Suspected => Color.FromHex("#FFD166"),
        SecurityStatus.Paroled => Color.FromHex("#66CCFF"),
        SecurityStatus.Discharged => Color.FromHex("#88DDAA"),
        SecurityStatus.Eliminated => Color.FromHex("#CC4466"),
        _ => Color.FromHex("#66EEAA")
    };
}
