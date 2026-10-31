using System.Numerics;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._Oxyd.UI.Style;

/// <summary>
/// Declarative stylesheet rules for CEV-Eris ported UI components and frames.
/// Automatically registered into client stylesheets via <see cref="CommonSheetletAttribute"/>.
/// </summary>
[CommonSheetlet]
public sealed class ErisSheetlet : Sheetlet<PalettedStylesheet>
{
    public const string StyleClassEris = "ErisStyle";
    public const string StyleClassErisDigital = "ErisStyleDigital";
    public const string StyleClassErisItemSlot = "ErisItemSlot";
    public const string StyleClassErisDisplay = "ErisStyleDisplay";
    public const string StyleClassErisHoloButton = "ErisHoloButton";
    public const string StyleClassErisHoloButtonOpenBoth = "ErisHoloButtonOpenBoth";
    public const string StyleClassErisHoloButtonOpenLeft = "ErisHoloButtonOpenLeft";
    public const string StyleClassErisHoloButtonOpenRight = "ErisHoloButtonOpenRight";

    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var erisDefault = CreateErisBox("/Textures/Oxyd/erisported/UI/ErisStyle.png", 8, 2);
        var erisDigital = CreateErisBox("/Textures/Oxyd/erisported/UI/ErisStyleDigital.png", 5, 2);
        var erisItemSlot = CreateErisBox("/Textures/Oxyd/erisported/UI/ErisItemSlot.png", 4, 2);
        var erisDisplay = CreateErisBox("/Textures/Oxyd/erisported/UI/ErisStyleDisplay.png", 0, 2);

        var holoNorm = CreateHoloButtonBox("HoloButton.png");
        var holoHov = CreateHoloButtonBox("HoloButton_Hover.png");
        var holoPress = CreateHoloButtonBox("HoloButton_Pressed.png");
        var holoDis = CreateHoloButtonBox("HoloButton_Disabled.png");

        var holoBothNorm = CreateHoloButtonBox("HoloButton_OpenBoth.png", 0, 6, 0, 6);
        var holoBothHov = CreateHoloButtonBox("HoloButton_OpenBoth_Hover.png", 0, 6, 0, 6);
        var holoBothPress = CreateHoloButtonBox("HoloButton_OpenBoth_Pressed.png", 0, 6, 0, 6);

        var holoLeftNorm = CreateHoloButtonBox("HoloButton_OpenLeft.png", 0, 6, 6, 6);
        var holoLeftHov = CreateHoloButtonBox("HoloButton_OpenLeft_Hover.png", 0, 6, 6, 6);
        var holoLeftPress = CreateHoloButtonBox("HoloButton_OpenLeft_Pressed.png", 0, 6, 6, 6);

        var holoRightNorm = CreateHoloButtonBox("HoloButton_OpenRight.png", 6, 6, 0, 6);
        var holoRightHov = CreateHoloButtonBox("HoloButton_OpenRight_Hover.png", 6, 6, 0, 6);
        var holoRightPress = CreateHoloButtonBox("HoloButton_OpenRight_Pressed.png", 6, 6, 0, 6);

        return
        [
            E<PanelContainer>().Class(StyleClassEris).Panel(erisDefault),
            E<PanelContainer>().Class(StyleClassErisDigital).Panel(erisDigital),
            E<PanelContainer>().Class(StyleClassErisItemSlot).Panel(erisItemSlot),
            E<PanelContainer>().Class(StyleClassErisDisplay).Panel(erisDisplay),

            // OutputPanel styling for chat messages and logs
            E<OutputPanel>().Class(StyleClassErisDigital).Prop(OutputPanel.StylePropertyStyleBox, erisDigital),
            E<OutputPanel>().Class(ErisUIGameScreen.StyleClassChatOutput).Prop(OutputPanel.StylePropertyStyleBox, erisDigital),
            E<OutputPanel>().Class(StyleClassErisItemSlot).Prop(OutputPanel.StylePropertyStyleBox, erisItemSlot),
            E<OutputPanel>().Class(StyleClassErisDisplay).Prop(OutputPanel.StylePropertyStyleBox, erisDisplay),

            // Style chat input panel and text entry to follow Eris digital terminal aesthetic
            E<PanelContainer>().Class(ChatInputBox.StyleClassChatPanel).Panel(erisDigital),
            E<LineEdit>().Class(ChatInputBox.StyleClassChatLineEdit).Prop("font-color", Color.FromHex("#E0F7FA")),
            E<LineEdit>().Class(ChatInputBox.StyleClassChatLineEdit).Pseudo(LineEdit.StylePseudoClassPlaceholder).Prop("font-color", Color.FromHex("#00D4FF80")),

            // Output panel scroll down button
            E<Button>().Class(OutputPanel.StyleClassOutputPanelScrollDownButton).PseudoNormal().Box(holoNorm),
            E<Button>().Class(OutputPanel.StyleClassOutputPanelScrollDownButton).PseudoHovered().Box(holoHov),
            E<Button>().Class(OutputPanel.StyleClassOutputPanelScrollDownButton).PseudoPressed().Box(holoPress),
            E<Button>().Class(OutputPanel.StyleClassOutputPanelScrollDownButton).ParentOf(E<Label>()).FontColor(Color.FromHex("#00D4FF")),

            // Holo button states
            E<Button>().Class(StyleClassErisHoloButton).PseudoNormal().Box(holoNorm),
            E<Button>().Class(StyleClassErisHoloButton).PseudoHovered().Box(holoHov),
            E<Button>().Class(StyleClassErisHoloButton).PseudoPressed().Box(holoPress),
            E<Button>().Class(StyleClassErisHoloButton).PseudoDisabled().Box(holoDis),

            E<ContainerButton>().Class(StyleClassErisHoloButton).PseudoNormal().Box(holoNorm),
            E<ContainerButton>().Class(StyleClassErisHoloButton).PseudoHovered().Box(holoHov),
            E<ContainerButton>().Class(StyleClassErisHoloButton).PseudoPressed().Box(holoPress),
            E<ContainerButton>().Class(StyleClassErisHoloButton).PseudoDisabled().Box(holoDis),

            // Segmented holo buttons
            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).PseudoNormal().Box(holoBothNorm),
            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).PseudoHovered().Box(holoBothHov),
            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).PseudoPressed().Box(holoBothPress),

            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenBoth).PseudoNormal().Box(holoBothNorm),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenBoth).PseudoHovered().Box(holoBothHov),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenBoth).PseudoPressed().Box(holoBothPress),

            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).PseudoNormal().Box(holoLeftNorm),
            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).PseudoHovered().Box(holoLeftHov),
            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).PseudoPressed().Box(holoLeftPress),

            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenLeft).PseudoNormal().Box(holoLeftNorm),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenLeft).PseudoHovered().Box(holoLeftHov),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenLeft).PseudoPressed().Box(holoLeftPress),

            E<Button>().Class(StyleClassErisHoloButtonOpenRight).PseudoNormal().Box(holoRightNorm),
            E<Button>().Class(StyleClassErisHoloButtonOpenRight).PseudoHovered().Box(holoRightHov),
            E<Button>().Class(StyleClassErisHoloButtonOpenRight).PseudoPressed().Box(holoRightPress),

            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenRight).PseudoNormal().Box(holoRightNorm),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenRight).PseudoHovered().Box(holoRightHov),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenRight).PseudoPressed().Box(holoRightPress),

            // Common aliases
            E<Button>().Class("HoloButton").PseudoNormal().Box(holoNorm),
            E<Button>().Class("HoloButton").PseudoHovered().Box(holoHov),
            E<Button>().Class("HoloButton").PseudoPressed().Box(holoPress),
            E<Button>().Class("HoloButton").PseudoDisabled().Box(holoDis),

            // Prevent Nanotrasen stylesheet modulation from overriding colors (especially green on toggle/pressed)
            E<Button>().Class(StyleClassErisHoloButton).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButton).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButton).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButton).PseudoDisabled().Prop(Control.StylePropertyModulateSelf, Color.White),

            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),

            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),

            E<Button>().Class(StyleClassErisHoloButtonOpenRight).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenRight).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenRight).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),

            E<ContainerButton>().Class(StyleClassErisHoloButton).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButton).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButton).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButton).PseudoDisabled().Prop(Control.StylePropertyModulateSelf, Color.White),

            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenBoth).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenBoth).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenBoth).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),

            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenLeft).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenLeft).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenLeft).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),

            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenRight).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenRight).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class(StyleClassErisHoloButtonOpenRight).PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.White),

            // Holo button label colors
            E<Button>().Class(StyleClassErisHoloButton).ParentOf(E<Label>()).FontColor(Color.FromHex("#00D4FF")),
            E<Button>().Class(StyleClassErisHoloButton).PseudoHovered().ParentOf(E<Label>()).FontColor(Color.White),
            E<Button>().Class(StyleClassErisHoloButton).PseudoPressed().ParentOf(E<Label>()).FontColor(Color.FromHex("#E0F7FA")),
            E<Button>().Class(StyleClassErisHoloButton).PseudoDisabled().ParentOf(E<Label>()).FontColor(Color.FromHex("#455A64")),

            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).ParentOf(E<Label>()).FontColor(Color.FromHex("#00D4FF")),
            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).PseudoHovered().ParentOf(E<Label>()).FontColor(Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenBoth).PseudoPressed().ParentOf(E<Label>()).FontColor(Color.White),

            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).ParentOf(E<Label>()).FontColor(Color.FromHex("#00D4FF")),
            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).PseudoHovered().ParentOf(E<Label>()).FontColor(Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenLeft).PseudoPressed().ParentOf(E<Label>()).FontColor(Color.White),

            E<Button>().Class(StyleClassErisHoloButtonOpenRight).ParentOf(E<Label>()).FontColor(Color.FromHex("#00D4FF")),
            E<Button>().Class(StyleClassErisHoloButtonOpenRight).PseudoHovered().ParentOf(E<Label>()).FontColor(Color.White),
            E<Button>().Class(StyleClassErisHoloButtonOpenRight).PseudoPressed().ParentOf(E<Label>()).FontColor(Color.White),

            // Legacy tag aliases for backward compatibility
            E<PanelContainer>().Class("ErisStyleInit").Panel(erisDefault),
            E<PanelContainer>().Class("ErisStyleDigitalInit").Panel(erisDigital),
        ];
    }

    private StyleBoxTexture CreateErisBox(string path, int patchMargin, int scale = 2)
    {
        var texture = ResCache.GetTexture(path);
        var box = new StyleBoxTexture
        {
            Texture = texture,
            TextureScale = new Vector2(scale, scale),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
        box.SetPatchMargin(StyleBox.Margin.All, patchMargin);
        return box;
    }

    private StyleBoxTexture CreateHoloButtonBox(string filename, int patchLeft, int patchTop, int patchRight, int patchBottom, int scale = 1)
    {
        var texture = ResCache.GetTexture("/Textures/Oxyd/erisported/UI/" + filename);
        var box = new StyleBoxTexture
        {
            Texture = texture,
            TextureScale = new Vector2(scale, scale),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
        box.PatchMarginLeft = patchLeft;
        box.PatchMarginTop = patchTop;
        box.PatchMarginRight = patchRight;
        box.PatchMarginBottom = patchBottom;
        box.SetPadding(StyleBox.Margin.All, 0);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8 * scale);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2 * scale);
        return box;
    }

    private StyleBoxTexture CreateHoloButtonBox(string filename, int patchMarginHorizontal = 6, int patchMarginVertical = 6, int scale = 1)
    {
        return CreateHoloButtonBox(filename, patchMarginHorizontal, patchMarginVertical, patchMarginHorizontal, patchMarginVertical, scale);
    }
}
