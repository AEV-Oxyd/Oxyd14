using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;


namespace Content.Shared._Oxyd.OxydGunSystem;


/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydGunComponent : Component
{
    // bullet sets their own speed , gun can only influence it
    [DataField]
    public float SpeedMultiplier = 1;

    public OxydGunProviderComponent ammoProvider = default!;

    [ViewVariables]
    // Firemodes handle most firing details that are not technical.
    public GunFiremodePrototype selectedFiremodePrototype;
    [ViewVariables]
    public List<GunFiremodePrototype> InstanciatedFiremodes = new();
    [DataField]
    public List<ProtoId<GunFiremodePrototype>> firemodes = new();
    [ViewVariables, AutoNetworkedField]
    // used for randomization
    public uint timesFired = 0;
    // How much actual firing time there is
    // This depends on server tick period. A gun can accumulate
    // extra firing time due to uneven ticks , this makes sure the
    // firarate is always overall respected , even if it'd be lost due to
    // ticks not being fast enough
    [ViewVariables]
    public TimeSpan firingTime = TimeSpan.Zero;

    public Vector2 getShootingOffset()
    {
        if (selectedFiremodePrototype.shootingPosIndex == selectedFiremodePrototype.shootingPosOffsets.Count)
            selectedFiremodePrototype.shootingPosIndex = 0;
        return selectedFiremodePrototype.shootingPosOffsets[selectedFiremodePrototype.shootingPosIndex++];
    }
};
[RegisterComponent]
public sealed partial class OxydActiveFiremodeUpdatingComponent : Component
{
    public GunFiremodePrototype FiremodePrototype;
    public Entity<OxydGunComponent> gun;
    public EntityUid? shooter;
}
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

