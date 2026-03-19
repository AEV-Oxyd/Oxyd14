using Content.Shared._Oxyd.Framework;
using Content.Shared.Flash.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Oxyd.Framework;

public sealed class ServerOxydHelpers : EntitySystem
{
    [Dependency] private readonly IServerNetManager _netManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;


    public List<ICommonSession> lookupPlayerSessions(MapId map, Box2Rotated box)
    {
        List<ICommonSession> found = new();
        box = box.Enlarged(_config.GetCVar(CVars.NetMaxUpdateRange));
        var consts = SharedOxydHelpers.getIntersectionCheckConstants(box);
        Log.Debug($"Checking box with points {box.BottomLeft} and {box.TopRight}, rot {box.Rotation.Degrees}");

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is null)
                continue;
            if (TerminatingOrDeleted(player.AttachedEntity.Value))
                continue;
            var entPos = _transform.GetMapCoordinates(player.AttachedEntity.Value);
            if (entPos.MapId != map)
                continue;
            if (!SharedOxydHelpers.checkIntersect(entPos.Position, box, consts))
                continue;
            found.Add(player);
        }
        return found;
    }

    public List<EntityUid> lookupPlayerEntities(MapId map, Box2Rotated box)
    {
        List<EntityUid> found = new();

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is null)
                continue;
            if (TerminatingOrDeleted(player.AttachedEntity.Value))
                continue;
            var entPos = _transform.GetMapCoordinates(player.AttachedEntity.Value);
            if (entPos.MapId != map)
                continue;
            var consts = SharedOxydHelpers.getIntersectionCheckConstants(box);
            box = box.Enlarged(_config.GetCVar(CVars.NetMaxUpdateRange));
            if (!SharedOxydHelpers.checkIntersect(entPos.Position, box, consts))
                continue;
            found.Add(player.AttachedEntity.Value);
        }
        return found;
    }
}
