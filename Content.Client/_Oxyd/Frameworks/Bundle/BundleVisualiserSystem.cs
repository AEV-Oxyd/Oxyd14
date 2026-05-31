using System.Numerics;
using Content.Shared._Oxyd.Framework.Bundles;
using Content.Shared.CCVar;
using Content.Shared.Singularity.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Framework.Bundle;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class BundleVisualiserSystem : VisualizerSystem<BundleComponent>
{
    [Dependency] private IPlayerManager player = default!;
    public const string LayerBase = "OXB_";
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
    }

    protected override void OnAppearanceChange(EntityUid uid, BundleComponent component, ref AppearanceChangeEvent args)
    {
        base.OnAppearanceChange(uid, component, ref args);
        if (!TryComp<SpriteComponent>(uid, out var selfSprite))
            return;
        for(var i = 0; i < component.containing.Count; i++)
        {
            var netId = component.containing[i];
            var entId = GetEntity(netId);
            if (TerminatingOrDeleted(entId))
                continue;
            if (component.bundlePositions.ContainsKey(entId))
                continue;
            if(!TryComp(entId, out SpriteComponent? sprite))
                continue;
            if (sprite.BaseRSI is null)
                continue;
            var itemRsi = sprite.BaseRSI;
            if (!SpriteSystem.LayerExists(uid, LayerBase + i))
            {
                SpriteSystem.AddBlankLayer((uid, selfSprite), i);
            }
            if (!SpriteSystem.TryGetLayer(uid, LayerBase + i, out var layer, false))
                continue;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
    }

     public sealed partial class BundleOverlay : Overlay, IEntityEventSubscriber
    {

        [Dependency] private IEntityManager _entMan = default!;
        [Dependency] private IPrototypeManager _prototypeManager = default!;
        [Dependency] private IConfigurationManager _configManager = default!;
        private SpriteSystem? sprites;

        public BundleOverlay()
        {
            IoCManager.InjectDependencies(this);
            ZIndex = 101; // Should be drawn after the placement overlay so admins placing items near the singularity can tell where they're going.

        }

        private int _count = 0;

        protected override bool BeforeDraw(in OverlayDrawArgs args)
        {
            if (args.Viewport.Eye == null)
                return false;
            _count = 0;

            return _count > 0;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (args.Viewport.Eye == null)
                return;
            if (sprites is null && !_entMan.TrySystem(out sprites))
                return;
            var q = _entMan.AllEntityQueryEnumerator<BundleComponent, SpriteComponent, TransformComponent>();
            var sq = _entMan.GetEntityQuery<SpriteComponent>();
            while (q.MoveNext(out var id, out var bundle, out var sprite, out var transform))
            {
                foreach (var net in bundle.containing)
                {
                    var resolved = _entMan.GetEntity(net);
                    if (resolved == EntityUid.Invalid)
                        continue;
                    if (!sq.TryComp(resolved, out var local))
                        continue;
                    sprites.RenderSprite((resolved, local),
                        args.WorldHandle,
                        Angle.Zero,
                        Angle.Zero,
                        transform.WorldPosition);
                }
            }
        }

    }
}
