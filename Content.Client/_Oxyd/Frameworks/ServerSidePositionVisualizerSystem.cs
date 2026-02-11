using System.Linq;
using System.Numerics;
using Robust.Client.Debugging;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Framework;
[RegisterComponent]
public partial class TellMePosComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int ticksFoward = 7;
}
/// <summary>
/// This handles...
/// </summary>
public sealed class ServerSidePositionVisualizerSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IMapManager _map = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        _overlay.AddOverlay(new ShitDebugOverlay(EntityManager, _resourceCache, _physics, _timing, _transformSystem, _sprite, _eye));
    }

    internal sealed class ShitDebugOverlay : Overlay
    {
        private readonly IEntityManager _entityManager;
        private readonly IGameTiming _gameTiming;
        private readonly SharedTransformSystem _transformSystem = default!;
        private readonly SharedPhysicsSystem _physicsSystem;
        private readonly SpriteSystem _sprite = default!;
        private readonly IEyeManager _eye = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

        private static readonly Color JointColor = new(0.5f, 0.8f, 0.8f);

        private readonly Font _font;

        public ShitDebugOverlay(IEntityManager entityManager, IResourceCache cache,
            SharedPhysicsSystem physicsSystem, IGameTiming timing, SharedTransformSystem tsf, SpriteSystem sprt, IEyeManager eye)
        {
            _entityManager = entityManager;
            _gameTiming = timing;
            _physicsSystem = physicsSystem;
            _transformSystem = tsf;
            _sprite = sprt;
            _eye = eye;
            _font = new VectorFont(cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 10);
        }

        public void DrawWorld(DrawingHandleWorld worldHandle, OverlayDrawArgs args)
        {
            var viewBounds = args.WorldBounds;
            var viewAABB = args.WorldAABB;
            var mapId = args.MapId;
            var firstList = _physicsSystem.GetCollidingEntities(mapId, viewBounds).ToList();
            var secondList = new List<Entity<PhysicsComponent>>(firstList.Count);
            var entq = _entityManager.GetEntityQuery<TellMePosComponent>();
            var entq2 = _entityManager.GetEntityQuery<TransformComponent>();
            foreach (var elem in firstList)
            {
                if (entq.HasComp(elem))
                    secondList.Add(elem);
            }

            foreach (var ent in secondList)
            {
                var comp = entq.GetComponent(ent);
                var xform = _physicsSystem.GetPhysicsTransform(ent.Owner);

                const float AlphaModifier = 0.8f;
                _eye.GetScreenProjectionMatrix(out var eyeMat);
                var offset = comp.ticksFoward * (float)_gameTiming.TickPeriod.TotalSeconds * ent.Comp.LinearVelocity;
                foreach(var a in _entityManager.GetComponent<FixturesComponent>(ent).Fixtures.Values)
                    DrawShape(worldHandle, a,  entq2.GetComponent(ent), new Color(0.5f, 0.5f, 0.3f).WithAlpha(AlphaModifier), offset );
                var grid = _transformSystem.GetGrid(ent.Owner);
                var rotVec = _transformSystem.GetWorldRotation(ent.Owner);
                if (grid is not null)
                {
                    rotVec = -eyeMat.Rotation() + _entityManager.GetComponent<TransformComponent>(grid.Value).LocalRotation.Reduced();
                }
                _sprite.SetOffset((ent.Owner, null), -eyeMat.Rotation().Reduced().RotateVec(offset));
            }
            worldHandle.UseShader(null);
            worldHandle.SetTransform(Matrix3x2.Identity);


        }


        protected override void Draw(in OverlayDrawArgs args)
        {
            switch (args.Space)
            {
                case OverlaySpace.ScreenSpace:
                    DrawScreen(args);
                    break;
                case OverlaySpace.WorldSpace:
                    DrawWorld((DrawingHandleWorld)args.DrawingHandle, args);
                    break;
            }
        }

        public void DrawScreen(OverlayDrawArgs args)
        {
            var q = _entityManager.EntityQueryEnumerator<TellMePosComponent>();
            while(q.MoveNext(out var uid , out var comp))
            {
                args.ScreenHandle.DrawString(_font,
                    _eye.WorldToScreen(_transformSystem.GetWorldPosition(uid)),
                    comp.ticksFoward.ToString());
            }
        }

        public void DrawShape(DrawingHandleWorld worldHandle, Fixture fixture, TransformComponent xform, Color color, Vector2 Offset)
        {
            switch (fixture.Shape)
            {
                case PhysShapeCircle circle:
                    var center = _transformSystem.GetWorldPosition(xform) + Offset;
                    worldHandle.DrawCircle(center, circle.Radius, color);
                    break;

                default:
                    return;
            }
        }
    }
}
