using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
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
    private string? _deviceName;
    private readonly BoxContainer _fallbackOverlay;
    private readonly Label _statusLabel;

    public PipMirrorViewport()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;

        _fallbackOverlay = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center
        };

        _statusLabel = new Label
        {
            Text = "SYNCHRONIZING PIP DISPLAY BUFFER...",
            FontColorOverride = Color.FromHex("#00D4FF"),
            HorizontalAlignment = HAlignment.Center
        };

        _fallbackOverlay.AddChild(_statusLabel);
        AddChild(_fallbackOverlay);
    }

    public void SetTarget(string? targetGuid, string? deviceName = null)
    {
        _targetGuid = targetGuid;
        _deviceName = deviceName;

        var surface = PipSurfaceRegistry.ResolveSurface(this, _targetGuid);
        surface?.RequestCaptureFrame(4);

        var label = string.IsNullOrEmpty(_deviceName)
            ? $"[{_targetGuid ?? "REMOTE"}]"
            : $"{_deviceName} [{_targetGuid}]";
        _statusLabel.Text = $"PIP FEEDBACK LOOP ACTIVE: {label}";
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var surface = PipSurfaceRegistry.ResolveSurface(this, _targetGuid);
        surface?.RequestCaptureFrame(4);
        _fallbackOverlay.Visible = surface?.ReadTexture == null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = new Vector2(PixelSize.X, PixelSize.Y);
        if (size.X <= 4 || size.Y <= 4)
            return;

        handle.DrawRect(new UIBox2(Vector2.Zero, size), Color.FromHex("#050910"));

        var surface = PipSurfaceRegistry.ResolveSurface(this, _targetGuid);
        surface?.RequestCaptureFrame(4);

        if (surface?.ReadTexture is { } tex)
        {
            _fallbackOverlay.Visible = false;

            var inset = new UIBox2(
                new Vector2(2f, 2f),
                new Vector2(Math.Max(2f, size.X - 2f), Math.Max(2f, size.Y - 2f)));

            // Subtle phosphor tint per feedback bounce gives a realistic CRT/LCD Hall-of-Mirrors cascade
            handle.DrawTextureRect(tex, inset, new Color(0.92f, 0.97f, 1.0f, 1.0f));
            handle.DrawRect(inset, Color.FromHex("#00D4FF55"), filled: false);
        }
        else
        {
            _fallbackOverlay.Visible = true;
            base.Draw(handle);
        }
    }
}
