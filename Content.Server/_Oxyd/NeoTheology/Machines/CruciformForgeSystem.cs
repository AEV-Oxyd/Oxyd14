using Content.Server.Lathe;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.6: the cruciform forge. Materials handed to it are banked by
/// <see cref="SharedMaterialStorageSystem"/>'s own <c>InteractUsing</c> handler (the forge carries a
/// <see cref="MaterialStorageComponent"/>); production is delegated to <see cref="LatheSystem"/>
/// entirely — the litany only queues <see cref="CruciformForgeComponent.Recipe"/> and the lathe
/// spends the materials, runs the timer and drops the result. No lathe UI is exposed.
/// </summary>
public sealed partial class CruciformForgeSystem : EntitySystem
{
    [Dependency] private NeoTheologyMachineSystem _machines = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>
    /// MakeCruciform bridge (Eris <c>rituals/machinery.dm:43-75</c>): the litany asks the forge to
    /// queue its cruciform recipe; the recipe check and the material spend are the lathe's.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyForgeProduce(Entity<CruciformForgeComponent> ent, ref LitanyForgeProduceEvent args)
    {
        args.Handled = args.ValidateOnly ? CanProduce(ent.Owner, ent.Comp) : TryProduce(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// One cruciform at a time: refuses while the lathe is mid-print or has anything queued.
    /// Materials and recipe availability are the lathe's own <see cref="SharedLatheSystem.CanProduce"/>.
    /// </summary>
    public bool CanProduce(EntityUid uid, CruciformForgeComponent? forge = null)
    {
        if (!Resolve(uid, ref forge) || !_machines.IsOperational(uid) ||
            !TryComp<LatheComponent>(uid, out var lathe) ||
            !_prototypes.TryIndex(forge.Recipe, out LatheRecipePrototype? recipe))
            return false;

        if (lathe.CurrentRecipe != null || lathe.Queue.Count != 0)
            return false;

        return _lathe.CanProduce(uid, recipe, component: lathe);
    }

    /// <summary>Queues the cruciform recipe and starts the print run.</summary>
    public bool TryProduce(EntityUid uid, CruciformForgeComponent? forge = null)
    {
        if (!Resolve(uid, ref forge) || !CanProduce(uid, forge) ||
            !_prototypes.TryIndex(forge.Recipe, out LatheRecipePrototype? recipe))
            return false;

        return _lathe.TryAddToQueue(uid, recipe, 1) && _lathe.TryStartProducing(uid);
    }
}
