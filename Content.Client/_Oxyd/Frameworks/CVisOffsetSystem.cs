using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.Framework;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Framework;

/// <summary>
/// This handles...
/// </summary>
public sealed class CVisOffsetSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly OxydClientsidePleaseIgnoreSystem _ignore = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var qery = EntityQueryEnumerator<ApplyVisualOffsetComponent>();
        _eye.CurrentEye.GetViewMatrix(out var viewMat, Vector2.One);
        while (qery.MoveNext(out var uid, out var comp))
        {
            //if (_ignore.shouldIgnore(uid))
            //    continue;
            //_sprite.SetOffset((uid, null), viewMat.Rotation().RotateVec(comp.offset));
            _sprite.SetOffset((uid, null), -_transformSystem.GetWorldRotation(uid).RotateVec(comp.offset));
        }
    }
}
