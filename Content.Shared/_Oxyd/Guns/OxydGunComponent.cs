using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.EntityList;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Shared._Oxyd.OxydGunSystem;
[Serializable, NetSerializable]
public sealed partial class UnjamGunEvent : SimpleDoAfterEvent
{
}
/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydGunComponent : Component
{
    public override bool SessionSpecific => true;

    [ViewVariables]
    public bool keepUpdating = false;


    [ViewVariables]
    // Firemodes handle most firing details that are not technical.
    public GunFiremodePrototype selectedFiremodePrototype => InstanciatedFiremodes[selectedFiremodeIndex];
    [ViewVariables, AutoNetworkedField]
    public int selectedFiremodeIndex = 0;
    // wheter gun safety is on or not
    [ViewVariables]
    // used for backtracking , none of the actual GunEffects make use of this
    // as they depend on linearity of execution. This is used for late-message recoil &
    // other features if they might get added and depend on past values for catching up
    public GameTick simulateAsTick;
    [ViewVariables, AutoNetworkedField]
    public bool safety = true;

    [DataField, AutoNetworkedField]
    public bool hasSafety = true;

    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool jammed = false;
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
    public override bool SessionSpecific => true;
};

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public partial class OxydGunAmmoChamberComponent : OxydGunProvidersComponent
{

    [DataField("bulletSlot")]
    public List<ItemSlot> bulletSlot = new();
    // actual bullet is pulled from here , bulletSlot is synced to what is in here
    // because ItemSlots fight between server-client , causing client to fire the same bullet multiple times.
    [ViewVariables, AutoNetworkedField]
    public List<EntityUid> nextBullet = new List<EntityUid>();


}
[RegisterComponent,NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydGunAmmoMagazineChamberComponent : OxydGunAmmoChamberComponent
{
    [DataField("magazineSlot"), CheckForGunUpdate(true)]
    public List<ItemSlot> magazineSlot = new();
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class LaserAmmoDef
{
    [DataField]
    public EntProtoId laser = default!;

    [DataField]
    public float cost = default!;

}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public partial class OxydGunLaserProviderComponent : OxydGunProvidersComponent
{


    [DataField("laserProto"), AutoNetworkedField]
    public List<LaserAmmoDef> laserProto = new();


}

[RegisterComponent]
public sealed partial class OxydBulletComponent : Component
{
    // meters per second.
    [DataField]
    public float Speed = 100;
    [DataField]
    public EntProtoId projectileEntity = default!;
    [DataField]
    public EntProtoId casingEntity = default!;
}

[RegisterComponent]
public sealed partial class OxydHitscanProjectileComponent : Component
{
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydMagazineComponent : OxydGunProvidersComponent
{
    public override bool SessionSpecific => true;

    [DataField("capacity"), AutoNetworkedField]
    public int maxBullets = 1;

    [ViewVariables, AutoNetworkedField]
    public Stack<NetEntity> loadedBullets;

    public OxydMagazineComponent()
    {
        loadedBullets = new(maxBullets);
    }
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydChargeComponent : OxydGunProvidersComponent
{
    public override bool SessionSpecific => true;
    [DataField, AutoNetworkedField]
    public float charge = 0;
}
[RegisterComponent]
public sealed partial class OxydMagazineInitializerComponent : Component
{
    [DataField]
    public ProtoId<EntityListPrototype> initialBullets;
}

