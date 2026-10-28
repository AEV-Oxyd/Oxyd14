using Robust.Server.GameObjects;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Power.Generator;
using Content.Shared.UserInterface;
using Content.Shared._Oxyd.NeoTheology.UI;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Biogenerator screen (Eris <c>nt_biogen.tmpl</c>): read-only status pane. Pushes a snapshot
/// on open and re-pushes once a second while the window stays open — NanoUI's auto-refresh
/// equivalent — so fuel burn and fouling show live.
/// </summary>
public sealed partial class BiogeneratorSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    private const float ConsoleRefresh = 1f;
    private float _consoleAccum;

    [SubscribeLocalEvent]
    private void OnBiogeneratorUiOpened(Entity<BiogeneratorComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        PushConsoleState(ent);
    }

    private void PushConsoleState(Entity<BiogeneratorComponent> ent)
    {
        var on = false;
        float fuelAmount = 0f, targetPower = 0f, maxTargetPower = 0f;
        if (TryComp<FuelGeneratorComponent>(ent.Owner, out var fuel))
        {
            on = fuel.On;
            targetPower = fuel.TargetPower;
            maxTargetPower = fuel.MaxTargetPower;
            fuelAmount = _generator.GetFuel(ent.Owner);
        }

        _ui.SetUiState(ent.Owner, BiogeneratorConsoleUiKey.Key, new BiogeneratorConsoleState(
            _machines.IsOperational(ent.Owner),
            on,
            fuelAmount,
            targetPower,
            maxTargetPower,
            ent.Comp.Dirtiness));
    }

    private void UpdateConsoles(float frameTime)
    {
        _consoleAccum += frameTime;
        if (_consoleAccum < ConsoleRefresh)
            return;
        _consoleAccum = 0f;

        var query = EntityQueryEnumerator<BiogeneratorComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var bio, out var ui))
        {
            if (_ui.IsUiOpen((uid, ui), BiogeneratorConsoleUiKey.Key))
                PushConsoleState((uid, bio));
        }
    }
}
