using Robust.Server.GameObjects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.UserInterface;
using Content.Shared._Oxyd.NeoTheology.UI;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Bioreactor metrics console (Eris <c>bioreactor.tmpl</c>): read-only status pane. Pushes a
/// snapshot on open and re-pushes once a second while the window stays open so live chamber
/// changes show up — NanoUI's auto-refresh equivalent.
/// </summary>
public sealed partial class BioreactorSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const float ConsoleRefresh = 1f;
    private float _consoleAccum;

    [SubscribeLocalEvent]
    private void OnBioreactorUiOpened(Entity<BioreactorComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        PushConsoleState(ent);
    }

    private void PushConsoleState(Entity<BioreactorComponent> ent)
    {
        var contents = 0;
        foreach (var _ in ChamberContents(ent.Owner))
            contents++;

        _ui.SetUiState(ent.Owner, BioreactorConsoleUiKey.Key, new BioreactorConsoleState(
            _machines.IsOperational(ent.Owner),
            ent.Comp.ChamberClosed,
            ent.Comp.ChamberSolution,
            ent.Comp.ChamberBreached,
            contents));
    }

    private void UpdateConsoles(float frameTime)
    {
        _consoleAccum += frameTime;
        if (_consoleAccum < ConsoleRefresh)
            return;
        _consoleAccum = 0f;

        var query = EntityQueryEnumerator<BioreactorComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var reactor, out var ui))
        {
            if (_ui.IsUiOpen((uid, ui), BioreactorConsoleUiKey.Key))
                PushConsoleState((uid, reactor));
        }
    }
}
