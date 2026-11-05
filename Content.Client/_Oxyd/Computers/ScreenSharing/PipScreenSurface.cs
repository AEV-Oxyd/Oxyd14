using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Registry of active <see cref="PipScreenSurface"/> controls indexed by device GUID.
/// Allows <see cref="PipMirrorViewport"/> instances to locate the live offscreen render texture
/// of an ancestor or open device screen without recursive widget parenting.
/// </summary>
public static class PipSurfaceRegistry
{
    private static readonly Dictionary<string, List<PipScreenSurface>> s_Surfaces =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string guid, PipScreenSurface surface)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return;

        if (!s_Surfaces.TryGetValue(guid, out var list))
        {
            list = new List<PipScreenSurface>(2);
            s_Surfaces[guid] = list;
        }

        if (!list.Contains(surface))
            list.Add(surface);
    }

    public static void Unregister(string guid, PipScreenSurface surface)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return;

        if (!s_Surfaces.TryGetValue(guid, out var list))
            return;

        list.Remove(surface);
        if (list.Count == 0)
            s_Surfaces.Remove(guid);
    }

    /// <summary>
    /// Returns true if any ancestor <see cref="PipScreenSurface"/> of <paramref name="start"/>
    /// represents <paramref name="deviceGuid"/>.
    /// </summary>
    public static bool IsDeviceInAncestorChain(Control start, string? deviceGuid)
    {
        return FindAncestorSurface(start, deviceGuid) != null;
    }

    /// <summary>
    /// Walks the UI parent chain from <paramref name="start"/> upward to locate an ancestor
    /// <see cref="PipScreenSurface"/> matching <paramref name="deviceGuid"/>.
    /// </summary>
    public static PipScreenSurface? FindAncestorSurface(Control start, string? deviceGuid)
    {
        if (string.IsNullOrWhiteSpace(deviceGuid))
            return null;

        for (var cur = start.Parent; cur != null; cur = cur.Parent)
        {
            if (cur is PipScreenSurface surface &&
                string.Equals(surface.DeviceGuid, deviceGuid, StringComparison.OrdinalIgnoreCase))
            {
                return surface;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the best <see cref="PipScreenSurface"/> for <paramref name="deviceGuid"/> relative
    /// to <paramref name="requester"/>: prefers the ancestor surface in the same UI hierarchy,
    /// falling back to any visible registered surface for that GUID.
    /// </summary>
    public static PipScreenSurface? ResolveSurface(Control requester, string? deviceGuid)
    {
        if (string.IsNullOrWhiteSpace(deviceGuid))
            return null;

        var ancestor = FindAncestorSurface(requester, deviceGuid);
        if (ancestor != null)
            return ancestor;

        if (s_Surfaces.TryGetValue(deviceGuid, out var list))
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var candidate = list[i];
                if (!candidate.Disposed && candidate.VisibleInTree)
                    return candidate;
            }

            if (list.Count > 0 && !list[^1].Disposed)
                return list[^1];
        }

        return null;
    }
}

/// <summary>
/// Wraps a device's screen content and captures its rendered output into a ping-pong pair of
/// offscreen <see cref="IRenderTexture"/>s whenever a <see cref="PipMirrorViewport"/> observes it.
/// Enables real-time Picture-in-Picture (PiP) and infinite Hall-of-Mirrors (Droste) cascades
/// without physical widget re-parenting.
/// </summary>
[Virtual]
public partial class PipScreenSurface : BoxContainer
{
    [Dependency] private IClyde _clyde = default!;

    private static int s_CaptureDepth;
    public static bool IsCapturingOffscreen => s_CaptureDepth > 0;

    private string? _deviceGuid;
    private IRenderTexture? _readTarget;
    private IRenderTexture? _writeTarget;
    private bool _hasValidReadFrame;
    private int _observedFramesRemaining;

    public string? DeviceGuid
    {
        get => _deviceGuid;
        set
        {
            if (string.Equals(_deviceGuid, value, StringComparison.OrdinalIgnoreCase))
                return;

            if (_deviceGuid != null)
                PipSurfaceRegistry.Unregister(_deviceGuid, this);

            _deviceGuid = value;

            if (_deviceGuid != null && !Disposed)
                PipSurfaceRegistry.Register(_deviceGuid, this);
        }
    }

    /// <summary>
    /// The most recently captured offscreen texture of this surface, or null if no frame has been captured yet.
    /// Double-buffered so a nested <see cref="PipMirrorViewport"/> can safely sample <see cref="ReadTexture"/>
    /// while this surface renders its next frame into the write buffer.
    /// </summary>
    public Texture? ReadTexture => _hasValidReadFrame ? _readTarget?.Texture : null;

    public PipScreenSurface()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    /// <summary>
    /// Marks this surface as actively observed by a <see cref="PipMirrorViewport"/> for the next N frames.
    /// </summary>
    public void RequestCaptureFrame(int frames = 3)
    {
        _observedFramesRemaining = Math.Max(_observedFramesRemaining, frames);
    }

    protected override void PreRenderChildren(ref ControlRenderArguments args)
    {
        base.PreRenderChildren(ref args);

        // When a PipMirrorViewport first attaches and we don't have a seeded read frame yet,
        // run 4 quick ping-pong passes so the nested Hall-of-Mirrors cascade is immediately populated.
        if (_observedFramesRemaining > 0 && s_CaptureDepth == 0 && !_hasValidReadFrame)
        {
            for (var i = 0; i < 4; i++)
            {
                if (!CaptureToTarget(args.Handle))
                    break;
            }
        }
    }

    protected override void PostRenderChildren(ref ControlRenderArguments args)
    {
        base.PostRenderChildren(ref args);

        if (_observedFramesRemaining <= 0 || s_CaptureDepth > 0)
            return;

        _observedFramesRemaining--;
        CaptureToTarget(args.Handle);
    }

    private bool CaptureToTarget(IRenderHandle renderHandle)
    {
        var rawSize = PixelSize;
        if (rawSize.X <= 4 || rawSize.Y <= 4)
            return false;

        var targetSize = Vector2i.ComponentMin(
            Vector2i.ComponentMax(rawSize, new Vector2i(32, 32)),
            new Vector2i(1024, 1024));

        EnsureRenderTargets(targetSize);
        if (_writeTarget == null || _readTarget == null)
            return false;

        s_CaptureDepth++;
        try
        {
            renderHandle.RenderInRenderTarget(_writeTarget, () =>
            {
                renderHandle.SetScissor(null);
                renderHandle.DrawingHandleScreen.SetTransform(Matrix3x2.Identity);
                UserInterfaceManager.RenderControl(renderHandle, this, Vector2i.Zero);
            }, Color.FromHex("#070B12"));
        }
        finally
        {
            s_CaptureDepth--;
        }

        (_readTarget, _writeTarget) = (_writeTarget, _readTarget);
        _hasValidReadFrame = true;
        return true;
    }

    private void EnsureRenderTargets(Vector2i size)
    {
        if (_readTarget != null && _readTarget.Size == size &&
            _writeTarget != null && _writeTarget.Size == size)
        {
            return;
        }

        DisposeRenderTargets();

        var format = new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb);
        var sampleParams = new TextureSampleParameters { Filter = true };
        var label = string.IsNullOrEmpty(_deviceGuid) ? "Unknown" : _deviceGuid;

        _readTarget = _clyde.CreateRenderTarget(size, format, sampleParams, $"PipSurface_{label}_Read");
        _writeTarget = _clyde.CreateRenderTarget(size, format, sampleParams, $"PipSurface_{label}_Write");
        _hasValidReadFrame = false;
    }

    private void DisposeRenderTargets()
    {
        _readTarget?.Dispose();
        _readTarget = null;
        _writeTarget?.Dispose();
        _writeTarget = null;
        _hasValidReadFrame = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_deviceGuid != null)
                PipSurfaceRegistry.Unregister(_deviceGuid, this);

            DisposeRenderTargets();
        }

        base.Dispose(disposing);
    }
}
