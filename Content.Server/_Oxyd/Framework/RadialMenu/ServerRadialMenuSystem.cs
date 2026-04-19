using Content.Shared._Oxyd.Framework.RadialMenu;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Framework.RadialMenu;

public sealed class ServerRadialMenuSystem : SharedRadialMenuSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<Guid, PendingRequest> _pending = new();
    private readonly Dictionary<ICommonSession, int> _playerRequestCount = new();

    private const int MaxPendingRequestsPerPlayer = 5;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RadialMenuSelectionEvent>(OnSelection);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        Timer += frameTime;
        if (Timer < 60f)
            return;

        Timer = 0f;

        var now = _timing.CurTime;
        var toRemove = new List<Guid>();

        foreach (var (id, request) in _pending)
        {
            if (now - request.CreationTime > DefaultTimeout)
            {
                toRemove.Add(id);
            }
        }

        foreach (var id in toRemove)
        {
            if (_pending.Remove(id, out var request))
            {
                DecrementPlayerCount(request.Player);
            }
        }
    }

    public override void ShowRadial(ICommonSession player, List<RadialMenuOption> options, Action<RadialBaseSelection> callback, EntityUid? target = null)
    {
        var count = _playerRequestCount.GetValueOrDefault(player);
        if (count >= MaxPendingRequestsPerPlayer)
        {
            Log.Warning($"Player {player.Name} reached max pending radial menu requests ({MaxPendingRequestsPerPlayer}). Ignoring new request.");
            return;
        }

        var id = Guid.NewGuid();
        _pending[id] = new PendingRequest(callback, options, _timing.CurTime, player, target);
        _playerRequestCount[player] = count + 1;

        RaiseNetworkEvent(new RadialMenuOpenEvent
        {
            RequestId = id,
            Options = options,
            Target = GetNetEntity(target)
        }, player);
    }

    private void OnSelection(RadialMenuSelectionEvent ev, EntitySessionEventArgs args)
    {
        if (!_pending.Remove(ev.RequestId, out var request))
            return;

        DecrementPlayerCount(request.Player);

        if (ev.SelectedIndex < 0 || ev.SelectedIndex >= request.Options.Count)
        {
            Log.Error($"RadialMenu: client sent out-of-range index {ev.SelectedIndex} for request {ev.RequestId}.");
            return;
        }

        request.Callback(new RadialBaseSelection { Index = ev.SelectedIndex, Options = request.Options });
    }

    private void DecrementPlayerCount(ICommonSession player)
    {
        if (!_playerRequestCount.TryGetValue(player, out var count))
            return;

        if (count <= 1)
            _playerRequestCount.Remove(player);
        else
            _playerRequestCount[player] = count - 1;
    }

    protected override void OpenMenu(Guid requestId, List<RadialMenuOption> options, EntityUid? target = null) { }
}
