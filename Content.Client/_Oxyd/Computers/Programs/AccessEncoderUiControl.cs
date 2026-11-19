using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Client UI for ID Access Encoder (/bin/access_encoder.nte).
/// Builds .acc access code files from an inserted PDA/computer ID card or a swiped ID card
/// and manages saved .acc files stored under /keys/ on the hard drive.
/// </summary>
public sealed class AccessEncoderUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentLabel _sourceLabel;
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

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6)
        };

        _headerLabel = new ContentLabel
        {
            Text = "ID ACCESS ENCODER (/keys/*.acc)",
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_headerLabel);

        _sourceLabel = new ContentLabel
        {
            Text = "Source ID: None (Insert ID into PDA or swipe ID card on computer)",
            FontColorOverride = Color.FromHex("#FFD54F"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_sourceLabel);

        var buildRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2, 0, 4)
        };

        _fileNameInput = new ContentLineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = "Access file name (e.g. engineering)..."
        };

        void SubmitBuild()
        {
            SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.BuildSaveFile, _fileNameInput.Text));
            _fileNameInput.Text = string.Empty;
        }

        _fileNameInput.OnTextEntered += _ => SubmitBuild();

        _buildBtn = new ContentButton
        {
            Text = "Build & Save .acc",
            MinWidth = 125,
            Margin = new Thickness(4, 0, 0, 0),
            Disabled = true
        };
        _buildBtn.OnPressed += _ => SubmitBuild();

        var refreshBtn = new ContentButton
        {
            Text = "Refresh",
            MinWidth = 68,
            Margin = new Thickness(4, 0, 0, 0)
        };
        refreshBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.Refresh));

        _clearScannedBtn = new ContentButton
        {
            Text = "Use Inserted ID",
            MinWidth = 110,
            Margin = new Thickness(4, 0, 0, 0),
            Visible = false
        };
        _clearScannedBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ClearScanned));

        buildRow.AddChild(_fileNameInput);
        buildRow.AddChild(_buildBtn);
        buildRow.AddChild(refreshBtn);
        buildRow.AddChild(_clearScannedBtn);
        root.AddChild(buildRow);

        _statusLabel = new ContentLabel
        {
            Text = "",
            FontColorOverride = Color.FromHex("#88CCCC"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_statusLabel);

        var listHeader = new ContentLabel
        {
            Text = "Saved Access Code Files (/keys/):",
            FontColorOverride = Color.FromHex("#B0BEC5"),
            Margin = new Thickness(0, 2, 0, 2)
        };
        root.AddChild(listHeader);

        var scroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _filesContainer = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        scroll.AddChild(_filesContainer);
        root.AddChild(scroll);

        _detailLabel = new ContentLabel
        {
            Text = "Select a saved .acc file to inspect its encoded access levels.",
            FontColorOverride = Color.FromHex("#A5D6A7"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        root.AddChild(_detailLabel);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not AccessEncoderUiState state)
            return;

        _headerLabel.Text = state.HasHardDrive
            ? $"ID ACCESS ENCODER ({state.SavedFiles.Count} saved in /keys/)"
            : "ID ACCESS ENCODER [NO HARD DRIVE]";

        if (state.HasSourceCard)
        {
            var tagPreview = state.SourceAccessTags.Count > 0
                ? string.Join(", ", state.SourceAccessTags)
                : "(no access tags)";
            var jobPart = string.IsNullOrWhiteSpace(state.SourceJobTitle) ? string.Empty : $" ({state.SourceJobTitle})";
            _sourceLabel.Text =
                $"Source: {state.SourceLabel} | Holder: {state.SourceOwnerName}{jobPart}\n" +
                $"Access Codes ({state.SourceAccessTags.Count}): {tagPreview}";
            _sourceLabel.FontColorOverride = Color.FromHex("#55FF88");
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
