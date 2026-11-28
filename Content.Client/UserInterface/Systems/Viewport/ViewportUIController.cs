using System.Linq;
using Content.Client._Oxyd.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Systems.Viewport;

public sealed partial class ViewportUIController : UIController
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private OxTagController tags = default!;
    [Dependency] private IPlayerManager _playerMan = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;

    public static readonly Vector2i ViewportSize = (EyeManager.PixelsPerMeter * 21, EyeManager.PixelsPerMeter * 15);
    public const int ViewportHeight = 15;

    private readonly HashSet<object> _worldRenderPauseTokens = new();

    private MainViewport? Viewport => TryGetMainViewport();

    public bool IsWorldRenderPaused => _worldRenderPauseTokens.Count > 0;

    public override void Initialize()
    {
        _configurationManager.OnValueChanged(CCVars.ViewportMinimumWidth, _ => UpdateViewportRatio());
        _configurationManager.OnValueChanged(CCVars.ViewportMaximumWidth, _ => UpdateViewportRatio());
        _configurationManager.OnValueChanged(CCVars.ViewportWidth, _ => UpdateViewportRatio());
        _configurationManager.OnValueChanged(CCVars.ViewportVerticalFit, _ => UpdateViewportRatio());

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;
    }

    public override void FrameUpdate(FrameEventArgs e)
    {
        var viewport = Viewport;
        if (viewport == null)
        {
            return;
        }

        base.FrameUpdate(e);

        viewport.Viewport.RenderPaused = _worldRenderPauseTokens.Count > 0;
        viewport.Viewport.Eye = _eyeManager.CurrentEye;

        // verify that the current eye is not "null". Fuck IEyeManager.

        var ent = _playerMan.LocalEntity;
        if (_eyeManager.CurrentEye.Position != default || ent == null)
            return;

        _entMan.TryGetComponent(ent, out EyeComponent? eye);

        if (eye?.Eye == _eyeManager.CurrentEye
            && _entMan.GetComponent<TransformComponent>(ent.Value).MapID == MapId.Nullspace)
        {
            // nothing to worry about, the player is just in null space... actually that is probably a problem?
            return;
        }

        // Currently, this shouldn't happen. This likely happened because the main eye was set to null. When this
        // does happen it can create hard to troubleshoot bugs, so lets print some helpful warnings:
        Log.Warning($"Main viewport's eye is in nullspace (main eye is null?). Attached entity: {_entMan.ToPrettyString(ent.Value)}. Entity has eye comp: {eye != null}");
    }

    private void OnScreenLoad()
    {
        ClearWorldRenderPauses();
        ReloadViewport();
    }

    private void OnScreenUnload()
    {
        ClearWorldRenderPauses();
    }

    public MainViewport? TryGetMainViewport()
    {
        foreach (var ctrl in tags.getControls("Viewport"))
        {
            if (ctrl is MainViewport mv && !mv.Disposed)
                return mv;
        }

        return UIManager.ActiveScreen?.GetWidget<MainViewport>();
    }

    /// <summary>
    /// Computes the screen-space focus rectangle covering the entire left HUD panel (<c>LeftRect</c>),
    /// center <c>ViewportContainer</c>, and bottom hands/hotbar bar (<c>LowerBar</c>), leaving only
    /// the right-side <c>RightRect</c> (Chat and <c>StatPanel</c> action/status tabs) uncovered and reachable.
    /// </summary>
    public bool TryGetViewportFocusBounds(out UIBox2 bounds)
    {
        var rootSize = UIManager.RootControl.Size;

        foreach (var ctrl in tags.getControls("RightRect"))
        {
            if (!ctrl.Disposed && ctrl.IsInsideTree && ctrl.Size.X > 0f && ctrl.Size.Y > 0f && ctrl.GlobalPosition.X >= 64f)
            {
                var bottom = rootSize.Y;
                if (bottom <= 0f)
                {
                    foreach (var lower in tags.getControls("LowerBar"))
                    {
                        if (!lower.Disposed && lower.IsInsideTree)
                            bottom = MathF.Max(bottom, lower.GlobalPosition.Y + lower.Size.Y);
                    }
                }

                if (bottom >= 64f)
                {
                    bounds = new UIBox2(0f, 0f, ctrl.GlobalPosition.X, bottom);
                    return true;
                }
            }
        }

        var dockTarget = GetViewportDockTarget();
        if (dockTarget != null && dockTarget.Size.X >= 64f && dockTarget.Size.Y >= 64f)
        {
            var bottom = MathF.Max(rootSize.Y, dockTarget.GlobalPosition.Y + dockTarget.Size.Y);
            bounds = new UIBox2(0f, 0f, dockTarget.GlobalPosition.X + dockTarget.Size.X, bottom);
            return true;
        }

        bounds = default;
        return false;
    }
    public Control? GetViewportDockTarget()
    {
        foreach (var ctrl in tags.getControls("ViewportContainer"))
        {
            if (!ctrl.Disposed && ctrl.IsInsideTree && ctrl.Size.X > 0f && ctrl.Size.Y > 0f)
                return ctrl;
        }

        var viewport = TryGetMainViewport();
        if (viewport != null && !viewport.Disposed && viewport.IsInsideTree)
        {
            if (viewport.Parent is { } parent &&
                !parent.Disposed &&
                parent.IsInsideTree &&
                parent.Name == "ViewportContainer" &&
                parent.Size.X > 0f &&
                parent.Size.Y > 0f)
            {
                return parent;
            }

            if (viewport.Size.X > 0f && viewport.Size.Y > 0f)
                return viewport;
        }

        return null;
    }

    /// <summary>
    /// Reference-counted pause control for the main game world viewport.
    /// While at least one token holds a pause, <see cref="MainViewport"/> skips world rendering cleanly.
    /// </summary>
    public void SetWorldRenderPaused(object token, bool paused)
    {
        if (paused)
            _worldRenderPauseTokens.Add(token);
        else
            _worldRenderPauseTokens.Remove(token);

        SyncWorldRenderPauseState();
    }

    /// <summary>
    /// Releases all world render pause tokens immediately (e.g. on screen unload or player detach).
    /// </summary>
    public void ClearWorldRenderPauses()
    {
        _worldRenderPauseTokens.Clear();
        SyncWorldRenderPauseState();
    }

    private void SyncWorldRenderPauseState()
    {
        var viewport = Viewport;
        if (viewport != null)
            viewport.Viewport.RenderPaused = _worldRenderPauseTokens.Count > 0;
    }

    private void UpdateViewportRatio()
    {
        if (Viewport == null)
        {
            return;
        }

        var min = _configurationManager.GetCVar(CCVars.ViewportMinimumWidth);
        var max = _configurationManager.GetCVar(CCVars.ViewportMaximumWidth);
        var width = _configurationManager.GetCVar(CCVars.ViewportWidth);
        var verticalfit = _configurationManager.GetCVar(CCVars.ViewportVerticalFit) && _configurationManager.GetCVar(CCVars.ViewportStretch);

        if (verticalfit)
        {
            width = max;
        }
        else if (width < min || width > max)
        {
            width = CCVars.ViewportWidth.DefaultValue;
        }

        Viewport.Viewport.ViewportSize = (EyeManager.PixelsPerMeter * 17, EyeManager.PixelsPerMeter * 17);
        Viewport.UpdateCfg();
    }

    public void ReloadViewport()
    {
        if (Viewport == null)
        {
            return;
        }

        UpdateViewportRatio();
        Viewport.Viewport.HorizontalExpand = true;
        Viewport.Viewport.VerticalExpand = true;
        Viewport.Viewport.RenderPaused = _worldRenderPauseTokens.Count > 0;
        _eyeManager.MainViewport = Viewport.Viewport;
    }
}
