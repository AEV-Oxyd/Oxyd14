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
                !surface.Disposed &&
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
        if (ancestor != null && !ancestor.Disposed)
            return ancestor;

        if (s_Surfaces.TryGetValue(deviceGuid, out var list))
        {
            // Prefer top-level (non-nested) visible surfaces first if available
            for (var i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (!candidate.Disposed && candidate.VisibleInTree)
                    return candidate;
            }
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

    private static readonly Queue<IRenderTexture> s_DeferredDisposeQueue = new();
    private static int s_CaptureDepth;
    private static uint s_CurrentFrameId;
    private static uint s_LastGlobalCaptureFrameId;

    /// <summary>
    /// Minimum interval between steady-state background captures once the initial cascade bounces are seeded.
    /// </summary>
    private const float BackgroundCaptureIntervalSeconds = 0.10f;

    public static bool IsCapturingOffscreen => s_CaptureDepth > 0;

    public static void BeginUiFrame()
    {
        s_CurrentFrameId++;
        FlushDeferredDisposals();
    }

    /// <summary>
    /// Enqueues a render target for disposal outside the Clyde render pass so a texture handle
    /// is never deleted while draw batches referencing it are still queued in Clyde.
    /// </summary>
    public static void DeferDispose(IRenderTexture? target)
    {
        if (target != null)
            s_DeferredDisposeQueue.Enqueue(target);
    }

    /// <summary>
    /// Disposes all retired render targets when not inside an active render pass.
    /// </summary>
    public static void FlushDeferredDisposals()
    {
        if (s_CaptureDepth > 0)
            return;

        while (s_DeferredDisposeQueue.TryDequeue(out var target))
        {
            target.Dispose();
        }
    }

    private string? _deviceGuid;
    private IRenderTexture? _readTarget;
    private IRenderTexture? _writeTarget;
    private bool _hasValidReadFrame;
    private int _observedFramesRemaining;
    private int _dirtyPassesRemaining = 3;
    private float _timeSinceLastCapture;
    private Vector2i _lastCapturedSize;

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
            {
                PipSurfaceRegistry.Register(_deviceGuid, this);
                InvalidateCapture();
            }
        }
    }

    /// <summary>
    /// The most recently captured offscreen texture of this surface, or null if no frame has been captured yet.
    /// Double-buffered so a nested <see cref="PipMirrorViewport"/> can safely sample <see cref="ReadTexture"/>
    /// while this surface renders its next frame into the write buffer.
    /// </summary>
    public Texture? ReadTexture => _hasValidReadFrame && !Disposed ? _readTarget?.Texture : null;

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
        if (_observedFramesRemaining <= 0)
            _dirtyPassesRemaining = Math.Max(_dirtyPassesRemaining, 3);

        _observedFramesRemaining = Math.Max(_observedFramesRemaining, frames);
    }

    /// <summary>
    /// Marks the surface content as changed so the next 3 cascade bounces are captured promptly.
    /// </summary>
    public void InvalidateCapture(int cascadeBounces = 3)
    {
        _dirtyPassesRemaining = Math.Max(_dirtyPassesRemaining, cascadeBounces);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _timeSinceLastCapture += args.DeltaSeconds;
        FlushDeferredDisposals();
    }

    protected override void PostRenderChildren(ref ControlRenderArguments args)
    {
        base.PostRenderChildren(ref args);

        if (_observedFramesRemaining <= 0 || s_CaptureDepth > 0)
            return;

        _observedFramesRemaining--;

        // Stagger offscreen FBO captures so at most 1 surface captures in any single UI render pass.
        if (s_LastGlobalCaptureFrameId == s_CurrentFrameId)
            return;

        if (!_hasValidReadFrame || _lastCapturedSize != PixelSize)
            _dirtyPassesRemaining = Math.Max(_dirtyPassesRemaining, 3);

        if (_dirtyPassesRemaining <= 0 && _timeSinceLastCapture < BackgroundCaptureIntervalSeconds)
            return;

        if (CaptureToTarget(args.Handle))
        {
            s_LastGlobalCaptureFrameId = s_CurrentFrameId;
            _timeSinceLastCapture = 0f;
            _lastCapturedSize = PixelSize;
            if (_dirtyPassesRemaining > 0)
                _dirtyPassesRemaining--;
        }
    }

    private bool CaptureToTarget(IRenderHandle renderHandle)
    {
        var rawSize = PixelSize;
        if (rawSize.X <= 4 || rawSize.Y <= 4)
            return false;

        var targetSize = Vector2i.ComponentMin(
            Vector2i.ComponentMax(rawSize, new Vector2i(32, 32)),
            new Vector2i(1024, 1024));

        EnsureWriteTarget(targetSize);
        if (_writeTarget == null)
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

        if (_writeTarget != null && _writeTarget.Size != targetSize)
        {
            DeferDispose(_writeTarget);
            _writeTarget = null;
        }

        return true;
    }

    private void EnsureWriteTarget(Vector2i size)
    {
        if (_writeTarget != null && _writeTarget.Size == size)
            return;

        if (_writeTarget != null)
        {
            DeferDispose(_writeTarget);
            _writeTarget = null;
        }

        var format = new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb);
        var sampleParams = new TextureSampleParameters { Filter = true };
        var label = string.IsNullOrEmpty(_deviceGuid) ? "Unknown" : _deviceGuid;

        _writeTarget = _clyde.CreateRenderTarget(size, format, sampleParams, $"PipSurface_{label}_Buffer");
    }

    private void DisposeRenderTargets()
    {
        DeferDispose(_readTarget);
        _readTarget = null;
        DeferDispose(_writeTarget);
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
