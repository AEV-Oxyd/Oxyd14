using Content.Shared._Oxyd.Framework.Bundles;
using Robust.Client.GameObjects;

namespace Content.Client._Oxyd.Framework.Bundle;

/// <summary>
/// This handles...
/// </summary>
public sealed class ClientBundleSystem : BundleSystem
{
    [Dependency] private BundleVisualiserSystem visualizer = default!;

    public override void afterMerge(Entity<BundleComponent> bundle)
    {
        visualizer.queuedThisTick.Enqueue(bundle.Owner);
    }
}
