using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Dedicated UI for /bin/access_encoder.nte: reads a single ID card from an installed
/// ID Card Slot module (or PDA slot) and encodes / manages /keys/*.acc access files.
/// </summary>
public sealed class AccessEncoderUiControl : ProgramUiControl
{
    private readonly ContentLabel _hardwareLabel;
    private readonly ContentButton _slotToggleBtn;
    private readonly ContentButton _readSlotBtn;
    private readonly ContentButton _clearBtn;
    private readonly ContentLineEdit _fileNameInput;
    private readonly ContentButton _buildBtn;
    private readonly ContentLabel _statusLabel;
    private readonly ContentLabel _cardOwnerLabel;
    private readonly ContentLabel _cardJobLabel;
    private readonly ContentWrapContainer _tagsWrap;
    private readonly ContentBoxContainer _filesList;
    private readonly ContentLabel _previewLabel;

    public AccessEncoderUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
            SeparationOverride = 6
        };

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
            Name = "AccessEncoderHardwareLabel",
            Text = "ID Slot Hardware: Checking...",
            FontColorOverride = Color.FromHex("#8AD8FF"),
            WordWrap = true
        };
        hwCol.AddChild(_hardwareLabel);

        var slotActions = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        _slotToggleBtn = new ContentButton
        {
            Name = "AccessEncoderSlotBtn",
            Text = "Slot #1: Empty (Insert Hand ID)",
            MinWidth = 185,
            MinHeight = 26
        };
        _slotToggleBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ToggleCardSlot));

        _readSlotBtn = new ContentButton
        {
            Name = "AccessEncoderReadSlotBtn",
            Text = "Read ID Slot",
            MinWidth = 100,
            MinHeight = 26
        };
        _readSlotBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ReadSlotCard));

        _clearBtn = new ContentButton
        {
            Name = "AccessEncoderClearBtn",
            Text = "Clear Readout",
            MinWidth = 100,
            MinHeight = 26
        };
        _clearBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.ClearScanned));

        slotActions.AddChild(_slotToggleBtn);
        slotActions.AddChild(_readSlotBtn);
        slotActions.AddChild(_clearBtn);
        hwCol.AddChild(slotActions);
        hwPanel.AddChild(hwCol);
        root.AddChild(hwPanel);

        // Top encode action bar
        var topRow = new ContentWrapContainer { HorizontalSeparation = 4, VerticalSeparation = 4 };
        _fileNameInput = new ContentLineEdit
        {
            Name = "AccessEncoderFileNameInput",
            PlaceHolder = "Key name (e.g. chief -> /keys/chief.acc)",
            MinWidth = 240,
            HorizontalExpand = true,
            MinHeight = 28
        };
        _fileNameInput.OnTextEntered += _ =>
        {
            SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.BuildSaveFile, _fileNameInput.Text));
        };

        _buildBtn = new ContentButton
        {
            Name = "AccessEncoderBuildBtn",
            Text = "Encode ID -> .acc",
            MinWidth = 132,
            MinHeight = 28
        };
        _buildBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.BuildSaveFile, _fileNameInput.Text));

        var refreshBtn = new ContentButton
        {
            Name = "AccessEncoderRefreshBtn",
            Text = "Refresh",
            MinWidth = 76,
            MinHeight = 28
        };
        refreshBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.Refresh));

        topRow.AddChild(_fileNameInput);
        topRow.AddChild(_buildBtn);
        topRow.AddChild(refreshBtn);
        root.AddChild(topRow);

        _statusLabel = new ContentLabel
        {
            Name = "AccessEncoderStatusLabel",
            Text = string.Empty,
            FontColorOverride = Color.FromHex("#88E0B8"),
            WordWrap = true
        };
        root.AddChild(_statusLabel);

        // Split content: left = Readout of inserted ID card, right = /keys/*.acc directory + inspector
        var bodySplit = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 8
        };

        var cardPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1f,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1821"),
                BorderColor = Color.FromHex("#274C66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8
            }
        };

        var cardCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 5
        };

        cardCol.AddChild(new ContentLabel
        {
            Text = "READ ID CARD BUFFER",
            FontColorOverride = Color.FromHex("#77DDFF")
        });

        _cardOwnerLabel = new ContentLabel
        {
            Name = "AccessEncoderCardOwnerLabel",
            Text = "Cardholder: (none)",
            FontColorOverride = Color.FromHex("#E6F2FA"),
            WordWrap = true
        };
        _cardJobLabel = new ContentLabel
        {
            Name = "AccessEncoderCardJobLabel",
            Text = "Role: (none)",
            FontColorOverride = Color.FromHex("#B3CEE0"),
            WordWrap = true
        };
        cardCol.AddChild(_cardOwnerLabel);
        cardCol.AddChild(_cardJobLabel);

        cardCol.AddChild(new ContentLabel
        {
            Text = "Extracted Access Codes:",
            FontColorOverride = Color.FromHex("#99B8CC")
        });

        var tagsScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true
        };
        _tagsWrap = new ContentWrapContainer
        {
            HorizontalExpand = true,
            HorizontalSeparation = 4,
            VerticalSeparation = 4
        };
        tagsScroll.AddChild(_tagsWrap);
        cardCol.AddChild(tagsScroll);
        cardPanel.AddChild(cardCol);
        bodySplit.AddChild(cardPanel);

        var filesPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1.15f,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0F1821"),
                BorderColor = Color.FromHex("#274C66"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8
            }
        };

        var filesCol = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 5
        };

        filesCol.AddChild(new ContentLabel
        {
            Text = "/keys/*.acc CREDENTIAL FILES",
            FontColorOverride = Color.FromHex("#77DDFF")
        });

        var filesScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1f,
            HScrollEnabled = false,
            VScrollEnabled = true
        };
        _filesList = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4
        };
        filesScroll.AddChild(_filesList);
        filesCol.AddChild(filesScroll);

        var previewPanel = new ContentPanelContainer
        {
            HorizontalExpand = true,
            MinHeight = 84,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#091119"),
                BorderColor = Color.FromHex("#224055"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5
            }
        };
        _previewLabel = new ContentLabel
        {
            Text = "Select a .acc file to inspect its encoded credentials.",
            FontColorOverride = Color.FromHex("#CBE4F5"),
            WordWrap = true
        };
        previewPanel.AddChild(_previewLabel);
        filesCol.AddChild(previewPanel);

        filesPanel.AddChild(filesCol);
        bodySplit.AddChild(filesPanel);
        root.AddChild(bodySplit);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not AccessEncoderUiState state)
            return;

        _statusLabel.Text = state.StatusMessage;

        if (!state.HasIdSlotHardware)
        {
            _hardwareLabel.Text = "ID Slot Hardware: MISSING (0/1). Install an ID Card Slot module to read & encode cards.";
            _hardwareLabel.FontColorOverride = Color.FromHex("#FF8877");
        }
        else if (state.HasModularSlot)
        {
            _hardwareLabel.Text = state.SlotOccupied
                ? $"ID Slot Hardware: READY — Inserted: {state.SlotCardLabel}"
                : "ID Slot Hardware: READY — Slot Empty (Insert ID card)";
            _hardwareLabel.FontColorOverride = Color.FromHex("#72E89B");
        }
        else
        {
            _hardwareLabel.Text = state.SlotOccupied
                ? $"PDA ID Slot: READY — Inserted: {state.SlotCardLabel}"
                : "PDA ID Slot: Empty (Insert ID into PDA slot)";
            _hardwareLabel.FontColorOverride = Color.FromHex("#72E89B");
        }

        _slotToggleBtn.Visible = state.HasModularSlot;
        _slotToggleBtn.Text = state.SlotOccupied
            ? $"Eject: {Truncate(state.SlotCardLabel, 24)}"
            : "Insert Hand ID -> Slot";
        _slotToggleBtn.Disabled = !state.HasModularSlot;

        _readSlotBtn.Disabled = !state.HasIdSlotHardware || !state.SlotOccupied;
        _buildBtn.Disabled = !state.HasIdSlotHardware || string.IsNullOrWhiteSpace(state.CardOwner);

        _cardOwnerLabel.Text = string.IsNullOrWhiteSpace(state.CardOwner)
            ? "Cardholder: (none)"
            : $"Cardholder: {state.CardOwner}";
        _cardJobLabel.Text = string.IsNullOrWhiteSpace(state.CardJob)
            ? "Role: (none)"
            : $"Role: {state.CardJob}";

        _tagsWrap.RemoveAllChildren();
        if (state.ScannedTags.Count == 0)
        {
            _tagsWrap.AddChild(new ContentLabel
            {
                Text = state.HasIdSlotHardware
                    ? "No access tags scanned yet. Insert an ID card and click Read ID Slot."
                    : "ID card reading unavailable until an ID Card Slot module is installed.",
                FontColorOverride = Color.FromHex("#8899AA"),
                WordWrap = true
            });
        }
        else
        {
            foreach (var tag in state.ScannedTags)
            {
                var pill = new ContentPanelContainer
                {
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = Color.FromHex("#143547"),
                        BorderColor = Color.FromHex("#2E86AB"),
                        BorderThickness = new Thickness(1),
                        ContentMarginLeftOverride = 6,
                        ContentMarginRightOverride = 6,
                        ContentMarginTopOverride = 2,
                        ContentMarginBottomOverride = 2
                    }
                };
                pill.AddChild(new ContentLabel
                {
                    Text = tag,
                    FontColorOverride = Color.FromHex("#AEEFFF")
                });
                _tagsWrap.AddChild(pill);
            }
        }

        _filesList.RemoveAllChildren();
        if (state.KeyFiles.Count == 0)
        {
            _filesList.AddChild(new ContentLabel
            {
                Text = "No .acc files stored in /keys/.",
                FontColorOverride = Color.FromHex("#8899AA"),
                WordWrap = true
            });
        }
        else
        {
            foreach (var path in state.KeyFiles)
            {
                var pathCopy = path;
                var row = new ContentBoxContainer
                {
                    Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    SeparationOverride = 4
                };

                var inspectBtn = new ContentButton
                {
                    Name = $"AccessEncoderInspectBtn_{pathCopy}",
                    Text = pathCopy,
                    HorizontalExpand = true,
                    Pressed = string.Equals(pathCopy, state.SelectedFilePath, StringComparison.OrdinalIgnoreCase)
                };
                inspectBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.SelectFile, pathCopy));

                var delBtn = new ContentButton
                {
                    Name = $"AccessEncoderDeleteBtn_{pathCopy}",
                    Text = "Delete",
                    MinWidth = 64
                };
                delBtn.OnPressed += _ => SendProgramMessage(new AccessEncoderActionMessage(AccessEncoderAction.DeleteFile, pathCopy));

                row.AddChild(inspectBtn);
                row.AddChild(delBtn);
                _filesList.AddChild(row);
            }
        }

        _previewLabel.Text = string.IsNullOrWhiteSpace(state.SelectedFilePreview)
            ? "Select a .acc file to inspect its encoded credentials."
            : state.SelectedFilePreview;
    }

    private static string Truncate(string text, int maxLen)
    {
        return text.Length <= maxLen ? text : text[..Math.Max(1, maxLen - 2)] + "..";
    }
}
