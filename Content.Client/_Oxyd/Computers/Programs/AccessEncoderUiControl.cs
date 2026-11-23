using System.Linq;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Client UI for ID Access Encoder &amp; ID Modification Console (/bin/access_encoder.nte and /bin/id_console.nte).
/// 1) Builds .acc access code files from an inserted PDA/computer/console ID card or a swiped ID card
///    and manages saved .acc files stored under /keys/ on the hard drive.
/// 2) Modifies a target ID card's Full Name, Job Title, and Access Levels, authorized either by
///    a Privileged ID card OR by a .acc file stored on the computer's filesystem.
/// </summary>
public sealed class AccessEncoderUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _sourceLabel;
    private readonly ContentWrapContainer _sourceTagsWrap;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLineEdit _fileNameInput;
    private readonly ContentButton _buildBtn;
    private readonly ContentButton _clearScannedBtn;
    private readonly ContentBoxContainer _filesContainer;
    private readonly ContentLabel _detailLabel;

    // ID Modification & Authorization Console controls
    private readonly ContentBoxContainer _slotButtonsRow;
    private readonly ContentButton _privSlotBtn;
    private readonly ContentButton _targetSlotBtn;
    private readonly ContentLabel _authSourceLabel;
    private readonly ContentLabel _targetCardLabel;
    private readonly ContentLineEdit _targetFullNameEdit;
    private readonly ContentLineEdit _targetJobTitleEdit;
    private readonly ContentButton _saveIdentityBtn;
    private readonly ContentButton _applyAccToTargetBtn;
    private readonly ContentButton _grantAllBtn;
    private readonly ContentButton _revokeAllBtn;
    private readonly ContentWrapContainer _accessButtonsWrap;

    private string _lastTargetCardLabel = string.Empty;
    private string _lastTargetName = string.Empty;
    private string _lastTargetJob = string.Empty;

    public AccessEncoderUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var rootScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 4
        };

        _headerLabel = new ContentLabel
        {
            Text = "ID AUTHORIZATION & ACCESS ENCODER (/keys/*.acc)",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 0, 0, 2)
        };
        root.AddChild(_headerLabel);

        // Dual physical ID slot controls when running on an ID Card Console host
        _slotButtonsRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
            Visible = false
        };
        _privSlotBtn = new ContentButton
        {
            Name = "IdModPrivSlotBtn",
            Text = "Insert Privileged ID",
            MinWidth = 148
        };
        _privSlotBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.TogglePrivilegedSlot));

        _targetSlotBtn = new ContentButton
        {
            Name = "IdModTargetSlotBtn",
            Text = "Insert Target ID",
            MinWidth = 138
        };
        _targetSlotBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ToggleTargetSlot));

        _slotButtonsRow.AddChild(_privSlotBtn);
        _slotButtonsRow.AddChild(_targetSlotBtn);
        root.AddChild(_slotButtonsRow);

        _sourceLabel = new ContentLabel
        {
            Text = "Source ID: None (Insert ID into PDA/console or swipe ID card on computer)",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#FFD54F")
        };
        root.AddChild(_sourceLabel);

        _sourceTagsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_sourceTagsWrap);

        _fileNameInput = new ContentLineEdit
        {
            Name = "AccessEncoderFileNameInput",
            HorizontalExpand = true,
            PlaceHolder = "Access file name (e.g. engineering)..."
        };

        void SubmitBuild()
        {
            SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.BuildSaveFile, _fileNameInput.Text));
            _fileNameInput.Text = string.Empty;
        }

        _fileNameInput.OnTextEntered += _ => SubmitBuild();
        root.AddChild(_fileNameInput);

        var actionsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };

        _buildBtn = new ContentButton
        {
            Name = "AccessEncoderBuildBtn",
            Text = "Build & Save .acc",
            MinWidth = 120,
            Disabled = true
        };
        _buildBtn.OnPressed += _ => SubmitBuild();

        var refreshBtn = new ContentButton
        {
            Name = "AccessEncoderRefreshBtn",
            Text = "Refresh",
            MinWidth = 68
        };
        refreshBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.Refresh));

        _clearScannedBtn = new ContentButton
        {
            Text = "Use Inserted ID",
            MinWidth = 110,
            Visible = false
        };
        _clearScannedBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ClearScanned));

        actionsWrap.AddChild(_buildBtn);
        actionsWrap.AddChild(refreshBtn);
        actionsWrap.AddChild(_clearScannedBtn);
        root.AddChild(actionsWrap);

        _statusLabel = new ContentLabel
        {
            Name = "AccessEncoderStatusLabel",
            Text = "",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC")
        };
        root.AddChild(_statusLabel);

        var listHeader = new ContentLabel
        {
            Text = "Saved Access Code Files (/keys/*.acc):",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#B0BEC5"),
            Margin = new Thickness(0, 2, 0, 1)
        };
        root.AddChild(listHeader);

        _filesContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 2
        };
        root.AddChild(_filesContainer);

        _detailLabel = new ContentLabel
        {
            Text = "Select a saved .acc file to authorize ID modifications or inspect encoded access levels.",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#A5D6A7"),
            VerticalAlignment = VAlignment.Top
        };
        root.AddChild(_detailLabel);

        // =====================================================================
        // ID CARD MODIFICATION & AUTHORIZATION CONSOLE SECTION
        // =====================================================================
        var modPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 4, 0, 0),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0D1824"),
                BorderColor = Color.FromHex("#245C7E"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        var modBox = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 5
        };

        modBox.AddChild(new ContentLabel
        {
            Text = "ID CARD MODIFICATION & .ACC AUTHORIZATION",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalExpand = true
        });

        _authSourceLabel = new ContentLabel
        {
            Name = "IdModAuthSourceLabel",
            Text = "Auth Source: None",
            FontColorOverride = Color.FromHex("#FFD54F"),
            HorizontalExpand = true
        };
        modBox.AddChild(_authSourceLabel);

        _targetCardLabel = new ContentLabel
        {
            Name = "IdModTargetCardLabel",
            Text = "Target ID Card: None",
            FontColorOverride = Color.FromHex("#88CCCC"),
            HorizontalExpand = true
        };
        modBox.AddChild(_targetCardLabel);

        var idFieldsRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6
        };
        _targetFullNameEdit = new ContentLineEdit
        {
            Name = "IdModFullNameEdit",
            PlaceHolder = "Target Full Name...",
            HorizontalExpand = true,
            MinHeight = 25f
        };
        _targetJobTitleEdit = new ContentLineEdit
        {
            Name = "IdModJobTitleEdit",
            PlaceHolder = "Target Job Title...",
            HorizontalExpand = true,
            MinHeight = 25f
        };
        _saveIdentityBtn = new ContentButton
        {
            Name = "IdModSaveIdentityBtn",
            Text = "Save Identity",
            MinWidth = 104,
            Disabled = true
        };
        void SubmitIdentity()
        {
            SendProgramMessage(new AccessEncoderActionMessage(
                AccessEncoderAction.WriteTargetIdentity,
                _targetFullNameEdit.Text,
                _targetJobTitleEdit.Text));
        }
        _targetFullNameEdit.OnTextEntered += _ => SubmitIdentity();
        _targetJobTitleEdit.OnTextEntered += _ => SubmitIdentity();
        _saveIdentityBtn.OnPressed += _ => SubmitIdentity();

        idFieldsRow.AddChild(_targetFullNameEdit);
        idFieldsRow.AddChild(_targetJobTitleEdit);
        idFieldsRow.AddChild(_saveIdentityBtn);
        modBox.AddChild(idFieldsRow);

        var bulkRow = new ContentWrapContainer
        {
            HorizontalSeparation = 6,
            VerticalSeparation = 4
        };
        _applyAccToTargetBtn = new ContentButton
        {
            Name = "IdModApplyAccBtn",
            Text = "Apply .acc to Target ID",
            MinWidth = 152,
            Disabled = true
        };
        _applyAccToTargetBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ApplyAccFileToTarget));

        _grantAllBtn = new ContentButton
        {
            Name = "IdModGrantAllBtn",
            Text = "Grant All Allowed",
            MinWidth = 124,
            Disabled = true
        };
        _grantAllBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.GrantAllAllowedAccess));

        _revokeAllBtn = new ContentButton
        {
            Name = "IdModRevokeAllBtn",
            Text = "Revoke Allowed",
            MinWidth = 114,
            Disabled = true
        };
        _revokeAllBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.RevokeAllAllowedAccess));

        bulkRow.AddChild(_applyAccToTargetBtn);
        bulkRow.AddChild(_grantAllBtn);
        bulkRow.AddChild(_revokeAllBtn);
        modBox.AddChild(bulkRow);

        _accessButtonsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        modBox.AddChild(_accessButtonsWrap);

        modPanel.AddChild(modBox);
        root.AddChild(modPanel);

        rootScroll.AddChild(root);
        AddChild(rootScroll);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not AccessEncoderUiState state)
            return;

        _headerLabel.Text = state.HasHardDrive
            ? $"ID AUTHORIZATION & ACCESS ENCODER ({state.SavedFiles.Count} .acc in /keys/)"
            : "ID AUTHORIZATION & ACCESS ENCODER [NO HARD DRIVE]";

        _slotButtonsRow.Visible = state.HasHostIdSlots;
        _privSlotBtn.Text = state.HasPrivilegedSlotCard ? "Eject Privileged ID" : "Insert Privileged ID";
        _targetSlotBtn.Text = state.HasTargetSlotCard ? "Eject Target ID" : "Insert Target ID";

        _sourceTagsWrap.RemoveAllChildren();
        if (state.HasSourceCard)
        {
            var jobPart = string.IsNullOrWhiteSpace(state.SourceJobTitle) ? string.Empty : $" ({state.SourceJobTitle})";
            _sourceLabel.Text =
                $"Source: {state.SourceLabel} | Holder: {state.SourceOwnerName}{jobPart} ({state.SourceAccessTags.Count} codes):";
            _sourceLabel.FontColorOverride = Color.FromHex("#55FF88");

            if (state.SourceAccessTags.Count == 0)
            {
                _sourceTagsWrap.AddChild(new ContentLabel
                {
                    Text = "(no access tags)",
                    WordWrap = false,
                    FontColorOverride = Color.FromHex("#78909C")
                });
            }
            else
            {
                foreach (var tag in state.SourceAccessTags)
                {
                    var chip = new ContentPanelContainer
                    {
                        PanelOverride = new StyleBoxFlat
                        {
                            BackgroundColor = Color.FromHex("#132A38"),
                            BorderColor = Color.FromHex("#2E6B8A"),
                            BorderThickness = new Thickness(1),
                            ContentMarginLeftOverride = 5,
                            ContentMarginRightOverride = 5,
                            ContentMarginTopOverride = 2,
                            ContentMarginBottomOverride = 2
                        }
                    };
                    chip.AddChild(new ContentLabel
                    {
                        Text = tag,
                        WordWrap = false,
                        FontColorOverride = Color.FromHex("#B3E5FC")
                    });
                    _sourceTagsWrap.AddChild(chip);
                }
            }
        }
        else
        {
            _sourceLabel.Text = "Source ID: None — Insert an ID card into your PDA/console or swipe an ID card on this computer.";
            _sourceLabel.FontColorOverride = Color.FromHex("#FFD54F");
        }

        _buildBtn.Disabled = !state.HasHardDrive || !state.HasSourceCard;
        _clearScannedBtn.Visible = state.UsingScannedCard;
        _statusLabel.Text = state.StatusMessage;

        _filesContainer.RemoveAllChildren();
        if (state.SavedFiles.Count == 0)
        {
            _filesContainer.AddChild(new ContentLabel
            {
                Text = "  (No access code files saved on hard drive yet)",
                HorizontalExpand = true,
                FontColorOverride = Color.FromHex("#78909C")
            });
        }
        else
        {
            foreach (var entry in state.SavedFiles)
            {
                var isSelected = !string.IsNullOrEmpty(state.SelectedFilePath) &&
                                 string.Equals(entry.FilePath, state.SelectedFilePath, StringComparison.OrdinalIgnoreCase);
                var row = new ContentBoxContainer
                {
                    Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    Margin = new Thickness(0, 1),
                    SeparationOverride = 4
                };

                var jobSuffix = string.IsNullOrWhiteSpace(entry.JobTitle) ? string.Empty : $" - {entry.JobTitle}";
                row.AddChild(new ContentLabel
                {
                    Text = $"{(isSelected ? "[ACTIVE] " : "")}{entry.FilePath} | {entry.OwnerName}{jobSuffix} [{entry.AccessTags.Count} codes]",
                    HorizontalExpand = true,
                    FontColorOverride = isSelected ? Color.FromHex("#66FFAA") : Color.FromHex("#E0F4FF")
                });

                var pathCopy = entry.FilePath;
                var inspectBtn = new ContentButton
                {
                    Name = $"AccSelectBtn_{entry.FileName}",
                    Text = isSelected ? "Active Auth" : "Use Auth",
                    MinWidth = 78,
                    Pressed = isSelected
                };
                inspectBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.SelectFile, pathCopy));
                row.AddChild(inspectBtn);

                var applyBtn = new ContentButton
                {
                    Name = $"AccApplyBtn_{entry.FileName}",
                    Text = "Apply to ID",
                    MinWidth = 82,
                    Disabled = !state.HasTargetCard
                };
                applyBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ApplyAccFileToTarget, pathCopy));
                row.AddChild(applyBtn);

                var delBtn = new ContentButton { Text = "Delete", MinWidth = 58 };
                delBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.DeleteFile, pathCopy));
                row.AddChild(delBtn);

                _filesContainer.AddChild(row);
            }
        }

        _detailLabel.Text = !string.IsNullOrWhiteSpace(state.SelectedFileSummary)
            ? state.SelectedFileSummary
            : "Tip: Select any .acc file above to authorize ID modifications or run 'auth <guid> /keys/<file>.acc' in Terminal.";

        // Update ID Card Modification Section
        _authSourceLabel.Text = $"Auth Source: {state.PrivilegedAuthLabel} ({state.AllowedAccessTags.Count} authorized access levels)";
        _authSourceLabel.FontColorOverride = state.HasPrivilegedAuth ? Color.FromHex("#66EEAA") : Color.FromHex("#FFD54F");

        _targetCardLabel.Text = state.HasTargetCard
            ? $"Target ID: {state.TargetCardLabel} — {state.TargetFullName} ({state.TargetJobTitle}) [{state.TargetAccessTags.Count} active codes]"
            : "Target ID: None (Insert ID card into Target slot / computer ID slot or swipe ID card)";
        _targetCardLabel.FontColorOverride = state.HasTargetCard ? Color.FromHex("#88EEFF") : Color.FromHex("#8899AA");

        var canModify = state.HasTargetCard && state.HasPrivilegedAuth;
        _targetFullNameEdit.Editable = canModify;
        _targetJobTitleEdit.Editable = canModify;
        _saveIdentityBtn.Disabled = !canModify;
        _applyAccToTargetBtn.Disabled = !state.HasTargetCard || state.SavedFiles.Count == 0;
        _grantAllBtn.Disabled = !canModify;
        _revokeAllBtn.Disabled = !canModify;

        if (_lastTargetCardLabel != state.TargetCardLabel ||
            _lastTargetName != state.TargetFullName ||
            _lastTargetJob != state.TargetJobTitle)
        {
            _lastTargetCardLabel = state.TargetCardLabel;
            _lastTargetName = state.TargetFullName;
            _lastTargetJob = state.TargetJobTitle;
            _targetFullNameEdit.Text = state.TargetFullName;
            _targetJobTitleEdit.Text = state.TargetJobTitle;
        }

        var allowedSet = state.AllowedAccessTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeSet = state.TargetAccessTags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        _accessButtonsWrap.RemoveAllChildren();
        foreach (var level in state.ConsoleAccessLevels)
        {
            var tagCopy = level;
            var hasTag = activeSet.Contains(tagCopy);
            var isAllowed = allowedSet.Contains(tagCopy);

            var btn = new ContentButton
            {
                Name = $"IdModAccessBtn_{tagCopy}",
                Text = hasTag ? $"[x] {tagCopy}" : $"[ ] {tagCopy}",
                Pressed = hasTag,
                Disabled = !state.HasTargetCard || !isAllowed,
                MinWidth = 104
            };
            btn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ToggleTargetAccessTag, tagCopy));
            _accessButtonsWrap.AddChild(btn);
        }
    }
}
