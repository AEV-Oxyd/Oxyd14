using System.Linq;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// UI for /bin/security_lock.nte allowing users to lock a computer behind a password,
/// a key-based password (.key file), and/or a chosen set of ID card access levels.
/// </summary>
public sealed class SecurityLockUiControl : ProgramUiControl
{
    private readonly ContentBoxContainer _authGateContainer;
    private readonly ContentLabel _authGateStatusLabel;
    private readonly ContentBoxContainer _configContainer;

    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _summaryLabel;
    private readonly ContentLabel _statusLabel;
    private readonly ContentButton _lockNowBtn;
    private readonly ContentButton _remoteLockBtn;
    private readonly ContentButton _clearAllBtn;

    private readonly ContentLineEdit _passwordInput;
    private readonly ContentButton _clearPasswordBtn;

    private readonly ContentLineEdit _keyLabelInput;
    private readonly ContentLabel _activeKeyLabel;
    private readonly ContentButton _exportKeyBtn;
    private readonly ContentButton _clearKeyBtn;
    private readonly ContentBoxContainer _keyFilesContainer;

    private readonly ContentLabel _accessHeaderLabel;
    private readonly ContentButton _matchModeBtn;
    private readonly ContentButton _clearAccessBtn;
    private readonly ContentWrapContainer _accessGrid;

    public SecurityLockUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 8
        };

        // ---------------------------------------------------------------------
        // STATUS & QUICK LOCK CARD (ALWAYS VISIBLE SO USERS CAN ALWAYS RE-LOCK)
        // ---------------------------------------------------------------------
        var statusCard = CreateSectionCard("#0E1A26", "#1F4B66");
        var statusCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
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
            Name = "SecurityLockHeaderLabel",
            Text = "SECURITY LOCK",
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#00E5FF")
        };
        _remoteLockBtn = new ContentButton
        {
            Name = "SecurityLockRemoteLockButton",
            Text = "Remote Lock: ON",
            MinWidth = 118
        };
        _remoteLockBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.ToggleRemoteLock));

        _lockNowBtn = new ContentButton
        {
            Name = "SecurityLockNowButton",
            Text = "Lock Computer",
            MinWidth = 122
        };
        _lockNowBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.LockNow));

        _clearAllBtn = new ContentButton
        {
            Name = "SecurityLockClearAllButton",
            Text = "Clear All",
            MinWidth = 78
        };
        _clearAllBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.ClearAllLocks));

        topRow.AddChild(_headerLabel);
        topRow.AddChild(_remoteLockBtn);
        topRow.AddChild(_lockNowBtn);
        topRow.AddChild(_clearAllBtn);
        statusCol.AddChild(topRow);

        _summaryLabel = new ContentLabel
        {
            Name = "SecurityLockSummaryLabel",
            Text = "No lock methods configured.",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#B8E0EE")
        };
        statusCol.AddChild(_summaryLabel);

        _statusLabel = new ContentLabel
        {
            Name = "SecurityLockStatusLabel",
            Text = string.Empty,
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#FFE066")
        };
        statusCol.AddChild(_statusLabel);

        statusCard.AddChild(statusCol);
        root.AddChild(statusCard);

        // ---------------------------------------------------------------------
        // SYSTEM ADMIN AUTHORIZATION GATE (NON-JAILBROKEN OS — CONFIG CHANGES ONLY)
        // ---------------------------------------------------------------------
        _authGateContainer = new ContentBoxContainer
        {
            Name = "SecurityLockAuthGate",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Visible = false,
            SeparationOverride = 8
        };
        var authCard = CreateSectionCard("#121926", "#AA3333");
        var authCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        authCol.AddChild(new ContentLabel
        {
            Text = "SYSTEM ADMIN AUTHORIZATION REQUIRED TO MODIFY LOCK SETTINGS",
            HorizontalAlignment = HAlignment.Center,
            FontColorOverride = Color.FromHex("#FF7777")
        });
        _authGateStatusLabel = new ContentLabel
        {
            Name = "SecurityLockAuthGateStatus",
            Text = string.Empty,
            HorizontalAlignment = HAlignment.Center,
            FontColorOverride = Color.FromHex("#FFE066"),
            Visible = false
        };
        authCol.AddChild(_authGateStatusLabel);

        var authBtn = new ContentButton
        {
            Name = "SecurityLockAuthorizeButton",
            Text = "Authorize",
            HorizontalAlignment = HAlignment.Center,
            MinWidth = 120
        };
        authBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.AuthorizeSystemAdmin));
        authCol.AddChild(authBtn);
        authCard.AddChild(authCol);
        _authGateContainer.AddChild(authCard);
        root.AddChild(_authGateContainer);

        _configContainer = new ContentBoxContainer
        {
            Name = "SecurityLockConfigContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 8
        };

        // ---------------------------------------------------------------------
        // PASSWORD LOCK CARD
        // ---------------------------------------------------------------------
        var pwCard = CreateSectionCard("#0C1622", "#1D445E");
        var pwCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
        };
        pwCol.AddChild(new ContentLabel
        {
            Text = "PASSWORD LOCK",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF")
        });

        var pwRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        _passwordInput = new ContentLineEdit
        {
            Name = "SecurityLockPasswordInput",
            HorizontalExpand = true,
            PlaceHolder = "Enter password..."
        };
        void SubmitPassword()
        {
            var val = (_passwordInput.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(val))
                return;
            SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.SetPassword, val));
            _passwordInput.Text = string.Empty;
        }
        _passwordInput.OnTextEntered += _ => SubmitPassword();

        var setPwBtn = new ContentButton
        {
            Name = "SecurityLockSetPasswordButton",
            Text = "Set Password",
            MinWidth = 108
        };
        setPwBtn.OnPressed += _ => SubmitPassword();

        _clearPasswordBtn = new ContentButton
        {
            Name = "SecurityLockClearPasswordButton",
            Text = "Clear",
            MinWidth = 62
        };
        _clearPasswordBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.ClearPassword));

        pwRow.AddChild(_passwordInput);
        pwRow.AddChild(setPwBtn);
        pwRow.AddChild(_clearPasswordBtn);
        pwCol.AddChild(pwRow);
        pwCard.AddChild(pwCol);
        _configContainer.AddChild(pwCard);

        // ---------------------------------------------------------------------
        // KEY AUTHENTICATION CARD
        // ---------------------------------------------------------------------
        var keyCard = CreateSectionCard("#0C1622", "#1D445E");
        var keyCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
        };
        keyCol.AddChild(new ContentLabel
        {
            Text = "KEY AUTHENTICATION",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#66EEAA")
        });
        _activeKeyLabel = new ContentLabel
        {
            Name = "SecurityLockActiveKeyLabel",
            Text = "Active Key: None",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#D0F2E4")
        };
        keyCol.AddChild(_activeKeyLabel);

        var keyRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        _keyLabelInput = new ContentLineEdit
        {
            Name = "SecurityLockKeyLabelInput",
            HorizontalExpand = true,
            PlaceHolder = "Key name..."
        };
        var genKeyBtn = new ContentButton
        {
            Name = "SecurityLockGenerateKeyButton",
            Text = "Generate Key",
            MinWidth = 104
        };
        genKeyBtn.OnPressed += _ =>
        {
            SendProgramMessage(new SecurityLockActionMessage(
                SecurityLockAction.GenerateKey,
                (_keyLabelInput.Text ?? string.Empty).Trim()));
            _keyLabelInput.Text = string.Empty;
        };

        _exportKeyBtn = new ContentButton
        {
            Name = "SecurityLockExportKeyButton",
            Text = "Export to Disk",
            MinWidth = 104
        };
        _exportKeyBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.ExportKeyToDisk));

        _clearKeyBtn = new ContentButton
        {
            Name = "SecurityLockClearKeyButton",
            Text = "Clear",
            MinWidth = 60
        };
        _clearKeyBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.ClearKey));

        keyRow.AddChild(_keyLabelInput);
        keyRow.AddChild(genKeyBtn);
        keyRow.AddChild(_exportKeyBtn);
        keyRow.AddChild(_clearKeyBtn);
        keyCol.AddChild(keyRow);

        _keyFilesContainer = new ContentBoxContainer
        {
            Name = "SecurityLockKeyFilesContainer",
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3
        };
        keyCol.AddChild(_keyFilesContainer);
        keyCard.AddChild(keyCol);
        _configContainer.AddChild(keyCard);

        // ---------------------------------------------------------------------
        // ID ACCESS SET LOCK CARD
        // ---------------------------------------------------------------------
        var accessCard = CreateSectionCard("#0C1622", "#1D445E");
        var accessCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
        };

        var accessHeaderRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _accessHeaderLabel = new ContentLabel
        {
            Text = "ID ACCESS LOCK (0 SELECTED)",
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#E0A0FF")
        };
        _matchModeBtn = new ContentButton
        {
            Name = "SecurityLockMatchModeButton",
            Text = "Match: ALL",
            MinWidth = 92
        };
        _matchModeBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.ToggleRequireAllAccess));

        _clearAccessBtn = new ContentButton
        {
            Name = "SecurityLockClearAccessButton",
            Text = "Clear",
            MinWidth = 60
        };
        _clearAccessBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(SecurityLockAction.ClearAccessTags));

        accessHeaderRow.AddChild(_accessHeaderLabel);
        accessHeaderRow.AddChild(_matchModeBtn);
        accessHeaderRow.AddChild(_clearAccessBtn);
        accessCol.AddChild(accessHeaderRow);

        _accessGrid = new ContentWrapContainer
        {
            Name = "SecurityLockAccessGrid",
            HorizontalExpand = true,
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        accessCol.AddChild(_accessGrid);
        accessCard.AddChild(accessCol);
        _configContainer.AddChild(accessCard);
        root.AddChild(_configContainer);

        scroll.AddChild(root);
        AddChild(scroll);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not SecurityLockUiState state)
            return;

        var needsAuth = state.RequiresSystemAdminAuth && !state.IsSystemAdminAuthorized;
        var osBadge = state.IsJailbrokenOs ? " [JAILBROKEN]" : string.Empty;
        _headerLabel.Text = $"SECURITY LOCK — {state.HostName} [{state.DeviceGuid}]{osBadge}";

        var methods = new List<string>();
        if (state.HasPassword)
            methods.Add("Password");
        if (state.HasKeyLock)
            methods.Add($"Key ({state.KeyLabel} [{state.KeyId}])");
        var selectedAccess = state.AccessOptions.Where(a => a.IsSelected).ToList();
        if (selectedAccess.Count > 0)
            methods.Add($"ID Access ({(state.RequireAllAccess ? "ALL" : "ANY")} of {selectedAccess.Count})");

        var hasAny = methods.Count > 0;
        // Re-locking a configured computer is always permitted without SystemAdmin authorization.
        _lockNowBtn.Disabled = !hasAny;
        _remoteLockBtn.Visible = !state.IsJailbrokenOs && !needsAuth;
        _remoteLockBtn.Text = state.RemoteLocked ? "Remote Lock: ON" : "Remote Lock: OFF";
        _clearAllBtn.Visible = !needsAuth;
        _clearAllBtn.Disabled = !hasAny && !state.IsSoftwareLocked;

        var readerNote = state.HasCardReader
            ? "Card reader ready"
            : "No ID card reader";
        _summaryLabel.Text = hasAny
            ? $"Active methods: {string.Join("  •  ", methods)}  |  {readerNote}"
            : $"No lock methods configured  |  {readerNote}";

        _statusLabel.Text = state.StatusMessage;
        _statusLabel.Visible = !string.IsNullOrWhiteSpace(state.StatusMessage) && !needsAuth;

        _authGateContainer.Visible = needsAuth;
        _configContainer.Visible = !needsAuth;
        if (needsAuth)
        {
            _authGateStatusLabel.Text = state.StatusMessage;
            _authGateStatusLabel.Visible = !string.IsNullOrWhiteSpace(state.StatusMessage);
            return;
        }

        _clearPasswordBtn.Disabled = !state.HasPassword;
        _clearKeyBtn.Disabled = !state.HasKeyLock;
        _exportKeyBtn.Disabled = !state.HasKeyLock || !state.HasInsertedDisk;
        _exportKeyBtn.ToolTip = state.HasInsertedDisk
            ? $"Export active key to '{state.InsertedDiskName}'"
            : "No data disk inserted";

        _activeKeyLabel.Text = state.HasKeyLock
            ? $"Active Key: {state.KeyLabel} [{state.KeyId}]"
            : "Active Key: None";

        _keyFilesContainer.RemoveAllChildren();
        if (state.KeyFiles.Count == 0)
        {
            _keyFilesContainer.AddChild(new ContentLabel
            {
                Text = "No .key files found in /keys/.",
                FontColorOverride = Color.FromHex("#7B95A6")
            });
        }
        else
        {
            foreach (var kf in state.KeyFiles)
            {
                var file = kf;
                var row = new ContentBoxContainer
                {
                    Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    SeparationOverride = 6
                };
                var src = file.FromDisk ? "[DISK]" : "[HDD]";
                var activeTag = file.IsActiveLockKey ? "  [ACTIVE]" : string.Empty;
                row.AddChild(new ContentLabel
                {
                    Text = $"{src} {file.FilePath} — {file.Label} [{file.KeyId}]{activeTag}",
                    HorizontalExpand = true,
                    VerticalAlignment = VAlignment.Center,
                    FontColorOverride = file.IsActiveLockKey ? Color.FromHex("#66EEAA") : Color.FromHex("#CFE8F5")
                });
                var bindBtn = new ContentButton
                {
                    Name = $"SecurityLockBindKey_{file.KeyId}",
                    Text = file.IsActiveLockKey ? "Active" : "Use Key",
                    Disabled = file.IsActiveLockKey,
                    MinWidth = 74
                };
                bindBtn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(
                    SecurityLockAction.BindKeyFile,
                    file.FilePath,
                    file.FromDisk));
                row.AddChild(bindBtn);
                _keyFilesContainer.AddChild(row);
            }
        }

        _accessHeaderLabel.Text = $"ID ACCESS LOCK ({selectedAccess.Count} SELECTED)";
        _matchModeBtn.Text = state.RequireAllAccess ? "Match: ALL" : "Match: ANY";
        _clearAccessBtn.Disabled = selectedAccess.Count == 0;

        _accessGrid.RemoveAllChildren();
        foreach (var opt in state.AccessOptions)
        {
            var accessId = opt.AccessId;
            var btn = new ContentButton
            {
                Name = $"SecurityLockAccess_{accessId}",
                Text = opt.DisplayName,
                Pressed = opt.IsSelected,
                MinHeight = 24
            };
            btn.OnPressed += _ => SendProgramMessage(new SecurityLockActionMessage(
                SecurityLockAction.ToggleAccessTag,
                accessId));
            _accessGrid.AddChild(btn);
        }
    }

    private static ContentPanelContainer CreateSectionCard(string bgHex, string borderHex)
    {
        return new ContentPanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex(bgHex),
                BorderColor = Color.FromHex(borderHex),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };
    }
}
