using System.Linq;
using System.Numerics;
using Content.Shared._Oxyd.Framework;
using Content.Shared._Oxyd.Predictors;
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
    [Dependency] private readonly BasicPhysicsPredictorSystem _predictor = default!;

    public Angle GetEffectiveWorldRotation(EntityUid uid)
    {
        var worldRot = _transformSystem.GetWorldRotation(uid);
        var eyeRot = _eye.CurrentEye.Rotation;
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return worldRot;
        if (sprite.NoRotation)
            return -eyeRot;
        if (sprite.SnapCardinals)
        {
            var angle = worldRot + eyeRot;
            var cardinal = angle.RoundToCardinalAngle();
            return worldRot - cardinal;
        }
        return worldRot;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var qery = EntityQueryEnumerator<ApplyVisualOffsetComponent>();
        while (qery.MoveNext(out var uid, out var comp))
        {
            if (_ignore.shouldIgnore(uid))
                continue;
            var applying = GetEffectiveWorldRotation(uid);
            if (HasComp<BasicPredictorOffsetSetterComponent>(uid))
            {
                comp.offset = _predictor.PredictWorldPosition(uid, 7);
            }
            _sprite.SetOffset((uid, null), (-applying).RotateVec(comp.offset));
        }
    }
}
