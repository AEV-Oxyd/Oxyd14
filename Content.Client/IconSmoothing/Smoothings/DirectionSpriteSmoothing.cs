using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.IconSmoothing.Smoothings;

/// <summary>
/// Calculates a sprite state from Cardinal Direction flags.
/// Shrimple as that.
/// </summary>
public sealed partial class DirectionSpriteSmoothing : ISpriteSmoothState
{
    [DataField(required:true)]
    public string Base { get; set; }

    [DataField(required:true)]
    public HashSet<string> Mask { get; set; }

    [DataField]
    public string LayerKey { get; set; } = "direction";

    [DataField]
    public int? Index { get; set; }

    [DataField]
    public ProtoId<ShaderPrototype>? Shader { get; set; }

    public void InitializeStates(Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        sprite.LayerMapSet(entity.AsNullable(), LayerKey, sprite.AddRsiLayer(entity.AsNullable(), Base + 0, index: Index));
    }

    public IEnumerable<SpriteLayer> EnumerateStates(HashSet<string>?[] layers, Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        var match = DirectionFlag.None;
        for (byte i = 0; 2 * i < IconSmoothSystem.Directions; i++)
        {
            if (layers[2 * i] is { } keys && keys.Overlaps(Mask))
                match |= (DirectionFlag)(1 << i);
        }

        yield return new SpriteLayer(LayerKey, Base + (byte)match);
    }
}

/// <summary>
/// Calculates a sprite state from legacy/BYOND CardinalFlags (North = 1, South = 2, East = 4, West = 8).
/// </summary>
public sealed partial class CardinalFlagsSpriteSmoothing : ISpriteSmoothState
{
    [DataField(required: true)]
    public string Base { get; set; }

    [DataField(required: true)]
    public HashSet<string> Mask { get; set; }

    [DataField]
    public string LayerKey { get; set; } = "direction";

    [DataField]
    public int? Index { get; set; }

    [DataField]
    public ProtoId<ShaderPrototype>? Shader { get; set; }

    public void InitializeStates(Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        sprite.SetSnapCardinals(entity.AsNullable(), true);
        sprite.LayerMapSet(entity.AsNullable(), LayerKey, sprite.AddRsiLayer(entity.AsNullable(), Base + 0, index: Index));
    }

    public IEnumerable<SpriteLayer> EnumerateStates(HashSet<string>?[] layers, Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        byte match = 0;
        if (layers[(int)Direction.North] is { } north && north.Overlaps(Mask))
            match |= 1;
        if (layers[(int)Direction.South] is { } south && south.Overlaps(Mask))
            match |= 2;
        if (layers[(int)Direction.East] is { } east && east.Overlaps(Mask))
            match |= 4;
        if (layers[(int)Direction.West] is { } west && west.Overlaps(Mask))
            match |= 8;

        yield return new SpriteLayer(LayerKey, Base + match);
    }
}
