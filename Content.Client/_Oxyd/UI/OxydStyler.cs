using System.Linq;
using System.Numerics;
using Content.Client._Oxyd.UI;
using Content.Client._Oxyd.UI.Style;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client;

/// <summary>
/// Handles viewport and panel layout scaling based on window aspect ratio.
/// Visual styling (textures, patch margins, styleboxes) is handled declaratively
/// via <see cref="ErisSheetlet"/> and standard <see cref="Control.StyleClasses"/>.
/// </summary>
public sealed partial class OxydStyler : UIController
{
    public const string DefaultTexture = ErisSheetlet.StyleClassEris;
    public const string DigitalTexture = ErisSheetlet.StyleClassErisDigital;
    public const string ItemSlotTexture = ErisSheetlet.StyleClassErisItemSlot;
    public const string DisplayTexture = ErisSheetlet.StyleClassErisDisplay;

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
    }

    [Obsolete("Styling is now handled via ErisSheetlet and StyleClasses. Use StyleClasses on PanelContainer instead.")]
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
    }
}
