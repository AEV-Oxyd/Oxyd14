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
            var newcount = ent.Comp.checksum.Count - state.checkTrim;
            var copy = new BundleComponent.BundleAct[newcount];
            ent.Comp.checksum.CopyTo(copy, state.checkTrim);
            Log.Debug($"Bundle sync, trim is {state.checkTrim}, counts are {ent.Comp.checksum.Count} vs {newcount}");
            if (state.Checksum.Count < newcount)
            {
                for (var i = 0; i < state.Checksum.Count; i++)
                {
                    if (state.Checksum[i].id != copy[i].id)
                        goto applyMods;
                    if (state.Checksum[i].entity != copy[i].entity)
                        goto applyMods;
                }

                ent.Comp.checksum = new List<BundleComponent.BundleAct>(copy);
                Log.Debug($"blocked bundle {ent} with {state}");
                return;
            }
            // fast insertions , wait for reconcilation to be done after time passes
            /*
            Log.Debug($"Reconciling bundle {ent} with {state}, {ent.Comp.lastUse.Value} vs {state.sentTick.Value}");
            if (ent.Comp.containing.Count < state.Containing.Count &&
                ent.Comp.lastUse.Value - state.sentTick.Value < 30)
            {
                // inconsistent order means we have something new that should be applied immediately!
                for (var i = 0; i < ent.Comp.containing.Count; i++)
                {
                    if (ent.Comp.containing[i] != state.Containing[i])
                        goto applyMods;
                }

                Log.Debug($"blocked bundle {ent} with {state}");
                return;
            }
            */
            /*
            if (ent.Comp.ignoreNext)
            {
                ent.Comp.ignoreNext = false;
                Log.Debug($"Ignoring bundle {ent} with {state}");
                return;
            }
            */
            Log.Debug($"Reconciling bundle {ent} with {state.Containing.Count} items");
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

