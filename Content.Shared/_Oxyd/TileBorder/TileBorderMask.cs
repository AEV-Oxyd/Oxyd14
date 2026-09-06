using Content.Shared.Maps;

namespace Content.Shared._Oxyd.TileBorder;

/// <summary>
/// 8-neighbour rim selection for tiles that set <see cref="ContentTileDefinition.BorderSprites"/>.
/// Bit i is set when the neighbour in <see cref="Direction"/> i shares the origin group.
/// </summary>
public static class TileBorderMask
{
    public const byte InteriorMask = 0xFF;

    public const string North = "n";
    public const string South = "s";
    public const string East = "e";
    public const string West = "w";
    public const string OutNorthEast = "out-ne";
    public const string OutNorthWest = "out-nw";
    public const string OutSouthEast = "out-se";
    public const string OutSouthWest = "out-sw";
    public const string InNorthEast = "in-ne";
    public const string InNorthWest = "in-nw";
    public const string InSouthEast = "in-se";
    public const string InSouthWest = "in-sw";

    public static bool IsInterior(byte mask) => mask == InteriorMask;

    public static string ResolveGroup(string id, string? borderGroup)
    {
        return string.IsNullOrEmpty(borderGroup) ? id : borderGroup;
    }

    public static string ResolveGroup(ContentTileDefinition def)
    {
        return ResolveGroup(def.ID, def.BorderGroup);
    }

    /// <summary>
    /// <paramref name="groupAt"/> returns the neighbour's group, or null when the tile is empty / has no rim.
    /// </summary>
    public static byte Compute(Vector2i origin, string originGroup, Func<Vector2i, string?> groupAt)
    {
        byte mask = 0;
        foreach (var dir in DirectionExtensions.AllDirections)
        {
            var group = groupAt(origin + dir.ToIntVec());
            if (group == originGroup)
                mask |= (byte) (1 << (int) dir);
        }

        return mask;
    }

    /// <summary>
    /// Appends RSI state names in draw order: cardinals, then outer corners, then inner corners.
    /// </summary>
    public static void AppendLayers(byte mask, List<string> states)
    {
        var n = Linked(mask, Direction.North);
        var s = Linked(mask, Direction.South);
        var e = Linked(mask, Direction.East);
        var w = Linked(mask, Direction.West);
        var ne = Linked(mask, Direction.NorthEast);
        var nw = Linked(mask, Direction.NorthWest);
        var se = Linked(mask, Direction.SouthEast);
        var sw = Linked(mask, Direction.SouthWest);

        if (!n)
            states.Add(North);
        if (!s)
            states.Add(South);
        if (!e)
            states.Add(East);
        if (!w)
            states.Add(West);

        if (!n && !e)
            states.Add(OutNorthEast);
        if (!n && !w)
            states.Add(OutNorthWest);
        if (!s && !e)
            states.Add(OutSouthEast);
        if (!s && !w)
            states.Add(OutSouthWest);

        if (n && e && !ne)
            states.Add(InNorthEast);
        if (n && w && !nw)
            states.Add(InNorthWest);
        if (s && e && !se)
            states.Add(InSouthEast);
        if (s && w && !sw)
            states.Add(InSouthWest);
    }

    private static bool Linked(byte mask, Direction dir)
    {
        return (mask & (1 << (int) dir)) != 0;
    }
}
