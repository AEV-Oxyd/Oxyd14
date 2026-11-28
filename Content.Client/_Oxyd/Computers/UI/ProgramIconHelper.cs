using System.Collections.Generic;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Programs;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.UI;

/// <summary>
/// Resolves application icons locally from client-synced <see cref="EntityPrototype"/> definitions
/// (<see cref="ProgramComponent.Icon"/>) without requiring extra networked sprite payloads.
/// </summary>
public sealed class ProgramIconResolver
{
    private const string FallbackIconPath = "/Textures/Interface/VerbIcons/settings.svg.192dpi.png";

    private readonly IEntityManager _entMan;
    private readonly IPrototypeManager _proto;
    private readonly IResourceCache _resourceCache;
    private readonly IComponentFactory _compFactory;
    private readonly Dictionary<string, Texture?> _pathCache = new();
    private readonly Dictionary<string, Texture?> _protoCache = new();

    public ProgramIconResolver(
        IEntityManager entMan,
        IPrototypeManager proto,
        IResourceCache resourceCache,
        IComponentFactory compFactory)
    {
        _entMan = entMan;
        _proto = proto;
        _resourceCache = resourceCache;
        _compFactory = compFactory;
    }

    public Texture? TryLoadTexture(string texturePath)
    {
        if (_pathCache.TryGetValue(texturePath, out var cached))
            return cached;

        Texture? tex = null;
        if (_resourceCache.TryGetResource<TextureResource>(new ResPath(texturePath), out var texRes))
            tex = texRes.Texture;

        _pathCache[texturePath] = tex;
        return tex;
    }

    public Texture? ResolveProgramIcon(OsProgramEntryDto entry)
    {
        if (entry.Icon != null)
        {
            var direct = TryLoadSprite(entry.Icon);
            if (direct != null)
                return direct;
        }

        if (!string.IsNullOrWhiteSpace(entry.PrototypeId))
        {
            if (_protoCache.TryGetValue(entry.PrototypeId, out var cached))
                return cached ?? TryLoadTexture(FallbackIconPath);

            Texture? resolved = null;
            if (_proto.TryIndex<EntityPrototype>(entry.PrototypeId, out var proto) &&
                proto.TryGetComponent<ProgramComponent>(out var progComp, _compFactory) &&
                progComp.Icon != null)
            {
                resolved = TryLoadSprite(progComp.Icon);
            }

            _protoCache[entry.PrototypeId] = resolved;
            if (resolved != null)
                return resolved;
        }

        return TryLoadTexture(FallbackIconPath);
    }

    public Texture? TryLoadSprite(SpriteSpecifier? spec)
    {
        if (spec == null || !_entMan.TrySystem<SpriteSystem>(out var spriteSys))
            return null;

        try
        {
            return spriteSys.Frame0(spec);
        }
        catch
        {
            return null;
        }
    }
}
