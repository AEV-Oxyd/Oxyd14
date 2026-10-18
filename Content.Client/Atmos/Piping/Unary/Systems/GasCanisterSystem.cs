using Content.Client.Atmos.UI;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Piping.Unary.Systems;
using Content.Shared.NodeContainer;

namespace Content.Client.Atmos.Piping.Unary.Systems;

public sealed partial class GasCanisterSystem : SharedGasCanisterSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GasCanisterComponent, AfterAutoHandleStateEvent>(OnGasState);
    }

    protected override void DeviceUpdated(Entity<GasCanisterComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        // Atmos not predicted :(
        throw new NotImplementedException();
    }

    private void OnGasState(Entity<GasCanisterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // BoundUserInterface so _Oxyd reskins of this window don't crash the cast.
        if (UI.TryGetOpenUi<BoundUserInterface>(ent.Owner, GasCanisterUiKey.Key, out var bui))
        {
            bui.Update<GasCanisterBoundUserInterfaceState>();
        }
    }

    protected override void DirtyUI(EntityUid uid, GasCanisterComponent? component = null, NodeContainerComponent? nodes = null)
    {
        // BoundUserInterface so _Oxyd reskins of this window don't crash the cast.
        if (UI.TryGetOpenUi<BoundUserInterface>(uid, GasCanisterUiKey.Key, out var bui))
        {
            bui.Update<GasCanisterBoundUserInterfaceState>();
        }
    }
}
