using Content.Shared._Oxyd.OxydGunSystem;

namespace Content.Client._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
public partial class ClientOxydGunSystem
{

    public void muzzleEffect(Entity<OxydGunComponent> ent, ref GunAfterFireIndividualProjectileEvent args)
    {
        var effectProto = SpawnAtPosition("MuzzleFlashEffect", Transform(args.projectile).Coordinates);
        _transformSystem.SetLocalRotation(effectProto,Transform(args.projectile.Owner).LocalRotation);
    }
}
