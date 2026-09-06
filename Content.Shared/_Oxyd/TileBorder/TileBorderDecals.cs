using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.TileBorder;

/// <summary>
/// Prototype id contract for server-generated floor-rim decals.
/// One precomposed RSI state per canonical neighbour mask; ids derive from BorderSprites.
/// State names are 2-digit lowercase hex (ToString("x2")).
/// </summary>
public static class TileBorderDecals
{
    public const string Tag = "tile-border";
    public const string IdPrefix = "TileBorder-";
    public const int ZIndex = -1;

    public static string StateName(byte canonical)
    {
        return canonical.ToString("x2");
    }

    public static string RsiStem(ResPath borderSprites)
    {
        return borderSprites.FilenameWithoutExtension;
    }

    public static string PrototypeId(ResPath borderSprites, byte canonical)
    {
        return IdPrefix + RsiStem(borderSprites) + "-" + StateName(canonical);
    }

    public static bool IsGenerated(string prototypeId)
    {
        return prototypeId.StartsWith(IdPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unique Canonicalize(mask) values for mask 0..255 except InteriorMask (0xFF). Count is 69.
    /// </summary>
    public static IReadOnlyList<byte> AllCanonicalMasks { get; } = EnumerateCanonicalMasks().ToArray();

    public static IEnumerable<byte> EnumerateCanonicalMasks()
    {
        var seen = new HashSet<byte>();
        for (var i = 0; i < 256; i++)
        {
            if (i == TileBorderMask.InteriorMask)
                continue;

            var canonical = TileBorderMask.Canonicalize((byte) i, out _);
            if (seen.Add(canonical))
                yield return canonical;
        }
    }
}
