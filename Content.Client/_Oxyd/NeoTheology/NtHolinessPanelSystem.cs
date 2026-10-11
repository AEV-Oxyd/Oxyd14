using System.Collections.Generic;
using Content.Client._Oxyd.Framework;
using Content.Client._Oxyd.UI;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.NeoTheology;

/// <summary>
/// Continuous holiness readout in the stat panel. Eris only surfaces cruciform power inside the
/// ritual-book window; the fork shows it as a stat row in the "IC" category, next to the Sanity
/// "Focus Insight" button. The networked <see cref="CruciformComponent"/> is read every frame so
/// regen ticks and cast debits stay live.
/// </summary>
public sealed partial class NtHolinessPanelSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private StatusPanelSystem _panels = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const string Category = "IC";
    private const string PanelName = "NtHoliness";

    private ErisSpecRow? _row;
    private ErisBar? _bar;

    public override void Initialize()
    {
        SubscribeLocalEvent<CruciformBearerComponent, CollectEntityPanels>(OnPanelRequest);
        SubscribeLocalEvent<CruciformBearerComponent, ComponentStartup>(OnBearerStartup);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnPanelRequest(Entity<CruciformBearerComponent> ent, ref CollectEntityPanels ev)
    {
        // The collect event is also raised on examine targets; only the local player's own
        // cruciform drives the readout.
        if (ent.Owner != _player.LocalEntity || RowExists())
            return;

        var row = BuildRow();
        if (!ev.adding.TryGetValue(Category, out var list))
            ev.adding[Category] = new List<Control>();
        ev.adding[Category].Add(row);
    }

    private void OnBearerStartup(Entity<CruciformBearerComponent> ent, ref ComponentStartup args)
    {
        // A cruciform implanted mid-round doesn't re-trigger panel collection — append directly.
        // (A bearer seen during initial entity sync arrives before LocalEntity attaches, so the
        // Update-path ensure below remains the backstop for the join-with-cruciform case.)
        if (ent.Owner != _player.LocalEntity || RowExists())
            return;

        RaiseLocalEvent(new AddToPanel(Category, BuildRow()));
        _panels.refreshContent(Category);
    }

    private ErisSpecRow BuildRow()
    {
        var row = new ErisSpecRow("Holiness", 68f) { Name = PanelName };
        var bar = new ErisBar { HorizontalExpand = true, MinWidth = 110f };
        row.AddValue(bar);
        _row = row;
        _bar = bar;
        return row;
    }

    private bool RowExists()
        => _panels.panelContent.TryGetValue(Category, out var category) &&
           ClientOxydHelpers.FindControl<BoxContainer>(category, PanelName, out _);

    private void OnDetached(LocalPlayerDetachedEvent ev)
    {
        _row = null;
        _bar = null;
    }

    public override void Update(float frameTime)
    {
        var cruciform = _player.LocalEntity is { } player &&
                        TryComp<CruciformBearerComponent>(player, out var bearer)
            ? bearer.Cruciform
            : null;

        // Backstop: a bearer already present at join arrives before LocalEntity attaches, so
        // neither the collect nor the startup path can add the row — ensure it lazily instead.
        if (cruciform is not null && !RowExists())
        {
            RaiseLocalEvent(new AddToPanel(Category, BuildRow()));
            _panels.refreshContent(Category);
        }

        if (_row is null || _bar is null)
            return;

        if (cruciform is not { } entity || !TryComp<CruciformComponent>(entity, out var comp))
        {
            _row.Visible = false;
            return;
        }

        _row.Visible = true;
        // The server settles comp.Holiness lazily without dirtying it — extrapolate
        // regen locally instead of rendering the (frozen) networked value.
        var holiness = NeoTheologyHoliness.Extrapolate(comp, _timing.CurTime);
        var max = Math.Max(1.0, comp.MaxHoliness);
        _bar.SetValue((float)(holiness / max), ErisNanoColors.BarDefault,
            $"{holiness:0} / {comp.MaxHoliness:0}");
    }
}
