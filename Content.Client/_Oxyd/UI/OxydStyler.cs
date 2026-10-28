using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.UI;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client;

// this exists because i dont wanna bother with sheetlets.
// also handles automatic scaling of the UI
// Horrid , SPCR 2026
public sealed class OxydStyler : UIController
{
    public const string DefaultTexture = "ErisStyleInit";
    public const string DigitalTexture = "ErisStyleDigitalInit";
    public const string ItemSlotTexture = "ErisItemSlot";
    public const string DisplayTexture = "ErisStyleDisplay";
    public const string LeftRectTag = "LeftRect";
    public const string DynPanelTag = "DynPanelBox";
    public const string RightRectTag = "RightRect";
    public const string ViewportRectTag = "ViewportContainer";
    public const string LowerBarTag = "LowerBar";
    public const string InventoryTag = "inventory";
    // minimum size of elements in pixels based on width/height report, closest one is used
    // 1 = left rect
    // 2 = viewport rect
    // 3 = right rect
    // 4 = bottom rect
    // 5 = minsize for autoscale
    // ratios are generated off these 
    public Dictionary<float, Vector2[]> pixelDefs = new()
    {
        {1.77f, [ // min window size : (1024, 576)
                new Vector2(256,512),
                new Vector2(512,512),
                new Vector2(256,512),
                new Vector2(1024,64),
                new Vector2(1024,576)
            ] },
        {1.53f,[ // min window size : ( 1344, 896)
            new Vector2(256,832),
            new Vector2(832,832),
            new Vector2(256,832),
            new Vector2(1280,64),
            new Vector2(1344,896)
        ] }
    };
    public Vector2 lastSize = Vector2.Zero;
    [Dependency] private OxTagController tags = default!;
    [Dependency] private IResourceCache res = default!;
    
    /// <summary>
    ///  64 height, variable width for my specific controls (QuickInventoryStorage)
    /// </summary>
    public Texture leftText = default!;
    public Texture middleText = default!;
    public Texture RightText = default!;
    public int sideTextWidth = 11;

    public override void Initialize()
    {
        base.Initialize();
        leftText = res.GetTexture(@"/Textures/Oxyd/erisported/UI/ErisStyle64LeftPane.png");
        middleText = res.GetTexture(@"/Textures/Oxyd/erisported/UI/ErisStyle64MiddlePane.png");
        RightText = res.GetTexture(@"/Textures/Oxyd/erisported/UI/ErisStyle64RightPane.png");
        foreach (var t in tags.getControls(DefaultTexture))
        {
            if(t is PanelContainer target)
                InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyle.png", 8, 2);
        }
        foreach (var t in tags.getControls(DigitalTexture))
        {
            if (t is PanelContainer target)
                InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyleDigital.png", 5, 2);
        }

        foreach (var t in tags.getControls(ItemSlotTexture))
        {
            if(t is PanelContainer target)
                InitTextureEris(target,"/Textures/Oxyd/erisported/UI/ErisItemSlot.png",4,2);
        }

        foreach (var t in tags.getControls(DisplayTexture))
        {
            if (t is PanelContainer target)
                InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyleDisplay.png", 0, 2);
        }
        tags.Added += (s, control) =>
        {
            if (control is PanelContainer target)
            {
                switch (s)
                {
                    case DefaultTexture:
                        InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyle.png", 8, 2);
                        break;
                    case DigitalTexture:
                        InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyleDigital.png", 5, 2);
                        break;
                    case ItemSlotTexture:
                        InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisItemSlot.png", 4, 2);
                        break;
                    case DisplayTexture:
                        InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyleDisplay.png", 0, 2);
                        break;
                }
            }
        };
    }

    public void InitTextureEris(PanelContainer target, string path, int patchmargin = 0, int scale = 1)
    {
        var text = res.GetTexture(path);
        var style = new StyleBoxTexture()
        {
            Texture = text,
        };
        style.SetPatchMargin(StyleBox.Margin.All, patchmargin);
        style.TextureScale = Vector2.One * scale;
        style.Mode = StyleBoxTexture.StretchMode.Tile;
        target.PanelOverride = style;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (lastSize != UIManager.RootControl.Size)
        {
            lastSize = UIManager.RootControl.Size;
            float ratio = lastSize.X / lastSize.Y;
            float closest = float.MaxValue;
            foreach (var k in pixelDefs.Keys)
            {
                if(Math.Abs(k - ratio) < Math.Abs(closest - ratio))
                    closest = k;
            }
            Control left = tags.getControl(LeftRectTag);
            Control right = tags.getControl(RightRectTag);
            Control lower = tags.getControl(LowerBarTag);
            Control viewport = tags.getControl(ViewportRectTag);
            Control dynPanel = tags.getControl(DynPanelTag);
            left.MinSize = pixelDefs[closest][0];
            viewport.MinSize = pixelDefs[closest][1];
            right.MinSize = pixelDefs[closest][2];
            lower.MinSize = pixelDefs[closest][3];
            if (lastSize.X < pixelDefs[closest][4].X || lastSize.Y < pixelDefs[closest][4].Y)
                return;
            var HeightAlloc = UIManager.RootControl.Size.Y - lower.MinSize.Y;
            viewport.SetSize = new Vector2(HeightAlloc);
            var leftoverSpace = (lastSize.X - HeightAlloc) / 2;
            left.SetWidth = leftoverSpace;
            right.SetWidth = leftoverSpace;
            lower.SetWidth = lastSize.X;
            dynPanel.SetHeight = HeightAlloc - tags.getControl(InventoryTag).Height - 256;


        }

        //Log.Debug(UIManager.RootControl.UIScale.ToString());
    }
}