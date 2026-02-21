using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Framework;

public static class OxydHelpers
{
    public static ResPath getSpritePath(SpriteSpecifier target)
    {
        switch (target)
        {
            case SpriteSpecifier.Rsi cast:
                return new ResPath(cast.RsiPath + $"/{cast.RsiState}");
            case SpriteSpecifier.Texture cast:
                return cast.TexturePath;
        }
        return ResPath.Empty;
    }
}
