using System.Numerics;
using Content.Client.PDA;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Sheetlets;
using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.PDA;

[CommonSheetlet]
public sealed class PdaSheetlet : Sheetlet<NanotrasenStylesheet>
{
    public override StyleRule[] GetRules(NanotrasenStylesheet sheet, object config)
    {
        IPanelConfig panelCfg = sheet;
        var resCache = IoCManager.Resolve<IResourceCache>();
        var erisDigitalTex = resCache.GetTexture("/Textures/Oxyd/erisported/UI/ErisStyleDigital.png");

        var erisDigitalBox = new StyleBoxTexture
        {
            Texture = erisDigitalTex,
            TextureScale = new Vector2(2, 2),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
        erisDigitalBox.SetPatchMargin(StyleBox.Margin.All, 5);

        var erisStyleTex = resCache.GetTexture("/Textures/Oxyd/erisported/UI/ErisStyleWhite.png");
        var erisMetalBox = new StyleBoxTexture
        {
            Texture = erisStyleTex,
            TextureScale = new Vector2(2, 2),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
        erisMetalBox.SetPatchMargin(StyleBox.Margin.All, 8);

        var pdaRailingTex = resCache.GetTexture("/Textures/Oxyd/erisported/UI/FuturisticPdaRailing.png");
        var pdaRailingBox = new StyleBoxTexture
        {
            Texture = pdaRailingTex,
            TextureScale = new Vector2(1, 1),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
        pdaRailingBox.SetPatchMargin(StyleBox.Margin.All, 10);

        var holoTex = resCache.GetTexture("/Textures/Oxyd/erisported/UI/HoloPlateWhite.png");
        var holoPlateBox = new StyleBoxTexture
        {
            Texture = holoTex,
            TextureScale = new Vector2(2, 2),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
        holoPlateBox.SetPatchMargin(StyleBox.Margin.All, 6);

        var erisBezelBox = new StyleBoxTexture
        {
            Texture = erisStyleTex,
            TextureScale = new Vector2(1, 1),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
        erisBezelBox.SetPatchMargin(StyleBox.Margin.All, 6);

        var angleBorderRect =
            sheet.GetTexture(panelCfg.GeometricPanelBorderPath).IntoPatch(StyleBox.Margin.All, 10);

        return
        [
            //PDA - Backgrounds (Textured Metal Chassis + Holo Plate + Eris Cyber Screen)
            E<PanelContainer>()
                .Class("PdaContentBackground")
                .Prop(PanelContainer.StylePropertyPanel, erisDigitalBox)
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#060A10")),

            E<PanelContainer>()
                .Class("PdaBackground")
                .Prop(PanelContainer.StylePropertyPanel, StyleBoxHelpers.SquareStyleBox(sheet))
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#080D14")),

            E<PanelContainer>()
                .Class("PdaBackgroundRect")
                .Prop(PanelContainer.StylePropertyPanel, holoPlateBox)
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#151D28")),

            E<PanelContainer>()
                .Class("PdaBorderRect")
                .Prop(PanelContainer.StylePropertyPanel, pdaRailingBox),

            //PDA - Settings Buttons
            E<PdaSettingsButton>()
                .Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(PdaSettingsButton.StylePropertyBgColor, Color.FromHex("#0A121C"))
                .Prop(PdaSettingsButton.StylePropertyFgColor, Color.FromHex("#00D4FF")),

            E<PdaSettingsButton>()
                .Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(PdaSettingsButton.StylePropertyBgColor, Color.FromHex("#00D4FF25"))
                .Prop(PdaSettingsButton.StylePropertyFgColor, Color.White),

            E<PdaSettingsButton>()
                .Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(PdaSettingsButton.StylePropertyBgColor, Color.FromHex("#00D4FF50"))
                .Prop(PdaSettingsButton.StylePropertyFgColor, Color.FromHex("#E0F7FA")),

            E<PdaSettingsButton>()
                .Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(PdaSettingsButton.StylePropertyBgColor, Color.FromHex("#0A121C"))
                .Prop(PdaSettingsButton.StylePropertyFgColor, Color.FromHex("#455A64")),

            //PDA - Program Items
            E<PdaProgramItem>()
                .Pseudo(ContainerButton.StylePseudoClassNormal)
                .Prop(PdaProgramItem.StylePropertyBgColor, Color.FromHex("#0A121C")),

            E<PdaProgramItem>()
                .Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(PdaProgramItem.StylePropertyBgColor, Color.FromHex("#00D4FF25")),

            E<PdaProgramItem>()
                .Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(PdaProgramItem.StylePropertyBgColor, Color.FromHex("#00D4FF50")),

            //PDA - Bezel Resource Bar
            E<PanelContainer>()
                .Class("PdaBezelFooter")
                .Prop(PanelContainer.StylePropertyPanel, erisBezelBox)
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#151D26")),

            E<Label>()
                .Class("PdaBezelFooterText")
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(10))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#668090")),

            E<Label>()
                .Class("PdaBezelStatsText")
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(10))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#80B8D0")),

            E<Label>()
                .Class("PdaBezelIdText")
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(10))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#40B880")),

            //PDA - Text
            E<Label>()
                .Class("PdaContentFooterText")
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(10))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#00D4FF")),

            E<Label>()
                .Class("PdaWindowFooterText")
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(10))
                .Prop(Label.StylePropertyFontColor, Color.FromHex("#333d3b")),
        ];
    }
}
