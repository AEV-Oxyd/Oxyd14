using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Client UI for ID Access Encoder (/bin/access_encoder.nte).
/// Builds .acc access code files from an inserted PDA/computer ID card or a swiped ID card
/// and manages saved .acc files stored under /keys/ on the hard drive.
/// Uses wrapping containers and scrollable regions so long access code lists never go out of bounds.
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
            Margin = new Thickness(6)
        };

        _headerLabel = new ContentLabel
        {
            Text = "ID ACCESS ENCODER (/keys/*.acc)",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_headerLabel);

        _sourceLabel = new ContentLabel
        {
            Text = "Source ID: None (Insert ID into PDA or swipe ID card on computer)",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#FFD54F"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_sourceLabel);

        _sourceTagsWrap = new ContentWrapContainer
        {
            HorizontalSeparation = 4,
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.AddChild(_sourceTagsWrap);

        _fileNameInput = new ContentLineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = "Access file name (e.g. engineering)...",
            Margin = new Thickness(0, 0, 0, 4)
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
            VerticalSeparation = 4,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _buildBtn = new ContentButton
        {
            Text = "Build & Save .acc",
            MinWidth = 120,
            Disabled = true
        };
        _buildBtn.OnPressed += _ => SubmitBuild();

        var refreshBtn = new ContentButton
        {
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
            Text = "",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#88CCCC"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_statusLabel);

        var listHeader = new ContentLabel
        {
            Text = "Saved Access Code Files (/keys/):",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#B0BEC5"),
            Margin = new Thickness(0, 2, 0, 2)
        };
        root.AddChild(listHeader);

        _filesContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.AddChild(_filesContainer);

        _detailLabel = new ContentLabel
        {
            Text = "Select a saved .acc file to inspect its encoded access levels.",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#A5D6A7"),
            VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0)
        };
        root.AddChild(_detailLabel);

        rootScroll.AddChild(root);
        AddChild(rootScroll);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not AccessEncoderUiState state)
            return;

        _headerLabel.Text = state.HasHardDrive
            ? $"ID ACCESS ENCODER ({state.SavedFiles.Count} saved in /keys/)"
            : "ID ACCESS ENCODER [NO HARD DRIVE]";

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
            _sourceLabel.Text = "Source ID: None — Insert an ID card into your PDA or swipe an ID card on this computer.";
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
                var row = new ContentBoxContainer
                {
                    Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    Margin = new Thickness(0, 2)
                };

                var jobSuffix = string.IsNullOrWhiteSpace(entry.JobTitle) ? string.Empty : $" - {entry.JobTitle}";
                row.AddChild(new ContentLabel
                {
                    Text = $"{entry.FilePath} | {entry.OwnerName}{jobSuffix} [{entry.AccessTags.Count} codes]",
                    HorizontalExpand = true
                });

                var pathCopy = entry.FilePath;
                var inspectBtn = new ContentButton { Text = "Inspect", MinWidth = 65, Margin = new Thickness(2, 0) };
                inspectBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.SelectFile, pathCopy));
                row.AddChild(inspectBtn);

                var delBtn = new ContentButton { Text = "Delete", MinWidth = 60, Margin = new Thickness(2, 0) };
                delBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.DeleteFile, pathCopy));
                row.AddChild(delBtn);

                _filesContainer.AddChild(row);
            }
        }

        _detailLabel.Text = !string.IsNullOrWhiteSpace(state.SelectedFileSummary)
            ? state.SelectedFileSummary
            : "Tip: Use Terminal 'auth <guid> /keys/<file>.acc' (or 'auth /keys/<file>.acc' in SSH) to authorize remotely.";
    }
}
