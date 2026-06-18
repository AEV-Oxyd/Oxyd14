using Content.Shared._Oxyd.Framework.Bundles;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Framework.Bundle;

/// <summary>
/// This handles...
/// </summary>
public sealed class ClientBundleSystem : BundleSystem
{
    [Dependency] private BundleVisualiserSystem visualizer = default!;
    [Dependency] private ClientOxydHelpers helpers = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BundleComponent, ComponentHandleState>(onHandleState);
    }

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


    public void onHandleState(Entity<BundleComponent> ent, ref ComponentHandleState args)
    {
        var d = args.Next;
        d ??= args.Current;
        if (d is BundleComponent.BundleState state)
        {
            // server-side events or initial state . apply instantly!
            if (state.Checksum.Count > ent.Comp.checksum.Count)
                goto applyMods;
            for (var j = ent.Comp.checksum.Count - 1; j > 0; j--)
            {
                if (state.Checksum[^1].entity == ent.Comp.checksum[j].entity &&
                    state.Checksum[^1].id == ent.Comp.checksum[j].id)
                {
                    var itercount = 1;
                    while (++itercount <= state.Checksum.Count && --j >= 0)
                    {
                        var targ = state.Checksum[^itercount];
                        var ex = ent.Comp.checksum[j];
                        if (targ.entity != ex.entity)
                            goto applyMods;
                        if (targ.id != ex.id)
                            goto applyMods;
                    }

                    //Log.Debug($"blocked bundle {ent} with {state}");
                    return;
                }
            }

            //Log.Debug($"Reconciling bundle {ent} with {state.Containing.Count} items");
            applyMods:
            ent.Comp.group = state.Group;
            ent.Comp.containing = state.Containing;
            ent.Comp.usedVolume = state.UsedVolume;
            ent.Comp.checksum = state.Checksum;
            ent.Comp.bundlePositions = state.BundlePositions;
            visualizer.queuedThisTick.Enqueue(ent);
        }
    }
}

