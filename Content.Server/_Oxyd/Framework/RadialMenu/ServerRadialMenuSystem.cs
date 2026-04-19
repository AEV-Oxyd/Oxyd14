using Content.Shared._Oxyd.Framework.RadialMenu;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server._Oxyd.Framework.RadialMenu;

public sealed class ServerRadialMenuSystem : EntitySystem
{
    private readonly record struct PendingRequest(
        Action<RadialBaseSelection> Callback,
        List<NetEntity> Options,
        bool ExpectsItem
    );

    private readonly Dictionary<Guid, PendingRequest> _pending = new();

    public override void Initialize()
    {
        SubscribeNetworkEvent<RadialMenuSelectionEvent>(OnSelection);
    }

    /// <summary>
    /// Show a radial menu to <paramref name="player"/> with entity icons.
    /// The callback receives a <see cref="RadialItemSelection"/> with both index and entity.
    /// </summary>
    public void ShowRadial(ICommonSession player, List<NetEntity> options, Action<RadialItemSelection> callback)
    {
        var id = Guid.NewGuid();
        _pending[id] = new PendingRequest(
            sel =>
            {
                if (sel is not RadialItemSelection item)
                {
                    Log.Error($"RadialMenu: expected RadialItemSelection but got {sel.GetType().Name} for request {id}. Dropping callback.");
                    return;
                }
                callback(item);
            },
            options,
            ExpectsItem: true
        );
        RaiseNetworkEvent(new RadialMenuOpenEvent { RequestId = id, Options = options }, player);
    }

    /// <summary>
    /// Show a radial menu to <paramref name="player"/> with entity icons.
    /// The callback receives a <see cref="RadialBaseSelection"/> with the chosen index.
    /// </summary>
    public void ShowRadial(ICommonSession player, List<NetEntity> options, Action<RadialBaseSelection> callback)
    {
        var id = Guid.NewGuid();
        _pending[id] = new PendingRequest(callback, options, ExpectsItem: false);
        RaiseNetworkEvent(new RadialMenuOpenEvent { RequestId = id, Options = options }, player);
    }

    private void OnSelection(RadialMenuSelectionEvent ev, EntitySessionEventArgs args)
    {
        if (!_pending.TryGetValue(ev.RequestId, out var request))
            return;

        _pending.Remove(ev.RequestId);

        if (ev.SelectedIndex < 0 || ev.SelectedIndex >= request.Options.Count)
        {
            Log.Error($"RadialMenu: client sent out-of-range index {ev.SelectedIndex} for request {ev.RequestId}.");
            return;
        }

        RadialBaseSelection selection = request.ExpectsItem
            ? new RadialItemSelection { Index = ev.SelectedIndex, Entity = request.Options[ev.SelectedIndex] }
            : new RadialBaseSelection { Index = ev.SelectedIndex };

        request.Callback(selection);
    }
}
