using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Framework;

public class SharedOxydHelpers : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _phys = default!;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SharedTransformSystem));
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public void QueueDel(EntityUid uid)
    {
        if (_netManager.IsClient)
        {
            SetPaused(uid, true);
            _transform.DetachEntity(uid);
        }
        else
            EntityManager.QueueDeleteEntity(uid);
    }
    public static bool checkIntersect(Vector2 p, Box2Rotated b)
    {
        var r = (-b.Rotation).RotateVec(p - b.Box.Center) + b.Box.Center;
        var t = b.Box;
        return r.X > t.Left && r.Y > t.Bottom && r.X < t.Right && r.Y < t.Top;
    }

    public static Box2 buildWorldBox(float x1, float y1, float x2, float y2)
    {
        return new Box2(x1 > x2 ? x2 : x1, y1 > y2 ? y2 : y1, x1 < x2 ? x2 : x1, y1 < y2 ? y2 : y1);
    }
}
