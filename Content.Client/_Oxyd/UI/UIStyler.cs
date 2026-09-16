using System.Numerics;
using Content.Client._Oxyd.UI;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client;

public sealed class UIStyler : UIController
{
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
        foreach (var t in tags.getControls("ErisStyleInit"))
        {
            if(t is PanelContainer target)
                InitTextureEris(target);
        }
        tags.Added += (s, control) =>
        {
            if (s == "ErisStyleInit" && control is PanelContainer target)
                InitTextureEris(target);
        };
    }

    public void InitTextureEris(PanelContainer target)
    {
        var text = res.GetTexture(@"/Textures/Oxyd/erisported/UI/ErisStyle.png");
        var style = new StyleBoxTexture()
        {
            Texture = text,
        };
        style.SetPatchMargin(StyleBox.Margin.All, 8);
        style.TextureScale = Vector2.One * 3;
        style.Mode = StyleBoxTexture.StretchMode.Tile;
        target.PanelOverride = style;
    }
}