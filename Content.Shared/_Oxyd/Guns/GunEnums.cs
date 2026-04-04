using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.OxydGunSystem;

// gun visuals for dynamic rendering
[Serializable, NetSerializable]
public enum GVis
{
    MagUnder,
    MagAbove,
    BoltOpen,
    BoltClosed,
    AmmoIndicator,
    AttStock,
    AttScope,
    AttBarrel,
    AttUnderBarrel,
}

// gun attachment types / slots
public enum GAtt
{
    Barrel,
    UnderBarrel,
    Scope,
    Stock,
    Chamber,
    Internal,
    Internal2,
    Internal3

}
