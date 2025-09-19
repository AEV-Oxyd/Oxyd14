using System.Linq;
using Content.Client.Gameplay;
using Content.Client.Hands.Systems;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.Framework;
// global event
// raised when pressed the mouse down
public class MouseDownEvent : EntityEventArgs
{
    public EntityUid clickedOn;
    public EntityUid user;
    public EntityCoordinates clickCoords;
}

// targeted event , raised on user, used item and the target(if any)
public class UsingMouseDownEvent : EntityEventArgs
{
    public EntityUid clickedOn;
    public List<EntityUid> holding = new();
    public EntityUid user;
    public EntityCoordinates clickCoords;
}
// raised when the mouse gets released
public class MouseUpEvent : EntityEventArgs
{
    public EntityUid clickedOn;
    public EntityUid user;
    public EntityCoordinates clickCoords;
}

// targeted event , raised on user, used item and the target(if any)
public class UsingMouseUpEvent : EntityEventArgs
{
    public EntityUid clickedOn;
    public List<EntityUid> holding = new();
    public EntityUid user;
    public EntityCoordinates clickCoords;
}
// raised for every tile-change(on player, on held item if any, on crossed entities)
public class MouseCrossEvent : EntityEventArgs
{
    public EntityUid crossed;
    public EntityUid user;
    public EntityUid activeHolding;
    public List<EntityUid> holding = new();
    public MapCoordinates clickCoords;
}


/// <summary>
/// This handles...
/// </summary>
public sealed class OxydMouseHandlingSystem : EntitySystem
{
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    public bool mousedDown = false;
    public EntityUid crossed = EntityUid.Invalid;
    /// <inheritdoc/>
    public override void Initialize()
    {
        CommandBinds.Builder
            .Bind(
                EngineKeyFunctions.Use,
                new PointerStateInputCmdHandler(HandleMouseEnabled, HandleMouseDisabled, false)
            )
            .Register<OxydMouseHandlingSystem>();
    }

    public bool HandleMouseEnabled(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session is null)
            return true;
        if (session.AttachedEntity is null)
            return true;
        var mouseData = EnsureComp<OxydMouseDataComponent>(session.AttachedEntity.Value);
        mouseData.lastClicked = uid;
        mouseData.mouseMap = _transformSystem.ToMapCoordinates(coords);
        mouseData.mouseEntity = coords;
        RaiseLocalEvent(new MouseDownEvent()
        {
            clickedOn = mouseData.lastClicked,
            user = session.AttachedEntity.Value,
            clickCoords = mouseData.mouseEntity,
        });
        var active = _handsSystem.GetActiveHandEntity();
        if (active is null)
            return false;
        var heldItems = _handsSystem.EnumerateHeld(session.AttachedEntity.Value).ToList();
        var targetedEvent = new UsingMouseDownEvent()
        {
            clickedOn = mouseData.lastClicked,
            user = session.AttachedEntity.Value,
            holding = heldItems,
            clickCoords = mouseData.mouseEntity,
        };
        RaiseLocalEvent(active.Value, targetedEvent);
        RaiseLocalEvent(uid, targetedEvent);
        RaiseLocalEvent(session.AttachedEntity.Value, targetedEvent);
        mousedDown = true;
        return false;
    }

    public bool HandleMouseDisabled(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session is null)
            return true;
        if (session.AttachedEntity is null)
            return true;
        var mouseData = EnsureComp<OxydMouseDataComponent>(session.AttachedEntity.Value);
        mouseData.lastClicked = uid;
        mouseData.mouseMap = _transformSystem.ToMapCoordinates(coords);
        RaiseLocalEvent(new MouseUpEvent()
        {
            clickedOn = mouseData.lastClicked,
            user = session.AttachedEntity.Value,
            clickCoords = mouseData.mouseEntity,
        });
        var active = _handsSystem.GetActiveHandEntity();
        if (active is null)
            return false;
        var heldItems = _handsSystem.EnumerateHeld(session.AttachedEntity.Value).ToList();
        var targetedEvent = new UsingMouseUpEvent()
        {
            clickedOn = mouseData.lastClicked,
            user = session.AttachedEntity.Value,
            holding = heldItems,
            clickCoords = mouseData.mouseEntity,
        };
        RaiseLocalEvent(active.Value, targetedEvent);
        RaiseLocalEvent(uid, targetedEvent);
        RaiseLocalEvent(session.AttachedEntity.Value, targetedEvent);
        mousedDown = false;
        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_stateManager.CurrentState is not GameplayState gameplayState)
            return;
        if (!mousedDown)
            return;
        if (_playerManager.LocalEntity is null)
            return;
        var held = _handsSystem.GetActiveHandEntity();
        var heldList = _handsSystem.EnumerateHeld(_playerManager.LocalEntity.Value).ToList();
        var mouseScreenPos = _inputManager.MouseScreenPosition;
        var mousePos = _eyeManager.PixelToMap(mouseScreenPos);
        var mouseData = EnsureComp<OxydMouseDataComponent>(_playerManager.LocalEntity.Value);
        mouseData.mouseMap = mousePos;
        var ent = gameplayState.GetClickedEntity(mousePos);
        if (ent is not null)
        {
            mouseData.lastHovered = ent.Value;
        }

        var ev = new MouseCrossEvent()
        {
            user = _playerManager.LocalEntity.Value,
            clickCoords = mousePos,
            holding = heldList,
        };
        if (ent is not null)
            ev.crossed = ent.Value;
        if (held is not null)
        {
            ev.activeHolding = held.Value;
            RaiseLocalEvent(held.Value, ev);
        }
        RaiseLocalEvent(ev);
        if (ent is not null)
            RaiseLocalEvent(ent.Value, ev);

    }
}
