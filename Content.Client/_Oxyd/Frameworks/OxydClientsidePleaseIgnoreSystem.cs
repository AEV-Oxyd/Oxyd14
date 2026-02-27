using System.Linq;
using Content.Shared._Oxyd.Framework;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Client._Oxyd.Framework;

/// <summary>
/// This handles...
/// </summary>
public sealed class OxydClientsidePleaseIgnoreSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    private EntityQuery<ClientsidePleaseIgnoreComponent> ignore;
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ClientsidePleaseIgnoreComponent, ComponentInit>(Purge);
        ignore = GetEntityQuery<ClientsidePleaseIgnoreComponent>();
    }

    public void Purge(Entity<ClientsidePleaseIgnoreComponent> obj,ref ComponentInit componentAdd)
    {
        if (_playerManager.LocalSession is null)
            return;
        if (obj.Comp.forSessions.Contains(_playerManager.LocalSession.Name))
        {
            RemComp<SpriteComponent>(obj);
        }

    }

    public bool shouldIgnore(EntityUid uid)
    {
        if (_playerManager.LocalSession is null)
            return true;

        if (ignore.TryGetComponent(uid, out var comp) &&  comp.forSessions.Contains(_playerManager.LocalSession.Name))
        {
            return true;
        }

        return false;
    }
}
