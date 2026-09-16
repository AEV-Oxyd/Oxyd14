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

    public override void Initialize()
    {
        base.Initialize();
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
        style.SetPatchMargin(StyleBox.Margin.Left, 8);
        style.SetPadding(StyleBox.Margin.Left, 8);
        style.TextureScale = Vector2.One;
        style.Mode = StyleBoxTexture.StretchMode.Tile;
        target.PanelOverride = style;
    }
}