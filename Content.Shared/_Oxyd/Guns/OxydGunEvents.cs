using System.IO;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public sealed class ClientSideInterpretingFiremode : EntityEventArgs
{
    public NetEntity gun;
    public int clientsideStartingStep = 0;
    public GameTick clientTick;
    public bool mouseHeld = false;
}

[NetSerializable, Serializable]
public sealed class SetGunChargeEvent : EntityEventArgs
{
    public float charge = 0f;
    public NetEntity gun = NetEntity.Invalid;
}

[Serializable, NetSerializable]
public sealed class FiremodeMouseStatus : EntityEventArgs
{
    public bool held = false;
    public NetEntity gun;
    public GameTick clientTick;
    public int fromStep;
}

[Serializable, NetSerializable]
public sealed class FiremodeClientsideFiredEvent : EntityEventArgs
{
    public NetCoordinates aimedPosition;
    public NetCoordinates shotFrom;
    public int firemodeStep = 0;
    public NetEntity gun;
    public GameTick clientTick;
}

[Serializable, NetSerializable]
public sealed class ClientSideDoneInterpretingFiremode : EntityEventArgs
{
    public NetEntity gun;
    public int stoppedAt = 0;
    public GameTick clientTick;
    public bool mouseHeld = false;
}

public sealed class FiremodeProjectilesFiredEvent : EntityEventArgs
{
    public Entity<OxydGunComponent> gun;
    public required HashSet<Entity<OxydProjectileComponent>> projectiles;
    public EntityUid shooter = EntityUid.Invalid;
}
[Serializable, NetSerializable]
public sealed class FiremodeChangedEvent : EntityEventArgs
{
    public required NetEntity gun;
    public required NetEntity switcher;
    public required int index;
}

[Serializable, NetSerializable]
public sealed class GunSafetyChangedEvent : EntityEventArgs
{
    public required NetEntity gun;
    public required NetEntity switcher;
    public required bool newState;
}

public sealed  class GunFiredEvent : EntityEventArgs
{
    public HashSet<Entity<OxydProjectileComponent>> projectiles = new();

    public GameTick simTick;
}

public sealed class GunBeforeFireIndividualProjectileEvent : EntityEventArgs
{
    public Entity<OxydProjectileComponent> projectile;
    public GameTick simTick;
}

public sealed class GunAfterFireIndividualProjectileEvent : EntityEventArgs
{
    public Entity<OxydProjectileComponent> projectile;
    public GameTick simTick;
}


public sealed class GunGetInaccuracyEvent : EntityEventArgs
{
    public required Angle baseInaccuracy;
    public required Angle addedInaccuracy;
    public required GameTick simTick;
}
[Serializable, NetSerializable]
public sealed class GunCompareFired : EntityEventArgs
{
    public int firedCount;
    public NetEntity target;
}

[ByRefEvent]
public struct GunFireResultEvent
{
    public string providerId;
    public bool fired = false;
    
    public GunFireResultEvent(string id, bool succes)
    {
        this.providerId = id;
        fired = succes;
    }
}

[ByRefEvent]
public struct GunFailedFireEvent
{
    public string providerId;
    
    public GunFailedFireEvent(string id)
    {
        this.providerId = id;
    }
}

[ByRefEvent]
public struct GunHasAmmoEvent
{
    public string providerId;
    public bool hasAmmo;
    
    public GunHasAmmoEvent(string id)
    {
        this.providerId = id;
        hasAmmo = false;
    }
}
[ByRefEvent]
public struct GunTryLoadAmmoEvent
{
    public EntityUid ammo;
    public bool handled;
    public bool prediction;
    
    public GunTryLoadAmmoEvent(EntityUid ammo, bool pred)
    {
        this.ammo = ammo;
        handled = false;
        prediction = pred;
    }
}
[ByRefEvent]
public struct GunTryGetAmmoEvent
{
    public string providerId;
    public EntityUid ammo;
    public EntProtoId projectile;
    public bool prediction;

    public GunTryGetAmmoEvent(string id, bool prediction = false)
    {
        providerId = id;
        ammo = EntityUid.Invalid;
        projectile = default;
        this.prediction = prediction;
    }
}
/// <summary>
///  ammo/projectile can be null/ invalid
/// </summary>
[ByRefEvent]
public struct GunAfterUseAmmoEvent
{
    public string providerId;
    public EntityUid ammo;
    public EntityUid projectile;

    public GunAfterUseAmmoEvent(string id, EntityUid ammo, EntityUid projectile)
    {
        providerId = id;
        this.ammo = ammo;
        this.projectile = projectile;
    }
    
}
