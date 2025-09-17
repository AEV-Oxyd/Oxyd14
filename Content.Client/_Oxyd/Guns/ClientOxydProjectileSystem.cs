using Content.Client.Projectiles;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed class ClientOxydProjectileSystem : SharedOxydProjectileSystem
{
    [Dependency] private readonly AnimationPlayerSystem _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public Dictionary<int, Entity<OxydProjectileComponent>> predictedProjectiles = new Dictionary<int, Entity<OxydProjectileComponent>>();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydProjectileComponent, UpdateIsPredictedEvent>(OnUpdatePred);
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve);
        UpdatesBefore.Add(typeof(TransformSystem));
    }

    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent ev)
    {
        var query = EntityQueryEnumerator<OxydProjectileComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            predicted.Coordinates = Transform(uid).Coordinates;
        }
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent ev)
    {
        if (_timing.IsFirstTimePredicted)
            return;
        var query = EntityQueryEnumerator<OxydProjectileComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            if (predicted.Coordinates is { } coordinates)
                _transform.SetCoordinates(uid, coordinates);

            predicted.Coordinates = null;
        }
    }

    public void OnUpdatePred(Entity<OxydProjectileComponent> ent, ref UpdateIsPredictedEvent ev)
    {
        ev.IsPredicted = true;
        ev.BlockPrediction = false;
    }
    public override void projectileQueued(Entity<OxydProjectileComponent> projectile)
    {

    }

    public override void processProjectiles(float deltaTime)
    {
        foreach (var projectile in FireNextTick)
        {
            Log.Debug($"Speed is {projectile.Comp.initialMovement}");
            _transform.SetCoordinates(projectile.Owner, projectile.Comp.initialPosition);
            _physics.UpdateIsPredicted(projectile.Owner);
            _physics.SetBodyStatus(projectile.Owner,Comp<PhysicsComponent>(projectile.Owner), BodyStatus.InAir, false);
            _physics.SetLinearVelocity(projectile.Owner, projectile.Comp.initialMovement);
        }
    }
}
