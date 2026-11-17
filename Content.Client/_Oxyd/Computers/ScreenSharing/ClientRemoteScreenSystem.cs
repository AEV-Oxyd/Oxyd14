using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.ScreenSharing;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._Oxyd.Computers.ScreenSharing;

public sealed partial class ClientRemoteScreenSystem : SharedRemoteScreenSystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private LayoutContainer? _offscreenStash;
    private readonly HashSet<RemoteScreenView.RemoteBuiTarget> _allTargets = new();
    private readonly Dictionary<EntityUid, RemoteScreenView.RemoteBuiTarget> _activeTargetsByUid = new();
    private readonly Dictionary<BaseWindow, RemoteScreenView.RemoteBuiTarget> _activeTargetsByWindow = new();

    private readonly Dictionary<string, List<PipScreenSurface>> _pipSurfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<IRenderTexture> _deferredDisposeQueue = new();
    private readonly HashSet<Control> _activeCaptureControls = new();
    private readonly HashSet<string> _activeCaptureGuids = new(StringComparer.OrdinalIgnoreCase);
    private int _captureDepth;
    private IRenderHandle? _captureRenderHandle;
    private Control? _captureControl;
    private Action? _captureAction;

    public bool IsCapturingOffscreen => _captureDepth > 0;
    public int CaptureDepth => _captureDepth;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UserInterfaceComponent, ActivateInWorldEvent>(
            OnActivateInWorld,
            before: new[] { typeof(ActivatableUISystem), typeof(SharedInteractionSystem) });
        SubscribeLocalEvent<UserInterfaceComponent, UseInHandEvent>(
            OnUseInHand,
            before: new[] { typeof(ActivatableUISystem) });
        SubscribeLocalEvent<EmbeddedComputerHostComponent, EmbeddedComputerHostActivatedEvent>(OnHostActivated);
        SubscribeLocalEvent<UserInterfaceComponent, GetVerbsEvent<ActivationVerb>>(
            OnGetActivationVerbs,
            before: new[] { typeof(SharedInteractionSystem) });
        SubscribeNetworkEvent<RemoteScreenBringToFrontEvent>(OnRemoteScreenBringToFront);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    public LayoutContainer? GetExistingOffscreenStash()
    {
        if (_offscreenStash != null && !_offscreenStash.Disposed && _offscreenStash.Parent != null)
            return _offscreenStash;

        return null;
    }

    public LayoutContainer GetOrCreateOffscreenStash(IUserInterfaceManager? uiManager = null)
    {
        if (_offscreenStash != null && !_offscreenStash.Disposed && _offscreenStash.Parent != null)
            return _offscreenStash;

        var mgr = uiManager ?? _uiManager;
        _offscreenStash = new LayoutContainer
        {
            Name = "RemoteOffscreenStash",
            RectClipContent = true,
            SetSize = new Vector2(2000, 2000)
        };

        mgr.WindowRoot.AddChild(_offscreenStash);
        LayoutContainer.SetPosition(_offscreenStash, new Vector2(-30000, -30000));
        return _offscreenStash;
    }

    public void RegisterTarget(RemoteScreenView.RemoteBuiTarget target)
    {
        _allTargets.Add(target);
    }

    public void UnregisterTarget(RemoteScreenView.RemoteBuiTarget target)
    {
        _allTargets.Remove(target);
    }

    public void RegisterTargetUid(EntityUid uid, RemoteScreenView.RemoteBuiTarget target)
    {
        _activeTargetsByUid[uid] = target;
    }

    public void UnregisterTargetUid(EntityUid uid, RemoteScreenView.RemoteBuiTarget target)
    {
        if (_activeTargetsByUid.TryGetValue(uid, out var existing) && existing == target)
            _activeTargetsByUid.Remove(uid);
    }

    public void RegisterTargetWindow(BaseWindow window, RemoteScreenView.RemoteBuiTarget target)
    {
        _activeTargetsByWindow[window] = target;
    }

    public void UnregisterTargetWindow(BaseWindow window, RemoteScreenView.RemoteBuiTarget target)
    {
        if (_activeTargetsByWindow.TryGetValue(window, out var existing) && existing == target)
            _activeTargetsByWindow.Remove(window);
    }

    public bool TryGetForEntity(EntityUid uid, out RemoteScreenView.RemoteBuiTarget target)
    {
        return _activeTargetsByUid.TryGetValue(uid, out target!);
    }

    public bool TryBringTargetToScreen(EntityUid uid)
    {
        if (_activeTargetsByUid.TryGetValue(uid, out var target) && !target.IsPoppedOut)
        {
            target.PullWindowOnScreen(bringToFront: true);
            return true;
        }

        return false;
    }

    public bool TryHandleWindowClose(BaseWindow window)
    {
        if (!_activeTargetsByWindow.TryGetValue(window, out var target))
            return false;

        return target.TryInterceptWindowClose(window);
    }

    private void SyncAllTargets()
    {
        if (_allTargets.Count == 0)
            return;

        var snapshot = new List<RemoteScreenView.RemoteBuiTarget>(_allTargets);
        for (var i = 0; i < snapshot.Count; i++)
        {
            snapshot[i].SyncAttachment();
        }
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        ClearClientTargetTracking();
    }

    private void ClearClientTargetTracking()
    {
        if (_allTargets.Count > 0)
        {
            var snapshot = new List<RemoteScreenView.RemoteBuiTarget>(_allTargets);
            for (var i = 0; i < snapshot.Count; i++)
                snapshot[i].Dispose();
        }

        _allTargets.Clear();
        _activeTargetsByUid.Clear();
        _activeTargetsByWindow.Clear();
        if (_offscreenStash != null && !_offscreenStash.Disposed)
        {
            _offscreenStash.RemoveAllChildren();
            _offscreenStash.Parent?.RemoveChild(_offscreenStash);
            _offscreenStash.Dispose();
        }
        _offscreenStash = null;
    }

    public void RegisterPipSurface(string guid, PipScreenSurface surface)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return;

        if (!_pipSurfaces.TryGetValue(guid, out var list))
        {
            list = new List<PipScreenSurface>(2);
            _pipSurfaces[guid] = list;
        }

        if (!list.Contains(surface))
            list.Add(surface);
    }

    public void UnregisterPipSurface(string guid, PipScreenSurface surface)
    {
        if (string.IsNullOrWhiteSpace(guid) || !_pipSurfaces.TryGetValue(guid, out var list))
            return;

        list.Remove(surface);
        if (list.Count == 0)
            _pipSurfaces.Remove(guid);
    }

    public PipScreenSurface? ResolvePipSurface(Control requester, string? deviceGuid)
    {
        if (string.IsNullOrWhiteSpace(deviceGuid))
            return null;

        var ancestor = PipSurfaceRegistry.FindAncestorSurface(requester, deviceGuid);
        if (ancestor != null && !ancestor.Disposed && ancestor.ReadTexture != null)
            return ancestor;

        if (_pipSurfaces.TryGetValue(deviceGuid, out var list))
        {
            for (var i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (!candidate.Disposed && candidate != requester && candidate.ReadTexture != null)
                    return candidate;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (!candidate.Disposed && candidate.VisibleInTree)
                    return candidate;
            }
        }

        if (ancestor != null && !ancestor.Disposed)
            return ancestor;

        return null;
    }

    public void InvalidatePipDevice(string? deviceGuid, int cascadeBounces = 3)
    {
        if (string.IsNullOrWhiteSpace(deviceGuid) || !_pipSurfaces.TryGetValue(deviceGuid, out var list))
            return;

        for (var i = 0; i < list.Count; i++)
        {
            var surface = list[i];
            if (!surface.Disposed)
                surface.InvalidateCapture(cascadeBounces);
        }
    }

    public void BeginPipUiFrame()
    {
        _captureDepth = 0;
        _activeCaptureControls.Clear();
        _activeCaptureGuids.Clear();
        FlushDeferredPipDisposals();
    }

    public void DeferPipDispose(IRenderTexture? target)
    {
        if (target != null)
            _deferredDisposeQueue.Enqueue(target);
    }

    public void FlushDeferredPipDisposals()
    {
        if (_captureDepth > 0)
            return;

        while (_deferredDisposeQueue.TryDequeue(out var target))
        {
            target.Dispose();
        }
    }

    public bool CanCapturePip(Control control, string? guid)
    {
        if (_captureDepth >= PipScreenSurface.MaxCaptureDepth)
            return false;

        if (_activeCaptureControls.Contains(control))
            return false;

        if (!string.IsNullOrEmpty(guid) && _activeCaptureGuids.Contains(guid))
            return false;

        return true;
    }

    public void RenderPipToTarget(IRenderHandle renderHandle, IRenderTexture writeTarget, Control control, string? captureGuid)
    {
        _captureAction ??= OnPipCaptureRender;
        var addedControl = _activeCaptureControls.Add(control);
        var addedGuid = !string.IsNullOrEmpty(captureGuid) && _activeCaptureGuids.Add(captureGuid);
        var prevModulate = renderHandle.DrawingHandleScreen.Modulate;
        var prevTransform = renderHandle.DrawingHandleScreen.GetTransform();

        var prevHandle = _captureRenderHandle;
        var prevControl = _captureControl;
        _captureRenderHandle = renderHandle;
        _captureControl = control;

        _captureDepth++;
        try
        {
            renderHandle.RenderInRenderTarget(writeTarget, _captureAction, Color.FromHex("#070B12"));
        }
        finally
        {
            _captureRenderHandle = prevHandle;
            _captureControl = prevControl;

            renderHandle.DrawingHandleScreen.Modulate = prevModulate;
            renderHandle.DrawingHandleScreen.SetTransform(prevTransform);
            _captureDepth--;
            if (addedGuid && !string.IsNullOrEmpty(captureGuid))
                _activeCaptureGuids.Remove(captureGuid);
            if (addedControl)
                _activeCaptureControls.Remove(control);
        }
    }

    private void OnPipCaptureRender()
    {
        if (_captureRenderHandle == null || _captureControl == null)
            return;

        _captureRenderHandle.SetScissor(null);
        _captureRenderHandle.DrawingHandleScreen.Modulate = Color.White;
        _captureRenderHandle.DrawingHandleScreen.SetTransform(Matrix3x2.Identity);
        _captureControl.UserInterfaceManager.RenderControl(_captureRenderHandle, _captureControl, Vector2i.Zero);
    }

    public void NotifyLocalWindowState(NetEntity targetEntity, bool isOpenOnScreen)
    {
        if (targetEntity == NetEntity.Invalid)
            return;

        RaiseNetworkEvent(new RemoteScreenLocalWindowStateEvent(targetEntity, isOpenOnScreen));
    }

    private bool TryBringRemoteTargetToScreen(EntityUid uid)
    {
        var targetUid = uid;
        if (TryComp<EmbeddedComputerHostComponent>(uid, out var host) &&
            host.ComputerEntity is { } compUid && Exists(compUid))
        {
            targetUid = compUid;
        }

        if (TryBringTargetToScreen(targetUid))
            return true;

        if (TryGetForEntity(targetUid, out var target))
        {
            target.PullWindowOnScreen(bringToFront: true);
            return true;
        }

        return false;
    }

    private void OnRemoteScreenBringToFront(RemoteScreenBringToFrontEvent ev)
    {
        if (TryGetEntity(ev.TargetEntity, out var targetUid))
            TryBringRemoteTargetToScreen(targetUid.Value);
    }

    private void OnActivateInWorld(EntityUid uid, UserInterfaceComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || args.User != _player.LocalEntity)
            return;

        if (TryBringRemoteTargetToScreen(uid))
            args.Handled = true;
    }

    private void OnUseInHand(EntityUid uid, UserInterfaceComponent comp, UseInHandEvent args)
    {
        if (args.Handled || args.User != _player.LocalEntity)
            return;

        if (TryBringRemoteTargetToScreen(uid))
            args.Handled = true;
    }

    private void OnHostActivated(Entity<EmbeddedComputerHostComponent> ent, ref EmbeddedComputerHostActivatedEvent args)
    {
        if (args.User != _player.LocalEntity)
            return;

        TryBringRemoteTargetToScreen(ent.Owner);
    }

    private void OnGetActivationVerbs(EntityUid uid, UserInterfaceComponent comp, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var targetUid = uid;
        if (TryComp<EmbeddedComputerHostComponent>(uid, out var host) &&
            host.ComputerEntity is { } compUid && Exists(compUid))
        {
            targetUid = compUid;
        }

        if (TryGetForEntity(targetUid, out var target) && !target.IsPoppedOut)
        {
            var verb = new ActivationVerb
            {
                Text = Loc.GetString("ui-verb-toggle-open"),
                Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/open.svg.192dpi.png")),
                Act = () => target.PullWindowOnScreen()
            };
            args.Verbs.Add(verb);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        BeginPipUiFrame();
        SyncAllTargets();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ClearClientTargetTracking();
        _pipSurfaces.Clear();
        FlushDeferredPipDisposals();
    }
}
