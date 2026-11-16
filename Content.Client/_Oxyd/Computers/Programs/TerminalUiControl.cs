using Content.Client._Oxyd.Computers.ScreenSharing;
using Content.Client._Oxyd.Computers.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Interactive NTOS shell + SSH + RemoteScreenView host control.
/// Reuses existing Content* input-relaying controls and RemoteScreenView without reimplementing relaying.
/// </summary>
public sealed class TerminalUiControl : ProgramUiControl
{
    private readonly ContentLabel _headerLabel;
    private readonly ContentBoxContainer _shellPanel;
    private readonly ContentScrollContainer _historyScroll;
    private readonly ContentBoxContainer _historyLines;
    private readonly ContentLabel _promptLabel;
    private readonly ContentLineEdit _commandInput;

    private readonly ContentBoxContainer _remoteScreenPanel;
    private readonly ContentLabel _remoteHeaderLabel;
    private readonly RemoteScreenView _remoteScreenView;

    public TerminalUiControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;

        var root = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(4)
        };

        _headerLabel = new ContentLabel
        {
            Text = "NTOS Terminal",
            FontColorOverride = Color.FromHex("#00D4FF"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.AddChild(_headerLabel);

        // Live SSH Screen View Panel
        _remoteScreenPanel = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };

        var screenBar = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 4)
        };

        _remoteHeaderLabel = new ContentLabel
        {
            Text = "REMOTE DISPLAY STREAM",
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#00FF88")
        };
        screenBar.AddChild(_remoteHeaderLabel);

        var detachBtn = new ContentButton
        {
            Text = "Detach Screen",
            MinWidth = 110
        };
        detachBtn.OnPressed += _ => SendProgramMessage(new TerminalRemoteScreenActionMessage("detach"));
        screenBar.AddChild(detachBtn);

        _remoteScreenPanel.AddChild(screenBar);
        _remoteScreenView = new RemoteScreenView
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _remoteScreenPanel.AddChild(_remoteScreenView);
        root.AddChild(_remoteScreenPanel);

        // Interactive CLI Panel
        _shellPanel = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true
        };

        _historyScroll = new ContentScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _historyLines = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        _historyScroll.AddChild(_historyLines);
        _shellPanel.AddChild(_historyScroll);

        var inputRow = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 4, 0, 0)
        };

        _promptLabel = new ContentLabel
        {
            Text = "$ ",
            FontColorOverride = Color.FromHex("#00FF88"),
            Margin = new Thickness(0, 0, 4, 0)
        };
        _commandInput = new ContentLineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = "Enter command (type 'help')..."
        };
        _commandInput.OnTextEntered += args =>
        {
            var text = args.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;
            _commandInput.Text = string.Empty;
            SendProgramMessage(new TerminalExecuteCommandMessage(text));
        };

        inputRow.AddChild(_promptLabel);
        inputRow.AddChild(_commandInput);
        _shellPanel.AddChild(inputRow);
        root.AddChild(_shellPanel);

        AddChild(root);
    }

    public override void UpdateProgramState(BoundUserInterfaceState raw)
    {
        if (raw is not TerminalUiState state)
            return;

        var nicStatus = state.HasNetworkCard
            ? (state.IsRouterOnline
                ? (string.IsNullOrEmpty(state.NetworkLabel) ? "ONLINE" : $"ONLINE ({state.NetworkLabel})")
                : "NO ROUTER")
            : "NO NIC";
        var sshStr = state.SshHost != null ? $" | SSH: {state.SshHost}" : "";
        _headerLabel.Text = $"GUID: {state.DeviceGuid} | NET: {nicStatus}{sshStr}";

        if (state.IsScreenAttached && state.RemoteScreen != null)
        {
            _shellPanel.Visible = false;
            _remoteScreenPanel.Visible = true;
            _remoteHeaderLabel.Text = $"LIVE DISPLAY: {state.RemoteScreen.DeviceName} [{state.RemoteScreen.DeviceGuid}]";
            _remoteScreenView.UpdateState(state.RemoteScreen);
        }
        else
        {
            _remoteScreenPanel.Visible = false;
            _shellPanel.Visible = true;
            _promptLabel.Text = state.Prompt;

            _historyLines.RemoveAllChildren();
            foreach (var line in state.Lines)
            {
                _historyLines.AddChild(new ContentLabel
                {
                    Text = line,
                    FontColorOverride = Color.FromHex("#D6F5F5")
                });
            }
            _historyScroll.ScrollOffsetY = float.MaxValue;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _remoteScreenView.Dispose();
        base.Dispose(disposing);
    }
}
