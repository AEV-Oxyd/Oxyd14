using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.TileBorder;

/// <summary>
/// Prototype id contract for server-generated floor-rim decals.
/// Ids are derived from <c>BorderSprites</c> so tiles need no extra YAML fields.
/// </summary>
public static class TileBorderDecals
{
    public const string Tag = "tile-border";
    public const string IdPrefix = "TileBorder-";
    public const int ZIndex = -1;

    public static readonly string[] States =
    [
        TileBorderMask.North,
        TileBorderMask.South,
        TileBorderMask.East,
        TileBorderMask.West,
        TileBorderMask.OutNorthEast,
        TileBorderMask.OutNorthWest,
        TileBorderMask.OutSouthEast,
        TileBorderMask.OutSouthWest,
        TileBorderMask.InNorthEast,
        TileBorderMask.InNorthWest,
        TileBorderMask.InSouthEast,
        TileBorderMask.InSouthWest,
    ];

    public static string RsiStem(ResPath borderSprites)
    {
        return borderSprites.FilenameWithoutExtension;
    }

    public static string PrototypeId(ResPath borderSprites, string state)
    {
        return IdPrefix + RsiStem(borderSprites) + "-" + state;
    }

    public static bool IsGenerated(string prototypeId)
    {
        return prototypeId.StartsWith(IdPrefix, StringComparison.Ordinal);
    }
}
