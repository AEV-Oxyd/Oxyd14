using Content.Shared._Oxyd.Framework.Bundles;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Framework.Bundle;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientBundleSystem : BundleSystem
{
    [Dependency] private BundleVisualiserSystem visualizer = default!;

    public override void afterMerge(Entity<BundleComponent> bundle)
    {
        base.afterMerge(bundle);
        visualizer.queuedThisTick.Enqueue(bundle);
    }

    public override void afterRemove(Entity<BundleComponent> bundle)
    {
        base.afterRemove(bundle);
        visualizer.queuedThisTick.Enqueue(bundle);
    }
}

