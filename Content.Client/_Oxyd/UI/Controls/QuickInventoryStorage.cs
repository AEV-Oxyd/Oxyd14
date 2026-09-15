using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Content.Client.UserInterface.Systems.Storage;
using Content.Shared.IdentityManagement;
using Content.Shared.Input;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._Oxyd.UI;

public sealed class QuickInventoryStorage : GridMapping
{
    [Dependency] private IEntityManager entityManager = default!;
    public Entity<StorageComponent> storage = new Entity<StorageComponent>();
    public Dictionary<EntityUid, SpriteView> existing = new ();
    public List<EntityUid> contained = new List<EntityUid>();
    public SpriteView containerRender;

    public QuickInventoryStorage()
    {
        IoCManager.InjectDependencies(this);
        containerRender = new SpriteView(storage.Owner, entityManager) {Scale = new Vector2(2f)};
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
        containerRender.ModulateSelfOverride = new Color(1f,1f,1f, 0.3f);
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
            var c = new SpriteView(ent, entityManager) {Scale = new Vector2(2f)};
            c.MouseFilter = MouseFilterMode.Pass;
            c.MinSize = new Vector2(64, 64);
            c.OnKeyBindDown += (args) =>
            {
                if(args.Function ==  EngineKeyFunctions.UIClick )
                    entityManager.RaisePredictiveEvent(new StorageInteractWithItemEvent(entityManager.GetNetEntity(ent), entityManager.GetNetEntity(storage)));
                //UpdateContained();
            };
            AddChild(c);
            existing[ent] = c;
        }
        var gone = existing.Keys.Except(contained).ToList();
        foreach (var ent in gone)
        {
            if (!existing.TryGetValue(ent, out var ctrl))
                continue;
            RemoveChild(ctrl);
            existing.Remove(ent);
        }

        containerRender.MinWidth = existing.Keys.Count()/4f * 64;
        containerRender.MinHeight = 64;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        Vector2 desired = new Vector2(0, 0);
        desired.Y = Children.Max(x =>
        {
            if(x != containerRender)
                return x.Height;
            return 0;
        });
        desired.X = Children.Sum(x =>
        {
            if(x != containerRender)
                return x.Width;
            return 0;
        });
        return desired;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        float arrangeX = 0;
        float arrangeY = 0;
        int items = contained.Count;
        foreach (var elem in Children)
        {
            if (elem is SpriteView x)
            {
                if (x == containerRender)
                    continue;
                x.Arrange(new UIBox2(arrangeX, 0, arrangeX + x.MinWidth, x.MinHeight));
                arrangeX += x.Width;
            }
        }
        containerRender.Arrange(new UIBox2(0, 0, arrangeX, 64));
        return finalSize;
    }
}