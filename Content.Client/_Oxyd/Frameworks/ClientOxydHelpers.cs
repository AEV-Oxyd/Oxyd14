using Robust.Client.GameObjects;
using Robust.Client.Timing;
using Robust.Shared.GameObjects;
using static Robust.Client.GameObjects.SpriteComponent;

namespace Content.Client._Oxyd.Framework;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientOxydHelpers : EntitySystem
{
    [Dependency] private IClientGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    // returns how many ticks ahead we are simulating as the client
    public uint getPredTicks()
    {
        return _gameTiming.CurTick.Value - _gameTiming.LastRealTick.Value;
    }

    /// <summary>
    /// This is AI generated because i couldn't be bothered to manually do this SPCR 2026
    /// Duplicates all data from <paramref name="source"/> into a new <see cref="Layer"/> appended to
    /// <paramref name="target"/> and returns it.
    /// </summary>
    public Layer CopyLayer(Layer source, Entity<SpriteComponent> target, out int i)
    {
        var newLayer = _sprite.AddBlankLayer(target);

        // Shader - route through the component method so it correctly sets the internal UnShaded flag.
        // Layer.Index is internal, so find the position by iterating AllLayers.
        var shader = source.Shader is { Mutable: true } ? source.Shader.Duplicate() : source.Shader;
        var layerIdx = 0;
        foreach (var l in target.Comp.AllLayers)
        {
            if (ReferenceEquals(l, newLayer))
                break;
            layerIdx++;
        }
        i = layerIdx;
        target.Comp.LayerSetShader(layerIdx, shader, source.ShaderPrototype?.Id);

        // Texture / RSI+State are mutually exclusive; prefer RSI if a valid state exists.
        if (source.State.IsValid)
            _sprite.LayerSetRsi(newLayer, source.RSI, source.State);
        else if (source.Texture != null)
            _sprite.LayerSetTexture(newLayer, source.Texture);

        // Transform - must use SpriteSystem setters because backing fields are internal.
        _sprite.LayerSetScale(newLayer, source.Scale);
        _sprite.LayerSetRotation(newLayer, source.Rotation);
        _sprite.LayerSetOffset(newLayer, source.Offset);

        // Visibility / animation
        _sprite.LayerSetVisible(newLayer, source.Visible);
        _sprite.LayerSetAutoAnimated(newLayer, source.AutoAnimated);
        newLayer.AnimationTimeLeft = source.AnimationTimeLeft;
        newLayer.AnimationTime = source.AnimationTime;
        newLayer.AnimationFrame = source.AnimationFrame;
        newLayer.Cycle = source.Cycle;
        newLayer.Loop = source.Loop;

        // Appearance
        newLayer.Color = source.Color;
        newLayer.DirOffset = source.DirOffset;
        _sprite.LayerSetRenderingStrategy(newLayer, source.RenderingStrategy);
        newLayer.CopyToShaderParameters = source.CopyToShaderParameters is { } csp
            ? new CopyToShaderParameters(csp)
            : null;

        return newLayer;
    }
}
