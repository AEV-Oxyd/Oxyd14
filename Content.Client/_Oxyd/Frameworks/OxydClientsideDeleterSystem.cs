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
public sealed class OxydClientsideDeleterSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly Robust.Client.Physics.PhysicsSystem _physics = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<DeleteClientsideComponent, ComponentInit>(Purge);
    }

    public void Purge(Entity<DeleteClientsideComponent> obj,ref ComponentInit componentAdd)
    {
        if (_playerManager.LocalSession is null)
            return;
        if (obj.Comp.forSessions.Contains(_playerManager.LocalSession.Name))
        {
            RemComp<SpriteComponent>(obj);
        }
    }
}
