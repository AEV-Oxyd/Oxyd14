using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Economy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Economy;

public sealed class OxydArtistBenchBoundUserInterface : BoundUserInterface
{
    private OxydArtistBenchWindow? _window;

    public OxydArtistBenchBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<OxydArtistBenchWindow>();
        _window.OnCreate += insight => SendMessage(new OxydArtistBenchCreateMessage { InsightUsed = insight });
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OxydArtistBenchUiState s)
            _window?.UpdateState(s);
    }
}

public sealed class OxydArtistBenchWindow : FancyWindow
{
    public event Action<float>? OnCreate;

    private readonly Label _status = new();
    private readonly Label _error = new() { Visible = false };
    private readonly LineEdit _insight = new() { PlaceHolder = "Insight to invest", MinWidth = 120 };
    private readonly Button _create = new() { Text = "Create artwork" };

    public OxydArtistBenchWindow()
    {
        Title = "Artist's bench";
        SetSize = new Vector2(360, 200);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        root.AddChild(_status);
        root.AddChild(_error);
        root.AddChild(new Label { Text = "Wood/steel/glass optional in this port — insight is the currency." });

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        row.AddChild(_insight);
        _create.OnPressed += _ =>
        {
            float.TryParse(_insight.Text, out var v);
            OnCreate?.Invoke(v);
        };
        row.AddChild(_create);
        root.AddChild(row);
        ContentsContainer.AddChild(root);
    }

    public void UpdateState(OxydArtistBenchUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _status.Text = state.Status
                       ?? $"Insight {state.Insight:0} (min {state.MinInsight:0}, max {state.MaxInsight:0})";
        _create.Disabled = !state.CanCreate;
        if (string.IsNullOrWhiteSpace(_insight.Text))
            _insight.Text = ((int)MathF.Max(state.MinInsight, MathF.Min(state.Insight, state.MaxInsight))).ToString();
    }
}
