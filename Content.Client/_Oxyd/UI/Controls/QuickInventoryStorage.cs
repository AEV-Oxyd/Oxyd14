using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Content.Client.UserInterface.Systems.Storage;
using Content.Shared.IdentityManagement;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Oxyd.UI;

public sealed class QuickInventoryStorage : BoxContainer
{
    [Dependency] private IEntityManager entityManager = default!;
    public Entity<StorageComponent> storage = new Entity<StorageComponent>();
    public Dictionary<EntityUid, Button> existing = new ();
    public List<EntityUid> contained = new List<EntityUid>();
    public SpriteView containerRender = new();

    public QuickInventoryStorage()
    {
        Orientation = LayoutOrientation.Horizontal;
        IoCManager.InjectDependencies(this);
        containerRender.HorizontalAlignment = HAlignment.Stretch;
        containerRender.VerticalAlignment = VAlignment.Center;
        AddChild(containerRender);
    }
    
    public void UpdateContainer(Entity<StorageComponent>? entity)
    {
        if (entity is null)
            return;
        if(entityManager.Deleted(entity))
            return;
        storage = entity.Value;
        containerRender.SetEntity(storage.Owner);
        UpdateContained();
    }

    public void UpdateContained()
    {
        if (entityManager.Deleted(storage))
            contained.Clear();
        else
            contained = storage.Comp.Container.ContainedEntities.ToList();
        foreach (var ent in contained)
        {
            if (existing.TryGetValue(ent, out var _))
                continue;
            var b = new Button();
            b.MinHeight = 64;
            b.MinWidth = 32;
            b.AddChild(new SpriteView(ent, entityManager) );
            b.OnButtonDown += (_) =>
            {
                entityManager.RaisePredictiveEvent(new StorageInteractWithItemEvent(entityManager.GetNetEntity(ent), entityManager.GetNetEntity(storage)));
                UpdateContained();
            };
            AddChild(b);
            existing[ent] = b;
        }
        var gone = existing.Keys.Except(contained).ToList();
        foreach (var ent in gone)
        {
            if (!existing.TryGetValue(ent, out var ctrl))
                continue;
            RemoveChild(ctrl);
            existing.Remove(ent);
        }
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var mod = base.ArrangeOverride(finalSize);
        containerRender.Arrange(Rect);
        return mod;
    }
}