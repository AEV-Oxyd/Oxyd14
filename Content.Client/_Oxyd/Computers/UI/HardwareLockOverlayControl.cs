using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.UI;

/// <summary>
/// Full-viewport semi-transparent hardware lock overlay with a centered locked keyhole icon.
/// Inherits <see cref="ContentControl"/> so it blocks clicks cleanly both locally and over PiP remote screens.
/// </summary>
public sealed class HardwareLockOverlayControl : ContentControl
{
    private const string LockIconPath = "/Textures/Interface/VerbIcons/lock.svg.192dpi.png";

    private readonly ContentPanelContainer _badgePanel;
    private readonly ContentLabel _subtitleLabel;

    public HardwareLockOverlayControl()
    {
        Name = "HardwareLockOverlay";
        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;

        var resCache = IoCManager.Resolve<IResourceCache>();
        Texture? lockTex = null;
        if (resCache.TryGetResource<TextureResource>(new ResPath(LockIconPath), out var texRes))
            lockTex = texRes.Texture;

        _badgePanel = new ContentPanelContainer
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Stop,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0B1520E6"),
                BorderColor = Color.FromHex("#FF5555"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 18,
                ContentMarginRightOverride = 18,
                ContentMarginTopOverride = 14,
                ContentMarginBottomOverride = 14
            }
        };

        var col = new ContentBoxContainer
        {
            Orientation = ContentBoxContainer.LayoutOrientation.Vertical,
            HorizontalAlignment = HAlignment.Center,
            SeparationOverride = 6
        };

        var iconRect = new ContentTextureRect
        {
            Name = "HardwareLockIcon",
            Texture = lockTex,
            DisplaySize = new Vector2(44, 44),
            Modulate = Color.FromHex("#FF5555"),
            HorizontalAlignment = HAlignment.Center
        };

        var titleLabel = new ContentLabel
        {
            Name = "HardwareLockTitle",
            Text = "HARDWARE LOCKED",
            WordWrap = false,
            HorizontalAlignment = HAlignment.Center,
            FontColorOverride = Color.FromHex("#FF6B6B")
        };

        _subtitleLabel = new ContentLabel
        {
            Name = "HardwareLockSubtitle",
            Text = "Swipe authorized ID card to unlock",
            HorizontalAlignment = HAlignment.Center,
            FontColorOverride = Color.FromHex("#D8EEF5")
        };

        col.AddChild(iconRect);
        col.AddChild(titleLabel);
        col.AddChild(_subtitleLabel);
        _badgePanel.AddChild(col);
        AddChild(_badgePanel);
    }

    public void SetSubtitle(string text)
    {
        _subtitleLabel.Text = text;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (Visible)
            args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (Visible)
            args.Handle();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var maxBadge = new Vector2(
            float.IsFinite(availableSize.X) ? MathF.Max(0f, availableSize.X - 16f) : availableSize.X,
            float.IsFinite(availableSize.Y) ? MathF.Max(0f, availableSize.Y - 16f) : availableSize.Y);
        _badgePanel.Measure(maxBadge);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var desired = _badgePanel.DesiredSize;
        var w = MathF.Min(MathF.Max(0f, finalSize.X - 16f), desired.X);
        var h = MathF.Min(MathF.Max(0f, finalSize.Y - 16f), desired.Y);
        var x = MathF.Max(0f, (finalSize.X - w) / 2f);
        var y = MathF.Max(0f, (finalSize.Y - h) / 2f);
        _badgePanel.Arrange(UIBox2.FromDimensions(new Vector2(x, y), new Vector2(w, h)));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, Color.FromHex("#060B12A8"), filled: true);
    }
}
