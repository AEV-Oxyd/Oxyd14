using Content.Shared.Maps;
using Robust.Shared.Maths;

namespace Content.Shared._Oxyd.TileBorder;

/// <summary>
/// 8-neighbour rim selection for tiles that set <see cref="ContentTileDefinition.BorderSprites"/>.
/// Bit i is set when the neighbour in <see cref="Direction"/> i shares the origin group.
/// Mask bits follow Direction: S=0, SE=1, E=2, NE=3, N=4, NW=5, W=6, SW=7.
/// </summary>
public static class TileBorderMask
{
    public const byte InteriorMask = 0xFF;

    public static bool IsInterior(byte mask) => mask == InteriorMask;

    /// <summary>
    /// Eris-style cardinal connection mask: N=1, S=2, E=4, W=8 (BYOND cardinals).
    /// Diagonals in <paramref name="mask"/> are ignored.
    /// </summary>
    public static byte CardinalDirSum(byte mask)
    {
        byte sum = 0;
        // Direction enum: S=0, E=2, N=4, W=6
        if ((mask & (1 << (int) Direction.North)) != 0)
            sum |= 1;
        if ((mask & (1 << (int) Direction.South)) != 0)
            sum |= 2;
        if ((mask & (1 << (int) Direction.East)) != 0)
            sum |= 4;
        if ((mask & (1 << (int) Direction.West)) != 0)
            sum |= 8;
        return sum;
    }

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
    /// Rotate neighbour-link bits 90° clockwise: bit i moves to bit (i - 2) mod 8.
    /// </summary>
    public static byte RotateClockwise(byte mask)
    {
        return (byte) ((mask >> 2) | (mask << 6));
    }

    /// <summary>
    /// Among the four clockwise 90° rotations of <paramref name="mask"/>, pick the numerically
    /// smallest as the canonical bake key. <paramref name="cwTurns"/> is how many CW turns from
    /// the original mask to that canonical (0..3), used as Decal.Angle = cwTurns * 90°.
    /// </summary>
    public static byte Canonicalize(byte mask, out int cwTurns)
    {
        var best = mask;
        var bestTurns = 0;
        var current = mask;

        for (var turns = 0; turns < 4; turns++)
        {
            if (current < best)
            {
                best = current;
                bestTurns = turns;
            }

            current = RotateClockwise(current);
        }

        cwTurns = bestTurns;
        return best;
    }
}
