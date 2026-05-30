using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.Containers;

namespace Content.Shared._Oxyd.Framework.Bundles;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class BundleSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem containers = default!;
    [Dependency] private SharedHandsSystem hands = default!;
    [Dependency] private SharedInteractionSystem interaction = default!;

    public static readonly string storeKey = "storagebase";
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<BundleComponent, ComponentStartup>(onStart);
        SubscribeLocalEvent<BundableComponent, InteractUsingEvent>(onAttack);
    }

    public void onStart(Entity<BundleComponent> ent, ref ComponentStartup args)
    {
        containers.EnsureContainer<Container>(ent.Owner, storeKey);
    }

    public void onAttack(Entity<BundableComponent> ent, ref InteractUsingEvent ev)
    {

    }
}
