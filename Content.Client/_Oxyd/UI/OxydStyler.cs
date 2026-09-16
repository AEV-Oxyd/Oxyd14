using System.Numerics;
using Content.Client._Oxyd.UI;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client;

public sealed class OxydStyler : UIController
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
                InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyle.png", 8, 2);
        }

        foreach (var t in tags.getControls("ErisStyleDigitalInit"))
        {
            if (t is PanelContainer target)
                InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyleDigital.png", 5, 2);
        }
        tags.Added += (s, control) =>
        {
            if (control is PanelContainer target)
            {
                switch (s)
                {
                    case "ErisStyleInit":
                        InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyle.png", 8, 2);
                        break;
                    case "ErisStyleDigitalInit":
                        InitTextureEris(target, "/Textures/Oxyd/erisported/UI/ErisStyleDigital.png", 5, 2);
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
}