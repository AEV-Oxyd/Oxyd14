using System.Collections;
using System.Linq;
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
public sealed partial class BundleVisualiserSystem : VisualizerSystem<BundableComponent>
{
    [Dependency] private IPlayerManager player = default!;
    [Dependency] private ContainerSystem containers = default!;
    [Dependency] private ClientOxydHelpers oxyd = default!;
    public const string BakeIdentifier = "@";
    public const string BakeEnder = "@";
    /// <summary>
    /// Refactor after engine PR gets merged if ever SPCR 2026
    /// https://github.com/space-wizards/RobustToolbox/pull/6606
    /// </summary>
    public Queue<EntityUid> queued = new();
    public Queue<EntityUid> queuedThisTick = new();


    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BundleComponent, AfterAutoHandleStateEvent>(OnState);
    }

    public void OnState(EntityUid uid, BundleComponent component, ref AfterAutoHandleStateEvent args)
    {
        queuedThisTick.Enqueue(uid);
    }

    protected override void OnAppearanceChange(EntityUid uid,
        BundableComponent component,
        ref AppearanceChangeEvent args)
    {
        base.OnAppearanceChange(uid, component, ref args);
        if (containers.TryGetContainingContainer(uid, out var container) && HasComp<BundleComponent>(container.Owner))
            queuedThisTick.Enqueue(container.Owner);
    }

    public void BakeLayers(Entity<SpriteComponent> source, Entity<SpriteComponent> target, Vector2 offset)
    {
        var indice = target.Comp.AllLayers.Count();
        for (var i = 0; i < source.Comp.AllLayers.Count(); i++)
        {
            var layer = source.Comp[i];
            if (layer is SpriteComponent.Layer layerData)
            {
                var genKey = BakeIdentifier + source.Owner + i;
                if (i == source.Comp.AllLayers.Count() - 1)
                    genKey += BakeEnder;
                var clone = SpriteSystem.AddBlankLayer((target.Owner, target.Comp), indice);
                SpriteSystem.LayerSetData((target.Owner, target.Comp), indice, layerData.ToPrototypeData());
                //clone.SetRsi(layer.Rsi);
                SpriteSystem.LayerSetRsi((target.Owner, target.Comp), indice, layer.ActualRsi, layer.RsiState);
                SpriteSystem.LayerSetOffset((target.Owner, target.Comp), indice, clone.Offset + offset);
                SpriteSystem.LayerMapSet((target.Owner, target.Comp), genKey, indice++);
            }
        }
    }

    public void WipeBaked(Entity<SpriteComponent> source, Entity<SpriteComponent> target)
    {
        var i = 0;
        var indexed = 0;
        while (indexed++ < 1000)
        {
            var key = BakeIdentifier + source.Owner + i++;
            if (SpriteSystem.TryGetLayer((target.Owner, target.Comp), key, out _, false))
                SpriteSystem.RemoveLayer((target.Owner, target.Comp), key);
            else
            {
                key += BakeEnder;
                if (SpriteSystem.TryGetLayer((target.Owner, target.Comp), key, out _, false))
                {
                    SpriteSystem.RemoveLayer((target.Owner, target.Comp), key);
                    break;
                }
                else
                    Log.Error(
                        $"BundleVisualizer had a SpriteBakeWipe with no BakeEnder identification present , layers aren't being properly built/configured, key was {key}");
            }

        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        while (queued.TryDequeue(out var thing))
        {
            if (!TryComp<BundleComponent>(thing, out var bundle))
                continue;
            if (!TryComp<SpriteComponent>(thing, out var selfsprite))
                continue;
            var output = true;
            while(output)
                output = SpriteSystem.RemoveLayer((thing, selfsprite), 0);
            foreach (var net in bundle.containing)
            {
                var ent = GetEntity(net);
                if (TerminatingOrDeleted(ent))
                    continue;
                if (!TryComp<SpriteComponent>(ent, out var sprite))
                    continue;
                //WipeBaked((ent, sprite), (thing, selfsprite));
                BakeLayers((ent, sprite), (thing, selfsprite), bundle.bundlePositions[net]);
            }
        }

        while (queuedThisTick.TryDequeue(out var uid))
            queued.Enqueue(uid);
    }
}
