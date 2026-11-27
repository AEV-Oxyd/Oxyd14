using System.Linq;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Content.Shared.Access;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Dedicated UI for /bin/id_console.nte: requires 2 ID Card Slot modules
/// (Slot #1 = Modifying ID, Slot #2 = Target ID) and optionally uses /keys/*.acc files for auth.
/// Enumerates static Job & Access prototypes directly on the client via IPrototypeManager.
/// </summary>
public sealed class IdModifierUiControl : ProgramUiControl
{
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly ContentBoxContainer _mainRoot;
    private readonly ContentPanelContainer _lockBanner;
    private readonly ContentLabel _lockBannerLabel;
    private readonly ContentLabel _hardwareLabel;
    private readonly ContentButton _privSlotBtn;
    private readonly ContentButton _targetSlotBtn;
    private readonly ContentButton _swapSlotsBtn;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLabel _authSummaryLabel;
    private readonly ContentWrapContainer _accFilesWrap;
    private readonly ContentLabel _targetCardLabel;
    private readonly ContentLineEdit _targetNameInput;
    private readonly ContentLineEdit _targetJobInput;
    private readonly ContentButton _applyIdentityBtn;
    private readonly ContentButton _applyAccToTargetBtn;
    private readonly ContentButton _grantAllBtn;
    private readonly ContentButton _revokeAllBtn;
    private readonly ContentWrapContainer _jobPresetsWrap;
    private readonly ContentWrapContainer _accessGridWrap;

    private readonly List<JobPrototype> _cachedJobs = new();
    private readonly List<AccessLevelPrototype> _cachedAccessLevels = new();
    private string _selectedJobProtoId = string.Empty;
    private string _lastTargetCardKey = string.Empty;

    public IdModifierUiControl()
    {
        IoCManager.InjectDependencies(this);

        HorizontalExpand = true;
        VerticalExpand = true;

        _cachedJobs.AddRange(_proto.EnumeratePrototypes<JobPrototype>()
            .Where(j => j.SetPreference)
            .OrderBy(j => j.LocalizedName, StringComparer.OrdinalIgnoreCase));

        _cachedAccessLevels.AddRange(_proto.EnumeratePrototypes<AccessLevelPrototype>()
            .Where(a => a.CanAddToIdCard)
            .OrderBy(a => a.GetAccessLevelName(), StringComparer.OrdinalIgnoreCase));

        _mainRoot = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 6
        };
        var root = _mainRoot;

        _lockBanner = new ContentPanelContainer
        {
            Name = "IdModLockBanner",
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
            Text = "TERMINAL LOCKED — Swipe authorized ID card to unlock.",
            FontColorOverride = Color.FromHex("#FF8888"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center
        };
        _lockBanner.AddChild(_lockBannerLabel);

        // 1. Hardware Slots Strip (Requires 2 ID Card Slot modules)
        var hwPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#111D29"),
                BorderColor = Color.FromHex("#244E6B"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5
            }
        };

        var hwCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };

        _hardwareLabel = new ContentLabel
        {
            Name = "IdModHardwareLabel",
            Text = "Dual ID Slot Hardware: Checking...",
            FontColorOverride = Color.FromHex("#8AD8FF"),
            WordWrap = true
        };
        hwCol.AddChild(_hardwareLabel);

        var slotRow = new ContentWrapContainer { HorizontalSeparation = 5, VerticalSeparation = 4 };
        _privSlotBtn = new ContentButton
        {
            Name = "IdModPrivSlotBtn",
            Text = "Slot #1 Modifying: Empty",
            MinWidth = 210,
            MinHeight = 26
        };
        _privSlotBtn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.TogglePrivilegedSlot));

        _targetSlotBtn = new ContentButton
        {
            Name = "IdModTargetSlotBtn",
            Text = "Slot #2 Target: Empty",
            MinWidth = 210,
            MinHeight = 26
        };
        _targetSlotBtn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.ToggleTargetSlot));

        _swapSlotsBtn = new ContentButton
        {
            Name = "IdModSwapSlotsBtn",
            Text = "Swap Slots",
            MinWidth = 92,
            MinHeight = 26
        };
        _swapSlotsBtn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.SwapSlots));

        slotRow.AddChild(_privSlotBtn);
        slotRow.AddChild(_targetSlotBtn);
        slotRow.AddChild(_swapSlotsBtn);
        hwCol.AddChild(slotRow);
        hwPanel.AddChild(hwCol);
        root.AddChild(hwPanel);

        _statusLabel = new ContentLabel
        {
            Name = "IdModStatusLabel",
            Text = string.Empty,
            FontColorOverride = Color.FromHex("#88E0B8"),
            WordWrap = true
        };
        root.AddChild(_statusLabel);

        // 2. Authorization Source Bar (Slot #1 or /keys/*.acc)
        var authPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1821"),
                BorderColor = Color.FromHex("#24455E"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5
            }
        };

        var authCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };

        _authSummaryLabel = new ContentLabel
        {
            Name = "IdModAuthSummaryLabel",
            Text = "Modifying Authority: None",
            FontColorOverride = Color.FromHex("#FFD166"),
            WordWrap = true
        };
        authCol.AddChild(_authSummaryLabel);

        _accFilesWrap = new ContentWrapContainer
        {
            Name = "IdModAccFilesWrap",
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        authCol.AddChild(_accFilesWrap);
        authPanel.AddChild(authCol);
        root.AddChild(authPanel);

        // 3. Target Card Editor (Identity, Job Presets & Access Tags Grid)
        var editorScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true
        };

        var editorCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6
        };

        _targetCardLabel = new ContentLabel
        {
            Name = "IdModTargetCardLabel",
            Text = "Target Card (Slot #2): None inserted",
            FontColorOverride = Color.FromHex("#E6F2FA"),
            WordWrap = true
        };
        editorCol.AddChild(_targetCardLabel);

        var idRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        _targetNameInput = new ContentLineEdit
        {
            Name = "IdModTargetNameInput",
            PlaceHolder = "Full Name",
            MinWidth = 180,
            HorizontalExpand = true,
            MinHeight = 28
        };
        _targetJobInput = new ContentLineEdit
        {
            Name = "IdModTargetJobInput",
            PlaceHolder = "Job Title",
            MinWidth = 170,
            HorizontalExpand = true,
            MinHeight = 28
        };
        _applyIdentityBtn = new ContentButton
        {
            Name = "IdModApplyIdentityBtn",
            Text = "Apply Name & Job",
            MinWidth = 134,
            MinHeight = 28
        };
        _applyIdentityBtn.OnPressed += _ =>
        {
            SendProgramMessage(new IdModifierActionMessage(
                IdModifierAction.WriteTargetIdentity,
                _targetNameInput.Text,
                _targetJobInput.Text,
                _selectedJobProtoId));
        };

        idRow.AddChild(_targetNameInput);
        idRow.AddChild(_targetJobInput);
        idRow.AddChild(_applyIdentityBtn);
        editorCol.AddChild(idRow);

        var bulkRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        _applyAccToTargetBtn = new ContentButton
        {
            Name = "IdModApplyAccToTargetBtn",
            Text = "Copy .acc Access to Target",
            MinWidth = 170,
            MinHeight = 26
        };
        _applyAccToTargetBtn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.ApplyAccFileToTarget));

        _grantAllBtn = new ContentButton
        {
            Name = "IdModGrantAllBtn",
            Text = "Grant All Allowed",
            MinWidth = 128,
            MinHeight = 26
        };
        _grantAllBtn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.GrantAllAllowedAccess));

        _revokeAllBtn = new ContentButton
        {
            Name = "IdModRevokeAllBtn",
            Text = "Revoke All Allowed",
            MinWidth = 136,
            MinHeight = 26
        };
        _revokeAllBtn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.RevokeAllAllowedAccess));

        bulkRow.AddChild(_applyAccToTargetBtn);
        bulkRow.AddChild(_grantAllBtn);
        bulkRow.AddChild(_revokeAllBtn);
        editorCol.AddChild(bulkRow);

        editorCol.AddChild(new ContentLabel
        {
            Text = "Job Title Presets:",
            FontColorOverride = Color.FromHex("#88C4E8")
        });

        _jobPresetsWrap = new ContentWrapContainer
        {
            Name = "IdModJobPresetsWrap",
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        editorCol.AddChild(_jobPresetsWrap);

        editorCol.AddChild(new ContentLabel
        {
            Text = "Target Access Levels:",
            FontColorOverride = Color.FromHex("#88C4E8")
        });

        _accessGridWrap = new ContentWrapContainer
        {
            Name = "IdModAccessGridWrap",
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        editorCol.AddChild(_accessGridWrap);

        editorScroll.AddChild(editorCol);
        root.AddChild(editorScroll);
        AddChild(root);
        AddChild(_lockBanner);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not IdModifierUiState state)
            return;

        if (state.IsLocked)
        {
            _mainRoot.Visible = false;
            _lockBanner.Visible = true;
            _lockBannerLabel.Text = string.IsNullOrWhiteSpace(state.StatusMessage)
                ? "TERMINAL LOCKED — Swipe authorized ID card to unlock."
                : state.StatusMessage;
            return;
        }

        _mainRoot.Visible = true;
        _lockBanner.Visible = false;

        _statusLabel.Text = state.StatusMessage;

        if (!state.HasRequiredModificationHardware)
        {
            _hardwareLabel.Text = $"Dual ID Slot Hardware: INSUFFICIENT ({state.SlotCount}/2 Card Slot modules installed). Cannot modify target IDs.";
            _hardwareLabel.FontColorOverride = Color.FromHex("#FF8877");
        }
        else
        {
            _hardwareLabel.Text = $"Dual ID Slot Hardware: READY ({state.SlotCount}/2 ID Card Slot modules active).";
            _hardwareLabel.FontColorOverride = Color.FromHex("#72E89B");
        }

        _privSlotBtn.Text = state.Slot1Occupied
            ? $"Eject Modifying: {Truncate(state.Slot1CardLabel, 22)}"
            : "Insert Modifying ID -> Slot #1";
        _privSlotBtn.Disabled = state.SlotCount < 1;

        _targetSlotBtn.Text = state.Slot2Occupied
            ? $"Eject Target: {Truncate(state.Slot2CardLabel, 22)}"
            : "Insert Target ID -> Slot #2";
        _targetSlotBtn.Disabled = !state.HasRequiredModificationHardware;

        _swapSlotsBtn.Disabled = !state.HasRequiredModificationHardware;

        var hasAuth = state.PrivilegedTags.Count > 0;
        _authSummaryLabel.Text = hasAuth
            ? $"Authority: {state.PrivilegedOwner} ({state.PrivilegedTags.Count} access codes)"
            : "Authority: None (Insert Modifying ID in Slot #1 or select a /keys/*.acc file)";
        _authSummaryLabel.FontColorOverride = hasAuth ? Color.FromHex("#66EEAA") : Color.FromHex("#FFD166");

        _accFilesWrap.RemoveAllChildren();
        foreach (var accPath in state.ExistingAccFiles)
        {
            var pathCopy = accPath;
            var isSel = string.Equals(pathCopy, state.SelectedAccFilePath, StringComparison.OrdinalIgnoreCase);
            var slashIdx = pathCopy.LastIndexOf('/');
            var shortName = slashIdx >= 0 ? pathCopy[(slashIdx + 1)..] : pathCopy;
            var btn = new ContentButton
            {
                Name = $"IdModSelectAccBtn_{pathCopy}",
                Text = shortName,
                Pressed = isSel,
                MinHeight = 24
            };
            btn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.SelectAccFile, pathCopy, "toggle"));
            _accFilesWrap.AddChild(btn);
        }

        var canModify = state.HasRequiredModificationHardware && state.HasTargetCard && hasAuth;
        _targetCardLabel.Text = state.HasTargetCard
            ? $"Target Card (Slot #2): {state.TargetCardName} | {state.TargetFullName} ({state.TargetJobTitle})"
            : "Target Card (Slot #2): None inserted";

        var cardKey = $"{state.HasTargetCard}:{state.TargetFullName}:{state.TargetJobTitle}:{state.TargetJobPrototype}";
        if (cardKey != _lastTargetCardKey)
        {
            _lastTargetCardKey = cardKey;
            _targetNameInput.Text = state.HasTargetCard ? state.TargetFullName : string.Empty;
            _targetJobInput.Text = state.HasTargetCard ? state.TargetJobTitle : string.Empty;
            _selectedJobProtoId = state.HasTargetCard ? state.TargetJobPrototype : string.Empty;
        }

        _targetNameInput.Editable = state.HasRequiredModificationHardware && state.HasTargetCard;
        _targetJobInput.Editable = state.HasRequiredModificationHardware && state.HasTargetCard;
        _applyIdentityBtn.Disabled = !canModify;
        _applyAccToTargetBtn.Disabled = !state.HasRequiredModificationHardware || !state.HasTargetCard || string.IsNullOrWhiteSpace(state.SelectedAccFilePath);
        _grantAllBtn.Disabled = !canModify;
        _revokeAllBtn.Disabled = !canModify;

        _jobPresetsWrap.RemoveAllChildren();
        foreach (var job in _cachedJobs)
        {
            var jobId = job.ID;
            var jobTitle = job.LocalizedName;
            var btn = new ContentButton
            {
                Name = $"IdModJobBtn_{jobId}",
                Text = jobTitle,
                Pressed = string.Equals(jobId, _selectedJobProtoId, StringComparison.OrdinalIgnoreCase),
                Disabled = !canModify,
                MinHeight = 22
            };
            btn.OnPressed += _ =>
            {
                _selectedJobProtoId = jobId;
                _targetJobInput.Text = jobTitle;
                SendProgramMessage(new IdModifierActionMessage(
                    IdModifierAction.WriteTargetIdentity,
                    _targetNameInput.Text,
                    jobTitle,
                    jobId));
            };
            _jobPresetsWrap.AddChild(btn);
        }

        var privSet = new HashSet<string>(state.PrivilegedTags, StringComparer.OrdinalIgnoreCase);
        var targetSet = new HashSet<string>(state.TargetAccessTags, StringComparer.OrdinalIgnoreCase);

        _accessGridWrap.RemoveAllChildren();
        foreach (var acc in _cachedAccessLevels)
        {
            var tagId = acc.ID;
            var isGranted = targetSet.Contains(tagId);
            var isAllowed = privSet.Contains(tagId);
            var btn = new ContentButton
            {
                Name = $"IdModAccessBtn_{tagId}",
                Text = acc.GetAccessLevelName(),
                Pressed = isGranted,
                Disabled = !state.HasRequiredModificationHardware || !state.HasTargetCard || !isAllowed,
                MinHeight = 24
            };
            btn.OnPressed += _ => SendProgramMessage(new IdModifierActionMessage(IdModifierAction.ToggleTargetAccessTag, tagId));
            _accessGridWrap.AddChild(btn);
        }
    }

    private static string Truncate(string text, int maxLen)
    {
        return text.Length <= maxLen ? text : text[..Math.Max(1, maxLen - 2)] + "..";
    }
}
