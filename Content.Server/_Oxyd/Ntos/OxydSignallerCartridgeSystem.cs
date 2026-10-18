using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._Oxyd.Ntos;
using Content.Shared._Oxyd.Pda;
using Content.Shared.CartridgeLoader;
using Content.Shared.Popups;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Eris signal manager — fires DeviceLink port when signaller HW is installed.</summary>
public sealed partial class OxydSignallerCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydSignallerCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<OxydSignallerCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnReady(EntityUid uid, OxydSignallerCartridgeComponent component, CartridgeUiReadyEvent args)
        => Push(args.Loader);

    private void OnMessage(EntityUid uid, OxydSignallerCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not OxydSignallerUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        string? error = null;
        string? status = null;

        if (msg.Action == OxydSignallerUiAction.Fire)
        {
            if (!TryComp<OxydModularPdaComponent>(loader, out var mod) || !mod.HasSignaller)
                error = "Signaller hardware required";
            else if (!TryComp<SignallerComponent>(loader, out var signaller))
                error = "Signaller not armed";
            else
            {
                _deviceLink.InvokePort(loader, signaller.Port);
                status = "Signal sent";
                _popup.PopupEntity("Remote signal transmitted.", loader, args.Actor);
            }
        }

        Push(loader, error, status);
    }

    private void Push(EntityUid loader, string? error = null, string? status = null)
    {
        var hasHw = TryComp<OxydModularPdaComponent>(loader, out var mod) && mod.HasSignaller;
        var state = new OxydSignallerUiState
        {
            HasHardware = hasHw,
            HasNetwork = true,
            NetworkRange = mod?.EffectiveNetworkRange ?? 0,
            Error = error,
            Status = status,
        };
        _cartridges.UpdateCartridgeUiState(loader, state);
    }
}
