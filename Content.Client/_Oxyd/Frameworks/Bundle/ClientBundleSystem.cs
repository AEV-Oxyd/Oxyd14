using Content.Shared._Oxyd.Framework.Bundles;
using Robust.Client.GameObjects;

namespace Content.Client._Oxyd.Framework.Bundle;

/// <summary>
/// This handles...
/// </summary>
public sealed class ClientBundleSystem : BundleSystem
{

    public override void afterMerge(Entity<BundleComponent> bundle)
    {
        var sp = EnsureComp<SpriteComponent>(bundle);
        var ap = EnsureComp<AppearanceComponent>(bundle);
    }
}
