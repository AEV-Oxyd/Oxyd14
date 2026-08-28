using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

[Flags, Serializable, NetSerializable]
public enum COM // Chamber Operating Mode
{
    None = 0, // No special operation mode
    Boltable = 1<<0, // is a bolting weapon.
    BoltLoad = 1<<1, // will load on bolt
    BoltUnload = 1<<2, // will remove casing on unbolt
    BoltClosedAutoload = 1<<3, // will keep auto-loading if bolt is closed
    Pumpable = 1<<4, // will chamber on pump
    PumpableLoad= 1<<5, // will add a casing if chamber's empty
    PumpableUnload = 1<<6, // will remove a casing on pump
    Auto = 1<<7, // always loads after every fire/insertion
}


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

    [ViewVariables, AutoNetworkedField] public Dictionary<string, uint> originalCapacityCounts = new();

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
public sealed partial class OxydHandheldGunComponent : Component;

[Serializable, NetSerializable]
public sealed class OxydProviderState<T> : ComponentState
{
    public Dictionary<string, T> data = new();
}

public abstract partial class BaseGunProvider : Component
{
    public abstract List<string> getKeys();
}
public abstract partial class OxydGunProvidersComponent<T> : BaseGunProvider
{
    [DataField] public Dictionary<string, T> providers = new();

    public override List<string> getKeys() => providers.Keys.ToList();

    public OxydProviderState<T> ComponentGetState()
    {
        return new OxydProviderState<T> { data = providers };
    }

    public void ComponentApplyState(OxydProviderState<T> state)
    {
        providers = state.data;
    }
};
[Serializable, NetSerializable, DataDefinition]
public partial class ChamberData
{
    [ViewVariables] public string store = string.Empty;
    [DataField] public COM operatingMode = COM.None;
    [ViewVariables] public bool state = false; // used by bolties
    [DataField] public bool pushback = false; // Default is Queue-based, if true, Stack-based
    [DataField] public int capacity = 1;
    [DataField] public int basecapacity = 1;
    [DataField] public EntityWhitelist whitelist = new();
}

[Serializable, NetSerializable, DataDefinition]
public partial class MagazineData : ChamberData
{
    [ViewVariables] public string magstore = string.Empty;
    [DataField] public EntityWhitelist magwhitelist = new();
}

[RegisterComponent, NetworkedComponent]
public partial class OxydChamberComponent : OxydGunProvidersComponent<ChamberData>;

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydMagazineChamberComponent : OxydGunProvidersComponent<MagazineData>
{
    [DataField]
    // wheter to draw mags above or below gun layer
    public bool magAbove = false;
    [DataField]
    public bool MagInhands = false;
}

[Serializable, NetSerializable, DataDefinition]
public partial class RevolverData
{
    [ViewVariables] public string store = string.Empty;
    // array for keeping track of actually loaded bullets.
    [ViewVariables] public EntityUid[] loaded = Array.Empty<EntityUid>();
    [DataField] public int index = 0;
    [DataField] public int capacity = 0;
    [DataField] public int basecapacity = 1;
    [DataField] public EntityWhitelist whitelist = new();
}

[RegisterComponent, NetworkedComponent]
public sealed partial class OxydRevolvingChamberComponent : OxydGunProvidersComponent<RevolverData>;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class LaserData
{
    [DataField]
    public EntProtoId laser = default!;

    [DataField]
    public float cost = default!;

}

[RegisterComponent, NetworkedComponent]
public partial class OxydGunLaserProviderComponent : OxydGunProvidersComponent<LaserData>;

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
public sealed partial class OxydHitscanProjectileComponent : Component;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydMagazineComponent : Component
{
    [DataField] public string storeKey = string.Empty;
    [DataField("capacity"), AutoNetworkedField] public uint maxBullets = 1;
    [DataField] public EntityWhitelist whitelist = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydChargeComponent : Component
{
    [DataField, AutoNetworkedField]
    public float charge = 0;
}
