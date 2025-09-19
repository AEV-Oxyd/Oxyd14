using Robust.Server.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Server._Oxyd.Framework;

/// <summary>
/// This handles...
/// </summary>
public sealed class PvsSessionBlockerSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _PlayerManager = default!;
    [Dependency] private readonly INetManager NetManager = default!;
    // name -> pvs blacklist key
    private Dictionary<string, int> playerKeys = new();
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<PvsSessionBlockerComponent, ComponentGetStateAttemptEvent>(OnTrySendPvs);
        NetManager.Connected += OnPlayerConnect;
    }
    private void OnPlayerConnect(object? sender, NetChannelArgs e)
    {
        if(e.Channel.UserName)
    }

    public void OnTrySendPvs(Entity<PvsSessionBlockerComponent> obj, ref ComponentGetStateAttemptEvent ev)
    {
        if (ev.Player is null)
            return;
        if(obj.Comp.blacklistedFor.Contains(ev.Player.Name))
            ev.Cancelled = true;
    }
}
