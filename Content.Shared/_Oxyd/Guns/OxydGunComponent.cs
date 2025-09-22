using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;


namespace Content.Shared._Oxyd.OxydGunSystem;


/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class OxydGunComponent : Component
{
    // bullet sets their own speed , gun can only influence it
    [DataField]
    public float SpeedMultiplier = 1;

    // bullets per second
    [DataField]
    public float FireRate = 1000;
    public int shootingPosIndex = 0;
    [DataField]
    public List<Vector2> shootingPosOffsets = new List<Vector2>();
    public OxydGunProviderComponent ammoProvider = default!;
    [DataField]
    public TimeSpan nextFire = TimeSpan.Zero;
    [ViewVariables]
    public TimeSpan fireDelay => TimeSpan.FromSeconds(1/FireRate);
    // how many "extra" bullets have we accumulated due to
    // firerate being faster than server tick rate
    // will be used to spawn multiple bullets in a tick when
    // falling behind. Triggers when shootFraction > fireDelay
    [ViewVariables]
    public TimeSpan shootFraction = TimeSpan.Zero;

    public Vector2 getShootingOffset()
    {
        if (shootingPosIndex == shootingPosOffsets.Count)
            shootingPosIndex = 0;
        return shootingPosOffsets[shootingPosIndex++];
    }
};
[RegisterComponent]
public sealed partial class OxydHandheldGunComponent : Component
{

}

public abstract partial class OxydGunProviderComponent : Component
{
    public abstract bool getAmmo([NotNullWhen(true)] out EntityUid? ammo,  out ItemSlot slot);
};

[RegisterComponent]
public partial class OxydGunAmmoChamberComponent : OxydGunProviderComponent
{
    [DataField("bulletSlot")]
    public ItemSlot bulletSlot = new();

    public override bool getAmmo([NotNullWhen(true)] out EntityUid? ammo, out ItemSlot slot)
    {
        ammo = bulletSlot.Item;
        slot = bulletSlot;
        return bulletSlot.HasItem;
    }
}
[RegisterComponent]
public sealed partial class OxydGunAmmoMagazineChamberComponent : OxydGunProviderComponent
{
    [DataField("bulletSlot")]
    public ItemSlot magazineSlot = new();
    public override bool getAmmo([NotNullWhen(true)] out EntityUid? ammo,  out ItemSlot slot)
    {
        ammo = magazineSlot.Item;
        slot = magazineSlot;
        return magazineSlot.HasItem;
    }
}

[RegisterComponent]
public sealed partial class OxydBulletComponent : Component
{
    // meters per second.
    [DataField]
    public float Speed = 100;
    [DataField]
    public EntProtoId projectileEntity = default!;
}

[RegisterComponent]
public sealed partial class OxydMagazineComponent : Component
{
    public ItemSlot topBulletSlot = new();
    public Queue<EntityUid> loadedBullets = new();
}

