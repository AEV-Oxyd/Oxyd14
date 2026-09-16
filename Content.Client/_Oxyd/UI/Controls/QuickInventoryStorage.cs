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

public sealed class QuickInventoryStorage : Container
{
    [Dependency] private IEntityManager entityManager = default!;
    private OxydStyler styler = default!;
    public Entity<StorageComponent> storage = new Entity<StorageComponent>();
    public Dictionary<EntityUid, SpriteView> existing = new ();
    public List<EntityUid> contained = new List<EntityUid>();
    public SpriteView containerRender;
    

    public QuickInventoryStorage()
    {
        IoCManager.InjectDependencies(this);
        styler = IoCManager.Resolve<IUserInterfaceManager>().GetUIController<OxydStyler>();
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
            c.MinSize = new Vector2(32, 32);
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

        containerRender.MinWidth = existing.Keys.Count() * 32;
        containerRender.MinHeight = 64;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var c = contained.Count;
        Vector2 desired = new Vector2(Math.Max((c/2 + c%2)* 32f, 64f) + styler.sideTextWidth*2, 64f);
        return desired;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        int bc = 0;
        Vector2 posOffset = new Vector2(styler.sideTextWidth, 0);
        foreach (var elem in Children)
        {
            if (elem is SpriteView x)
            {
                if (x == containerRender)
                    continue;
                x.Arrange(new UIBox2(posOffset.X, posOffset.Y,  posOffset.X + 32f, posOffset.Y + 32f));
                switch(bc%4)
                {
                    case 0:
                        posOffset.Y += 32;
                        break;
                    case 1:
                        posOffset.Y -= 32;
                        posOffset.X += 32;
                        break;
                    case 2:
                        posOffset.Y += 32;
                        break;
                    case 3:
                        posOffset.X += 32;
                        posOffset.Y -= 32;
                        break;
                }

                bc++;

            }
        }
        var finalBox = new UIBox2(0f, 0f, Math.Max((bc/2+bc%2 ) * 32f + styler.sideTextWidth * 2, 64f+styler.sideTextWidth*2), 64);
        containerRender.Arrange(finalBox);
        return finalBox.BottomRight;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        Vector2 offset = Vector2.Zero;
        handle.DrawTexture(styler.leftText, offset);
        offset.X += styler.sideTextWidth;
        while (offset.X < PixelWidth - styler.sideTextWidth)
        {
            handle.DrawTexture(styler.middleText, offset);
            offset.X += 32;
        }
        handle.DrawTexture(styler.RightText, offset);
        base.Draw(handle);
        handle.DrawRect(new UIBox2(Vector2.One*2, Rect.BottomRight - Rect.TopLeft - Vector2.One*2), Color.FromHex("#16161e"), false);
        
    }
}