using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Picture-in-Picture (PiP) viewport control that renders the live offscreen texture of an
/// ancestor or open <see cref="PipScreenSurface"/>.
/// When two devices share screens to each other (or a device views itself), this control samples
/// the double-buffered texture of the ancestor surface to produce a natural infinite "Hall of Mirrors"
/// (Droste) cascade without recursive widget parenting.
/// </summary>
public sealed class PipMirrorViewport : BoxContainer
{
    private string? _targetGuid;

    public PipMirrorViewport()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
    }

    public void SetTarget(string? targetGuid, string? deviceName = null)
    {
        var changed = !string.Equals(_targetGuid, targetGuid, StringComparison.OrdinalIgnoreCase);
        _targetGuid = targetGuid;

        var surface = PipSurfaceRegistry.ResolveSurface(this, _targetGuid);
        surface?.RequestCaptureFrame(4);
        if (changed)
            surface?.InvalidateCapture(3);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var surface = PipSurfaceRegistry.ResolveSurface(this, _targetGuid);
        surface?.RequestCaptureFrame(4);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = new Vector2(PixelSize.X, PixelSize.Y);
        if (size.X <= 4 || size.Y <= 4)
            return;

        handle.DrawRect(new UIBox2(Vector2.Zero, size), Color.FromHex("#050910"));

        var surface = PipSurfaceRegistry.ResolveSurface(this, _targetGuid);
        surface?.RequestCaptureFrame(4);

        var inset = new UIBox2(
            new Vector2(2f, 2f),
            new Vector2(Math.Max(2f, size.X - 2f), Math.Max(2f, size.Y - 2f)));

        if (surface?.ReadTexture is { } tex)
        {
            // Subtle phosphor tint per feedback bounce gives a realistic CRT/LCD Hall-of-Mirrors cascade
            handle.DrawTextureRect(tex, inset, new Color(0.92f, 0.97f, 1.0f, 1.0f));
        }

        handle.DrawRect(inset, Color.FromHex("#00D4FF55"), filled: false);
    }
}
