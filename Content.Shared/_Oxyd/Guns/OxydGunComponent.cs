using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.EntityList;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Shared._Oxyd.OxydGunSystem;


/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydGunComponent : Component
{
    [ViewVariables]
    public bool keepUpdating = false;


    [ViewVariables]
    // Firemodes handle most firing details that are not technical.
    public GunFiremodePrototype selectedFiremodePrototype => InstanciatedFiremodes[selectedFiremodeIndex];
    [ViewVariables]
    public int selectedFiremodeIndex = 0;
    // wheter gun safety is on or not
    [ViewVariables]
    // used for backtracking , none of the actual GunEffects make use of this
    // as they depend on linearity of execution. This is used for late-message recoil &
    // other features if they might get added and depend on past values for catching up
    public GameTick simulateAsTick;
    [ViewVariables]
    public bool safety = true;
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
    // ticks not being fast enough or varying network ping
    [ViewVariables,  AutoNetworkedField]
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

public abstract partial class OxydGunProvidersComponent : Component
{
    public abstract bool getAmmo(int index, [NotNullWhen(true)] out EntityUid? ammo,  out ItemSlot slot);
};

[RegisterComponent]
public partial class OxydGunAmmoChamberComponent : OxydGunProvidersComponent
{
    [DataField("bulletSlot")]
    public List<ItemSlot> bulletSlot = new();
    // actual bullet is pulled from here , bulletSlot is synced to what is in here
    // because ItemSlots fight between server-client , causing client to fire the same bullet multiple times.
    [ViewVariables]
    public List<EntityUid> nextBullet = new List<EntityUid>();

    public override bool getAmmo(int index,[NotNullWhen(true)] out EntityUid? ammo, out ItemSlot slot)
    {
        ammo = nextBullet[index];
        slot = bulletSlot[index];
        return nextBullet[index] != EntityUid.Invalid;
    }

}
[RegisterComponent]
public sealed partial class OxydGunAmmoMagazineChamberComponent : OxydGunAmmoChamberComponent
{
    [DataField("magazineSlot")]
    public List<ItemSlot> magazineSlot = new();
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

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydMagazineComponent : Component
{
    [DataField("capacity"), AutoNetworkedField]
    public int maxBullets = 1;
    [ViewVariables, AutoNetworkedField]
    public Stack<NetEntity> loadedBullets;

    public OxydMagazineComponent()
    {
        loadedBullets = new(maxBullets);
    }
}

[RegisterComponent]
public sealed partial class OxydMagazineInitializerComponent : Component
{
    [DataField]
    public ProtoId<EntityListPrototype> initialBullets;
}

