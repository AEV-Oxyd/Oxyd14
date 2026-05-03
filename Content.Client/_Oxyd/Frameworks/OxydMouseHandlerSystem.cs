using System.Linq;
using Content.Client.Gameplay;
using Content.Client.Hands.Systems;
using Content.Client.Interaction;
using Content.Client.Construction;
using Content.Client.Tabletop;
using Content.Client.UserInterface;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Framework;
// global event
// raised when pressed the mouse down
public class MouseDownEvent
{
    public EntityUid clickedOn;
    public EntityUid user;
    public EntityCoordinates clickCoords;
}

// targeted event , raised on user, used item and the target(if any)
public class UsingMouseDownEvent
{
    public EntityUid clickedOn;
    public List<EntityUid> holding = new();
    public EntityUid user;
    public EntityCoordinates clickCoords;
    public EntityUid activeHeld;
}
// raised when the mouse gets released
public class MouseUpEvent
{
    public EntityUid clickedOn;
    public EntityUid user;
    public EntityCoordinates clickCoords;
}

// targeted event , raised on user, used item and the target(if any)
public class UsingMouseUpEvent
{
    public EntityUid clickedOn;
    public List<EntityUid> holding = new();
    public EntityUid user;
    public EntityCoordinates clickCoords;
    public EntityUid activeHeld;
}
// raised for every tile-change(on player, on held item if any, on crossed entities)
public class MouseCrossEvent
{
    public EntityUid crossed;
    public EntityUid user;
    public EntityUid activeHolding;
    public List<EntityUid> holding = new();
    public MapCoordinates clickCoords;
}
// raised when clicking with alt held
public class MouseAltClickEvent
{
    public EntityUid clickedOn;
    public EntityUid user;
    public EntityCoordinates clickCoords;
}

public class MouseAltClickedEvent
{
    public EntityUid user;
    public EntityCoordinates clickCoords;
}


/// <summary>
/// This handles...
/// </summary>
public sealed class OxydMouseHandlingSystem : EntitySystem
{
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    public bool mousedDown = false;
    public bool altDown = false;
    // will stop the click event being replicated to  the normal SS14 keybindigns handler
    // used to stop double-interactions when our own pipeline returns a true to block it
    // can't really stop it from its proper place since its in engine
    // accesed in HandsUIController
    public bool blockTransmit = false;
    public EntityUid crossed = EntityUid.Invalid;
    /// <inheritdoc/>
    public override void Initialize()
    {
        CommandBinds.Builder
            .BindBefore(
                EngineKeyFunctions.Use,
                new PointerStateInputCmdHandler(HandleMouseEnabled, HandleMouseDisabled, false),
                typeof(SharedInteractionSystem),
                typeof(ActionUIController),
                typeof(DragDropSystem),
                typeof(ConstructionSystem),
                typeof(TabletopSystem)
            )
            .Bind(ContentKeyFunctions.AltInteractionMode, InputCmdHandler.FromDelegate(HandleAltEnabled, HandleAltDisabled, false, false))
            .Register<OxydMouseHandlingSystem>();

        _inputManager.UIKeyBindStateChanged += OnUIKeyBindStateChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _inputManager.UIKeyBindStateChanged -= OnUIKeyBindStateChanged;
    }

    private bool OnUIKeyBindStateChanged(BoundKeyEventArgs args)
    {
        if (args.Function != EngineKeyFunctions.Use)
            return false;

        var control = _uiManager.MouseGetControl(args.PointerLocation);
        var entity = FindEntityForControl(control, out var isViewport);

        if (isViewport)
            return false;

        var session = _playerManager.LocalSession;
        if (session == null)
            return false;

        var coords = _eyeManager.PixelToMap(args.PointerLocation.Position);
        Log.Debug($"OnUiKey gave coords {coords}");

        /*
        if (coords.MapId == MapId.Nullspace && entity != null)
        {
            coords = _transformSystem.GetMapCoordinates(entity.Value);
        }

        if (coords.MapId == MapId.Nullspace && session.AttachedEntity != null)
        {
            coords = _transformSystem.GetMapCoordinates(session.AttachedEntity.Value);
        }
        */

        if (coords.MapId == MapId.Nullspace)
            return false;

        var entCoords = _transformSystem.ToCoordinates(coords);
        var blockval = false;
        if (args.State == BoundKeyState.Down)
        {
            blockval = HandleMouseEnabled(session, entCoords, entity ?? EntityUid.Invalid);
        }
        else if (args.State == BoundKeyState.Up)
        {
            blockval = HandleMouseDisabled(session, entCoords, entity ?? EntityUid.Invalid);
        }
        Log.Debug($"BlockTransmit set to {blockval}");
        blockTransmit = blockval;
        return blockval;
    }

    private EntityUid? FindEntityForControl(Control? control, out bool isViewport)
    {
        isViewport = false;
        if (control == null)
            return null;

        if (control is IViewportControl)
        {
            isViewport = true;
            return null;
        }

        if (control is IEntityControl entityControl)
        {
            return entityControl.UiEntity;
        }

        if (control is SpriteView spriteView)
        {
            return spriteView.Entity;
        }

        return FindEntityForControl(control.Parent, out isViewport);
    }

    public void HandleAltEnabled(ICommonSession? session)
    {
        Log.Error($"Alt enabled");
        altDown = true;
    }

    public void HandleAltDisabled(ICommonSession? session)
    {
        Log.Error($"Alt disabled");
        altDown = false;
    }

    public bool HandleMouseEnabled(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session is null)
            return false;
        if (session.AttachedEntity is null)
            return false;
        if (!_timing.IsFirstTimePredicted)
            return false;
        Log.Debug($"Mouse enabled");
        var mouseData = EnsureComp<OxydMouseDataComponent>(session.AttachedEntity.Value);
        mouseData.lastClicked = uid;
        mouseData.mouseMap = _transformSystem.ToMapCoordinates(coords);
        mouseData.mouseEntity = coords;
        var ev = new SyncedEntityEventArgs<MouseDownEvent>()
        {
            self = new MouseDownEvent()
            {
                clickedOn = mouseData.lastClicked,
                user = session.AttachedEntity.Value,
                clickCoords = mouseData.mouseEntity,
            }
        };
        RaiseLocalEvent(ev);
        ev.Execute();
        if (altDown)
        {
            //Log.Debug($"Raising alt click event");
            var evAlt = new SyncedEntityEventArgs<MouseAltClickEvent>()
            {
                self = new MouseAltClickEvent()
                {
                    clickedOn = mouseData.lastClicked,
                    user = session.AttachedEntity.Value,
                    clickCoords = mouseData.mouseEntity,
                }
            };
            RaiseLocalEvent(evAlt);
            evAlt.Execute();

            var evAltClicked = new SyncedEntityEventArgs<MouseAltClickedEvent>()
            {
                self = new MouseAltClickedEvent()
                {
                    user = session.AttachedEntity.Value,
                    clickCoords = mouseData.mouseEntity,
                }
            };
            RaiseLocalEvent(mouseData.lastClicked, evAltClicked);
            evAltClicked.Execute();
        }
        var active = _handsSystem.GetActiveHandEntity();
        mousedDown = true;
        if (active is null)
            return false;
        var heldItems = _handsSystem.EnumerateHeld(session.AttachedEntity.Value).ToList();
        var targetedEvent = new SyncedEntityEventArgs<UsingMouseDownEvent>()
        {
            self = new UsingMouseDownEvent()
            {
                clickedOn = mouseData.lastClicked,
                user = session.AttachedEntity.Value,
                holding = heldItems,
                clickCoords = mouseData.mouseEntity,
                activeHeld = active.Value,
            }
        };
        RaiseLocalEvent(active.Value, targetedEvent);
        if(uid != active.Value)
            RaiseLocalEvent(uid, targetedEvent);
        RaiseLocalEvent(session.AttachedEntity.Value, targetedEvent);
        return targetedEvent.Execute();
    }

    public bool HandleMouseDisabled(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session is null)
            return false;
        if (session.AttachedEntity is null)
            return false;
        if (!_timing.IsFirstTimePredicted)
            return false;
        var mouseData = EnsureComp<OxydMouseDataComponent>(session.AttachedEntity.Value);
        mouseData.lastClicked = uid;
        mouseData.mouseMap = _transformSystem.ToMapCoordinates(coords);
        var evUp = new SyncedEntityEventArgs<MouseUpEvent>()
        {
            self = new MouseUpEvent()
            {
                clickedOn = mouseData.lastClicked,
                user = session.AttachedEntity.Value,
                clickCoords = mouseData.mouseEntity,
            }
        };
        RaiseLocalEvent(evUp);
        evUp.Execute();
        var active = _handsSystem.GetActiveHandEntity();
        mousedDown = false;
        if (active is null)
            return false;
        var heldItems = _handsSystem.EnumerateHeld(session.AttachedEntity.Value).ToList();
        var targetedEvent = new SyncedEntityEventArgs<UsingMouseUpEvent>()
        {
            self = new UsingMouseUpEvent()
            {
                clickedOn = mouseData.lastClicked,
                user = session.AttachedEntity.Value,
                holding = heldItems,
                clickCoords = mouseData.mouseEntity,
                activeHeld = active.Value,
            }
        };
        RaiseLocalEvent(active.Value, targetedEvent);
        if(uid != active.Value)
            RaiseLocalEvent(uid, targetedEvent);
        RaiseLocalEvent(session.AttachedEntity.Value, targetedEvent);
        return targetedEvent.Execute();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_stateManager.CurrentState is not GameplayState gameplayState)
            return;
        if (_playerManager.LocalEntity is not { } localEntity)
            return;

        var mouseScreenPos = _inputManager.MouseScreenPosition;
        var mouseData = EnsureComp<OxydMouseDataComponent>(localEntity);
        var mousePos = _eyeManager.PixelToMap(mouseScreenPos);
        if (mousePos.MapId == MapId.Nullspace)
        {
            mousePos = _eyeManager.PixelToMap(mouseScreenPos.Position);
        }

        var hoveredControl = _uiManager.CurrentlyHovered;
        var ent = FindEntityForControl(hoveredControl, out var isViewportUpdate);

        if (mousePos.MapId == MapId.Nullspace && ent != null)
        {
            mousePos = _transformSystem.GetMapCoordinates(ent.Value);
        }

        if (mousePos.MapId == MapId.Nullspace)
        {
            mousePos = _transformSystem.GetMapCoordinates(localEntity);
        }

        mouseData.mouseMap = mousePos;
        if (!mousedDown)
            return;

        var held = _handsSystem.GetActiveHandEntity();
        var heldList = _handsSystem.EnumerateHeld(localEntity).ToList();

        if (isViewportUpdate)
        {
            ent = gameplayState.GetClickedEntity(mousePos);
        }
        if (ent is not null)
        {
            mouseData.lastHovered = ent.Value;
        }

        var ev = new SyncedEntityEventArgs<MouseCrossEvent>()
        {
            self = new MouseCrossEvent()
            {
                user = localEntity,
                clickCoords = mousePos,
                holding = heldList,
            }
        };
        if (ent is not null)
            ev.self.crossed = ent.Value;
        if (held is not null)
        {
            ev.self.activeHolding = held.Value;
            RaiseLocalEvent(held.Value, ev);
        }
        RaiseLocalEvent(ev);
        if (ent is not null)
            RaiseLocalEvent(ent.Value, ev);
        ev.Execute();

    }
}
