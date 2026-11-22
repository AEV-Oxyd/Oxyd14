using Content.Client.UserInterface.Fragments;
using Content.Shared._Oxyd.Ntos;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Oxyd.Ntos;

public sealed partial class OxydRecordsUi : UIFragment
{
    private OxydRecordsUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new OxydRecordsUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is OxydRecordsUiState s)
            _fragment?.UpdateState(s);
    }
}

public sealed class OxydRecordsUiFragment : BoxContainer
{
    public event Action<OxydRecordsUiMessageEvent>? OnMessage;

    private readonly Label _banner = new();
    private readonly Label _error = new() { Visible = false };
    private readonly BoxContainer _list = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly Label _detail = new();

    public OxydRecordsUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 320;

        AddChild(_banner);
        AddChild(_error);
        var refresh = new Button { Text = "Refresh" };
        refresh.OnPressed += _ => OnMessage?.Invoke(new OxydRecordsUiMessageEvent(OxydRecordsUiAction.Refresh));
        AddChild(refresh);
        AddChild(new ScrollContainer
        {
            VerticalExpand = true,
            MinHeight = 140,
            Children = { _list },
        });
        AddChild(_detail);
        var clear = new Button { Text = "Back to list" };
        clear.OnPressed += _ => OnMessage?.Invoke(new OxydRecordsUiMessageEvent(OxydRecordsUiAction.Clear));
        AddChild(clear);
    }

    public void UpdateState(OxydRecordsUiState state)
    {
        _error.Visible = state.Error != null;
        _error.Text = state.Error ?? string.Empty;
        _banner.Text = string.IsNullOrEmpty(state.StationName)
            ? $"Records ({state.Records.Count})"
            : $"{state.StationName} — {state.Records.Count} records";

        _list.RemoveAllChildren();
        foreach (var rec in state.Records)
        {
            var id = rec.Id;
            var btn = new Button { Text = $"{rec.Name} — {rec.Job}", HorizontalExpand = true };
            btn.OnPressed += _ => OnMessage?.Invoke(new OxydRecordsUiMessageEvent(OxydRecordsUiAction.Select, id));
            _list.AddChild(btn);
        }

        if (state.Active is { } d)
        {
            _detail.Text =
                $"{d.Name}\nJob: {d.Job}\nAge: {d.Age}\nSpecies: {d.Species}\nGender: {d.Gender}\nDNA: {d.Dna ?? "-"}\nFingerprint: {d.Fingerprint ?? "-"}";
        }
        else
            _detail.Text = "Select a record.";
    }
}
