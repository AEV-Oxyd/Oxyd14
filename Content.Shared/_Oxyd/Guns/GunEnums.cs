using System.Collections.Immutable;
using Robust.Shared.Serialization;
namespace Content.Shared._Oxyd.OxydGunSystem;

// gun visuals for dynamic rendering. Listed in draw order priority (top being lowest , bottom highest)
[Serializable, NetSerializable]
public enum GVis
{
    None, // used for internal atts that dont have any renderin
    MagUnder,
    Gun,
    MagAbove,
    BoltOpen,
    BoltClosed,
    AttStock,
    AttScope,
    AttBarrel,
    AttUnderBarrel,
}

public class GunData
{
    public static readonly ImmutableDictionary<GVis, string> Vis2Str =
        new Dictionary<GVis, string>()
        {
            { GVis.MagUnder, "magunder" },
            { GVis.MagAbove, "magabove" },
            { GVis.BoltOpen, "boltopen" },
            { GVis.BoltClosed, "boltclosed" },
            { GVis.AttStock, "attstock" },
            { GVis.AttScope, "attscope" },
            { GVis.AttBarrel, "attbarrel" },
            { GVis.AttUnderBarrel, "attunderbarrel" },
        }.ToImmutableDictionary();
}

// gun attachment types / slots
[Serializable, NetSerializable]
public enum AttSlot
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
