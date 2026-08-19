using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
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

    public TimeSpan lastNetMouseUpdate = TimeSpan.Zero;
    public bool mouseDown = false;
    // how many states are we expecting/waiting to receive from the client?
    public int stateCounter = 0;

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
public partial class OxydChamberComponent : OxydGunProvidersComponent
{

    [DataField("bulletSlot")]
    public List<ItemSlot> bulletSlot = new();
    // actual bullet is pulled from here , bulletSlot is synced to what is in here
    // because ItemSlots fight between server-client , causing client to fire the same bullet multiple times.
    [ViewVariables, AutoNetworkedField]
    public List<EntityUid> realBullet = new List<EntityUid>();
    [ViewVariables, AutoNetworkedField]
    public bool silenceAutoInsert = false;


}
[RegisterComponent,NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydMagazineChamberComponent : OxydChamberComponent
{
    [DataField("magazineSlot"), CheckForGunUpdate(true)]
    public List<ItemSlot> magazineSlot = new();

    [DataField]
    // wheter to draw mags above or below gun layer
    public bool magAbove = false;

    [DataField]
    public bool MagInhands = false;
}

[RegisterComponent,NetworkedComponent]
public sealed partial class OxydPredictedGunStorageComponent : OxydGunProvidersComponent
{
    [DataField] public List<string> storeKeys = new() {"base"};

}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class OxydRevolvingChamberComponent : OxydGunProvidersComponent
{
    // used to define what kind of bullets will fit in the gun . Won't actually be initialized!
    [DataField("bulletSlot")]
    public ItemSlot bulletSlot = new();
    public override bool SessionSpecific => true;
    [NetSerializable, Serializable, DataDefinition]
    public sealed partial class RevolvingData
    {
        [ViewVariables]
        public int index = 0;
        [DataField]
        public int count
        {
            get;
            set
            {
                loaded = new NetEntity[value];
                field = value;
            }
        }

        [ViewVariables]
        public NetEntity[] loaded;

        public void increment()
        {
            index = (index + 1) % count;
        }
        // gets the next position
        public int getIncrement()
        {
            return (index + 1) % count;
        }

        public int getIncrement(int i)
        {
            return (i+1) % count;
        }
        // returns -1 for no slots open
        public int getFreeSpot()
        {
            if (seek() == NetEntity.Invalid)
                return index;
            int starting = index;
            int cur = getIncrement(starting);
            while (cur != starting)
            {
                if (loaded[cur] == NetEntity.Invalid)
                    return cur;
                cur = getIncrement(cur);
            }

            return -1;
        }

        public NetEntity get()
        {
            var tng = loaded[index];
            increment();
            return tng;
        }

        public NetEntity seek()
        {
            return loaded[index];
        }
    }
    [CheckForGunUpdate(true), DataField, AutoNetworkedField]
    public List<RevolvingData> revolvingSlots;
}
// acts as a buffer between magazines / loading if present
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class OxydChamberExtensionComponent : Component
{
    public override bool SessionSpecific => true;
    [ViewVariables, AutoNetworkedField]
    // will be null for every firemode index present unless set
    //  array length defines how many extra bullet slots are given
    public List<List<NetEntity>?> extending = new();
}

[Serializable, NetSerializable]
public sealed class OxydChamberExtensionComponentState : ComponentState
{
    public readonly List<List<NetEntity>?> Extending;

    public OxydChamberExtensionComponentState(List<List<NetEntity>?> extending)
    {
        Extending = extending;
    }
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
