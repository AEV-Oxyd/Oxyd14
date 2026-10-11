using Content.Shared._Oxyd.Medical;
using Robust.Client.GameObjects;

namespace Content.Client._Oxyd.Medical;

/// <summary>
/// Applies the borer's Hide verb (Eris hide(): draw under floor clutter) by
/// swapping the sprite draw depth when the server toggles the appearance data.
/// </summary>
public sealed partial class OxydBorerHiddenVisualizerSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydBorerComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, OxydBorerComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!_appearance.TryGetData<bool>(uid, OxydBorerVisuals.Hidden, out var hidden, args.Component))
            return;

        var depth = hidden
            ? (int) Content.Shared.DrawDepth.DrawDepth.BelowFloor
            : (int) Content.Shared.DrawDepth.DrawDepth.SmallMobs;
        _sprite.SetDrawDepth((uid, args.Sprite), depth);
    }
}
