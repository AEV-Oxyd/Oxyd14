using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Security;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.Computers.Programs;

// =============================================================================
// RECORD SUBJECT PHOTO CONTROL (HEADSHOT & PRISON ATTIRE FULL-BODY MUGSHOT)
// =============================================================================

/// <summary>
/// Renders a client-side humanoid preview entity for Station/Crew Records and Criminal/Security Records:
/// - When <see cref="IsHeadshot"/> is true (Crew Records): renders a zoomed, cropped headshot of the subject.
/// - When <see cref="WearPrisonAttire"/> is true (Security Records): equips the subject in a gray jumpsuit
///   (<c>ClothingUniformJumpsuitColorGrey</c>) and black shoes (<c>ClothingShoesColorBlack</c>) and renders
///   the entire full-body subject.
/// </summary>
public sealed class RecordSubjectPhotoControl : ContentControl
{
    public const string PrisonJumpsuitProtoId = "ClothingUniformJumpsuitColorGrey";
    public const string PrisonShoesProtoId = "ClothingShoesColorBlack";

    private readonly IEntityManager _entMan;
    private readonly IPrototypeManager _proto;
    private readonly List<EntityUid> _spawnedEquipment = new();

    private SpriteSystem? _spriteSystem;
    private TransformSystem? _transformSystem;

    private string _lastSignature = string.Empty;
    private string _cachedKey = string.Empty;
    private string _cachedName = string.Empty;
    private string _cachedSpecies = "Human";
    private string _cachedGender = "Epicene";
    private int _cachedAge = 25;
    private string _cachedJobProto = string.Empty;
    private HumanoidCharacterProfile? _cachedProfile;

    public bool IsHeadshot { get; }
    public bool WearPrisonAttire { get; }

    public EntityUid PreviewDummy { get; private set; } = EntityUid.Invalid;
    public string LoadedSubjectKey => _cachedKey;
    public string LoadedSubjectName => _cachedName;
    public string? EquippedJumpsuitPrototype { get; private set; }
    public string? EquippedShoesPrototype { get; private set; }

    public RecordSubjectPhotoControl(bool isHeadshot, bool wearPrisonAttire)
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        _proto = IoCManager.Resolve<IPrototypeManager>();

        IsHeadshot = isHeadshot;
        WearPrisonAttire = wearPrisonAttire;
        RectClipContent = true;
        MinSize = isHeadshot ? new Vector2(92f, 92f) : new Vector2(104f, 128f);
        MaxSize = isHeadshot ? new Vector2(92f, 92f) : new Vector2(104f, 128f);
    }

    public void SetSubject(
        string key,
        string name,
        string species,
        string gender,
        int age,
        string jobProtoId,
        HumanoidCharacterProfile? profile)
    {
        _cachedKey = key ?? string.Empty;
        _cachedName = name ?? string.Empty;
        _cachedSpecies = string.IsNullOrWhiteSpace(species) ? "Human" : species;
        _cachedGender = string.IsNullOrWhiteSpace(gender) ? "Epicene" : gender;
        _cachedAge = Math.Clamp(age, 1, 200);
        _cachedJobProto = jobProtoId ?? string.Empty;
        _cachedProfile = profile;

        if (string.IsNullOrWhiteSpace(_cachedName))
        {
            ClearPreview();
            return;
        }

        var profileHash = profile != null
            ? $"{profile.Species.Id}:{profile.Sex}:{profile.Appearance.SkinColor.ToHex()}:{profile.Appearance.EyeColor.ToHex()}:{profile.Appearance.Markings.Count}"
            : "default";
        var sig = $"{_cachedKey}|{_cachedName}|{_cachedSpecies}|{_cachedGender}|{_cachedAge}|{_cachedJobProto}|{IsHeadshot}|{WearPrisonAttire}|{profileHash}";
        if (sig == _lastSignature && _entMan.EntityExists(PreviewDummy))
            return;

        RebuildPreviewEntity(sig);
    }

    public void ClearPreview()
    {
        _lastSignature = string.Empty;
        EquippedJumpsuitPrototype = null;
        EquippedShoesPrototype = null;

        for (var i = 0; i < _spawnedEquipment.Count; i++)
        {
            var eq = _spawnedEquipment[i];
            if (_entMan.EntityExists(eq))
                _entMan.DeleteEntity(eq);
        }
        _spawnedEquipment.Clear();

        if (_entMan.EntityExists(PreviewDummy))
            _entMan.DeleteEntity(PreviewDummy);

        PreviewDummy = EntityUid.Invalid;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        if (!string.IsNullOrWhiteSpace(_cachedName) && !_entMan.EntityExists(PreviewDummy))
        {
            SetSubject(_cachedKey, _cachedName, _cachedSpecies, _cachedGender, _cachedAge, _cachedJobProto, _cachedProfile);
        }
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        ClearPreview();
    }

    private void RebuildPreviewEntity(string signature)
    {
        ClearPreview();
        _lastSignature = signature;

        var validSpecies = _proto.HasIndex<SpeciesPrototype>(_cachedSpecies)
            ? new ProtoId<SpeciesPrototype>(_cachedSpecies)
            : HumanoidCharacterProfile.DefaultSpecies;

        var parsedSex = _cachedGender.Equals("Female", StringComparison.OrdinalIgnoreCase)
            ? Sex.Female
            : Sex.Male;

        HumanoidCharacterProfile resolvedProfile;
        if (_cachedProfile != null)
        {
            resolvedProfile = new HumanoidCharacterProfile(_cachedProfile)
            {
                Name = _cachedName,
                Age = _cachedAge
            };
            if (!_proto.HasIndex<SpeciesPrototype>(resolvedProfile.Species))
                resolvedProfile.Species = validSpecies;
        }
        else
        {
            resolvedProfile = HumanoidCharacterProfile.DefaultWithSpecies(validSpecies, parsedSex);
            resolvedProfile.Name = _cachedName;
            resolvedProfile.Age = _cachedAge;
        }

        var speciesProto = _proto.Index(resolvedProfile.Species);
        PreviewDummy = _entMan.SpawnEntity(speciesProto.DollPrototype, MapCoordinates.Nullspace);
        _entMan.System<MetaDataSystem>().SetEntityName(PreviewDummy, _cachedName);
        _entMan.System<SharedVisualBodySystem>().ApplyProfileTo(PreviewDummy, resolvedProfile);

        var invSys = _entMan.System<InventorySystem>();

        if (WearPrisonAttire)
        {
            // Security Records full-body mugshot: subject wears a gray jumpsuit and black shoes.
            if (invSys.TryUnequip(PreviewDummy, "jumpsuit", out var oldSuit, silent: true, force: true, reparent: false))
                _entMan.DeleteEntity(oldSuit.Value);
            if (invSys.TryUnequip(PreviewDummy, "shoes", out var oldShoes, silent: true, force: true, reparent: false))
                _entMan.DeleteEntity(oldShoes.Value);

            if (_proto.HasIndex<EntityPrototype>(PrisonJumpsuitProtoId))
            {
                var suit = _entMan.SpawnEntity(PrisonJumpsuitProtoId, MapCoordinates.Nullspace);
                _spawnedEquipment.Add(suit);
                if (invSys.TryEquip(PreviewDummy, suit, "jumpsuit", silent: true, force: true))
                    EquippedJumpsuitPrototype = PrisonJumpsuitProtoId;
            }

            if (_proto.HasIndex<EntityPrototype>(PrisonShoesProtoId))
            {
                var shoes = _entMan.SpawnEntity(PrisonShoesProtoId, MapCoordinates.Nullspace);
                _spawnedEquipment.Add(shoes);
                if (invSys.TryEquip(PreviewDummy, shoes, "shoes", silent: true, force: true))
                    EquippedShoesPrototype = PrisonShoesProtoId;
            }
        }
        else if (IsHeadshot && !string.IsNullOrWhiteSpace(_cachedJobProto) &&
                 _proto.TryIndex<JobPrototype>(_cachedJobProto, out var jobProto) &&
                 _proto.Resolve(jobProto.StartingGear, out var gear))
        {
            var suitProto = ((IEquipmentLoadout) gear).GetGear("jumpsuit");
            if (!string.IsNullOrEmpty(suitProto) && _proto.HasIndex<EntityPrototype>(suitProto))
            {
                var suit = _entMan.SpawnEntity(suitProto, MapCoordinates.Nullspace);
                _spawnedEquipment.Add(suit);
                if (invSys.TryEquip(PreviewDummy, suit, "jumpsuit", silent: true, force: true))
                    EquippedJumpsuitPrototype = suitProto;
            }
        }

        InvalidateMeasure();
    }

    protected override void Draw(IRenderHandle renderHandle)
    {
        base.Draw(renderHandle);

        var screen = renderHandle.DrawingHandleScreen;
        var bg = WearPrisonAttire ? Color.FromHex("#1A131A") : Color.FromHex("#0A1724");
        var border = WearPrisonAttire ? Color.FromHex("#7E3240") : Color.FromHex("#245C7E");
        var rect = new UIBox2(Vector2.Zero, PixelSize);
        screen.DrawRect(rect, bg);

        // Subtle mugshot height lines or ID headshot backdrop grid
        var lineColor = WearPrisonAttire ? Color.FromHex("#331D26") : Color.FromHex("#122B3F");
        for (var y = 16; y < (int) PixelSize.Y; y += 16)
        {
            screen.DrawLine(new Vector2(2f, y), new Vector2(PixelSize.X - 2f, y), lineColor);
        }

        if (_entMan.EntityExists(PreviewDummy) &&
            _entMan.TryGetComponent(PreviewDummy, out SpriteComponent? sprite) &&
            _entMan.TryGetComponent(PreviewDummy, out TransformComponent? xform))
        {
            _spriteSystem ??= _entMan.System<SpriteSystem>();
            _transformSystem ??= _entMan.System<TransformSystem>();
            _spriteSystem.ForceUpdate(PreviewDummy);

            Vector2 drawPos;
            Vector2 drawScale;

            if (IsHeadshot)
            {
                // Zoom in on the head & upper collar and crop below the shoulders.
                var s = MathF.Min(PixelSize.X, PixelSize.Y) / 18f;
                drawScale = new Vector2(s, s);
                drawPos = new Vector2(PixelSize.X * 0.5f, PixelSize.Y * 0.84f);
            }
            else
            {
                // Full-body photo showing the entire subject from head to black shoes.
                var s = MathF.Min(PixelSize.X, PixelSize.Y) / 38f;
                drawScale = new Vector2(s, s);
                drawPos = new Vector2(PixelSize.X * 0.5f, PixelSize.Y * 0.52f);
            }

            renderHandle.DrawEntity(
                PreviewDummy,
                drawPos,
                drawScale,
                Angle.Zero,
                Angle.Zero,
                Direction.South,
                sprite,
                xform,
                _transformSystem);
        }

        // Outer photo frame border
        screen.DrawRect(new UIBox2(0, 0, PixelSize.X, 1), border);
        screen.DrawRect(new UIBox2(0, PixelSize.Y - 1, PixelSize.X, PixelSize.Y), border);
        screen.DrawRect(new UIBox2(0, 0, 1, PixelSize.Y), border);
        screen.DrawRect(new UIBox2(PixelSize.X - 1, 0, PixelSize.X, PixelSize.Y), border);
    }
}

// =============================================================================
// STATION RECORDS MANAGER UI (/bin/station_records.nte)
// =============================================================================

public sealed class StationRecordsUiControl : ProgramUiControl
{
    private readonly ContentBoxContainer _mainRoot;
    private readonly ContentPanelContainer _lockBanner;
    private readonly ContentLabel _lockBannerLabel;
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _searchEdit;
    private readonly ContentBoxContainer _listContainer;

    private readonly RecordSubjectPhotoControl _headshotPhoto;
    private readonly ContentLabel _headshotCaptionLabel;
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

    public RecordSubjectPhotoControl HeadshotControl => _headshotPhoto;

    public StationRecordsUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        _mainRoot = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 5
        };
        var root = _mainRoot;

        _lockBanner = new ContentPanelContainer
        {
            Name = "StationRecordsLockBanner",
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
            Text = "RECORDS LOCKED — Swipe authorized ID card to unlock.",
            FontColorOverride = Color.FromHex("#FF8888"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center
        };
        _lockBanner.AddChild(_lockBannerLabel);

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

        // Top row of dossier: Subject Headshot + Name / Job / Age fields
        var dossierTopRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 10
        };

        var headshotColumn = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalAlignment = HAlignment.Center
        };
        _headshotPhoto = new RecordSubjectPhotoControl(isHeadshot: true, wearPrisonAttire: false)
        {
            Name = "StationRecordsHeadshotPhoto"
        };
        _headshotCaptionLabel = new ContentLabel
        {
            Name = "StationRecordsHeadshotCaption",
            Text = "ID HEADSHOT",
            FontColorOverride = Color.FromHex("#66B8DD"),
            HorizontalAlignment = HAlignment.Center
        };
        headshotColumn.AddChild(_headshotPhoto);
        headshotColumn.AddChild(_headshotCaptionLabel);
        dossierTopRow.AddChild(headshotColumn);

        var primaryFieldsCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
        };

        ContentLineEdit MakeFieldIn(ContentBoxContainer targetBox, string label, string ctrlName, string placeholder)
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
            targetBox.AddChild(row);
            return edit;
        }

        _nameEdit = MakeFieldIn(primaryFieldsCol, "Name:", "StationRecordsNameEdit", "Crewmember full name");
        _jobEdit = MakeFieldIn(primaryFieldsCol, "Job Title:", "StationRecordsJobEdit", "Job title");
        _ageEdit = MakeFieldIn(primaryFieldsCol, "Age:", "StationRecordsAgeEdit", "25");

        dossierTopRow.AddChild(primaryFieldsCol);
        form.AddChild(dossierTopRow);

        _speciesEdit = MakeFieldIn(form, "Species:", "StationRecordsSpeciesEdit", "Human");
        _genderEdit = MakeFieldIn(form, "Gender:", "StationRecordsGenderEdit", "Epicene");
        _fingerprintEdit = MakeFieldIn(form, "Fingerprint:", "StationRecordsFingerprintEdit", "Fingerprint hash");
        _dnaEdit = MakeFieldIn(form, "DNA:", "StationRecordsDnaEdit", "DNA hash");

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
        AddChild(_lockBanner);
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
        if (state.IsLocked)
        {
            _mainRoot.Visible = false;
            _lockBanner.Visible = true;
            _lockBannerLabel.Text = string.IsNullOrWhiteSpace(state.StatusMessage)
                ? "RECORDS LOCKED — Swipe authorized ID card to unlock."
                : state.StatusMessage;
            _listContainer.RemoveAllChildren();
            _headshotPhoto.ClearPreview();
            return;
        }

        _mainRoot.Visible = true;
        _lockBanner.Visible = false;

        var tsText = state.CacheTimestamp.HasValue
            ? $" [Updated {TimeSpan.FromSeconds(Math.Truncate(state.CacheTimestamp.Value.TotalSeconds)):hh\\:mm\\:ss}]"
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
            _headshotPhoto.SetSubject(
                selected.Key,
                selected.Name,
                selected.Species,
                selected.Gender,
                selected.Age,
                selected.JobPrototype,
                selected.CharacterProfile);
        }
        else
        {
            _loadedKey = string.Empty;
            _crimStatusLabel.Text = "Security Status: --";
            _headshotPhoto.ClearPreview();
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
    private readonly ContentBoxContainer _mainRoot;
    private readonly ContentPanelContainer _lockBanner;
    private readonly ContentLabel _lockBannerLabel;
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _searchEdit;
    private readonly ContentBoxContainer _listContainer;

    private readonly RecordSubjectPhotoControl _subjectPhoto;
    private readonly ContentLabel _subjectPhotoCaption;
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

    public RecordSubjectPhotoControl SubjectPhotoControl => _subjectPhoto;

    public CriminalRecordsUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        _mainRoot = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 5
        };
        var root = _mainRoot;

        _lockBanner = new ContentPanelContainer
        {
            Name = "CriminalRecordsLockBanner",
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#120A10"),
                BorderColor = Color.FromHex("#CC3344"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 16,
                ContentMarginRightOverride = 16,
                ContentMarginTopOverride = 16,
                ContentMarginBottomOverride = 16
            }
        };
        _lockBannerLabel = new ContentLabel
        {
            Text = "SECURITY RECORDS LOCKED — Swipe Security ID card to unlock.",
            FontColorOverride = Color.FromHex("#FF8888"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center
        };
        _lockBanner.AddChild(_lockBannerLabel);

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

        // Subject mugshot row: full-body photo in gray jumpsuit & black shoes + subject details & status controls
        var subjectTopRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 10
        };

        var mugshotCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalAlignment = HAlignment.Center
        };
        _subjectPhoto = new RecordSubjectPhotoControl(isHeadshot: false, wearPrisonAttire: true)
        {
            Name = "CriminalRecordsSubjectPhoto"
        };
        _subjectPhotoCaption = new ContentLabel
        {
            Name = "CriminalRecordsSubjectPhotoCaption",
            Text = "SUBJECT PHOTO",
            FontColorOverride = Color.FromHex("#E08899"),
            HorizontalAlignment = HAlignment.Center
        };
        mugshotCol.AddChild(_subjectPhoto);
        mugshotCol.AddChild(_subjectPhotoCaption);
        subjectTopRow.AddChild(mugshotCol);

        var subjectInfoCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
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
        subjectInfoCol.AddChild(_selectedNameLabel);
        subjectInfoCol.AddChild(_selectedStatusBadge);

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
        subjectInfoCol.AddChild(statusBtnRow);

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
        subjectInfoCol.AddChild(reasonRow);

        subjectTopRow.AddChild(subjectInfoCol);
        rightBox.AddChild(subjectTopRow);

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
        AddChild(_lockBanner);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not CriminalRecordsUiState state)
            return;

        _lastState = state;
        if (state.IsLocked)
        {
            _mainRoot.Visible = false;
            _lockBanner.Visible = true;
            _lockBannerLabel.Text = string.IsNullOrWhiteSpace(state.StatusMessage)
                ? "SECURITY RECORDS LOCKED — Swipe Security ID card to unlock."
                : state.StatusMessage;
            _listContainer.RemoveAllChildren();
            _historyContainer.RemoveAllChildren();
            _subjectPhoto.ClearPreview();
            return;
        }

        _mainRoot.Visible = true;
        _lockBanner.Visible = false;

        var tsText = state.CacheTimestamp.HasValue
            ? $" [Updated {TimeSpan.FromSeconds(Math.Truncate(state.CacheTimestamp.Value.TotalSeconds)):hh\\:mm\\:ss}]"
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
            _subjectPhoto.ClearPreview();
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
        _subjectPhoto.SetSubject(
            sel.Key,
            sel.Name,
            sel.Species,
            sel.Gender,
            sel.Age,
            string.Empty,
            sel.CharacterProfile);

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
