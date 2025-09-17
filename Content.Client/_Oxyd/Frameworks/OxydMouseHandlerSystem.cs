using Content.Client.Hands.Systems;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._Oxyd.Framework;
// global event
public class MouseDownEvent : EntityEventArgs
{
    public EntityUid clickedOn;
    public EntityUid user;
    public EntityCoordinates clickCoords;
    public bool clientsideEntityTargeted = false;
}
// targeted event , raised on user, used item and the target(if any)
public class UsingMouseDownEvent : EntityEventArgs
{
    public EntityUid clickedOn;
    public EntityUid used;
    public EntityUid user;
    public EntityCoordinates clickCoords;
    public bool clientsideEntityTargeted = false;
}

/// <summary>
/// This handles...
/// </summary>
public sealed class OxydMouseHandlingSystem : EntitySystem
{
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        CommandBinds.Builder
            .Bind(
                EngineKeyFunctions.Use,
                new PointerInputCmdHandler(HandleUseInteraction))

            .Register<OxydMouseHandlingSystem>();
    }

    public bool HandleUseInteraction(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session is null)
            return true;
        if (session.AttachedEntity is null)
            return true;
        RaiseLocalEvent(new MouseDownEvent()
        {
            clickedOn = uid,
            user = session.AttachedEntity.Value,
            clickCoords = coords,
            clientsideEntityTargeted = IsClientSide(uid),
        });
        var heldItem = _handsSystem.GetActiveItem(session.AttachedEntity.Value);
        if (heldItem is null)
            return false;
        var targetedEvent = new UsingMouseDownEvent()
        {
            clickedOn = uid,
            user = session.AttachedEntity.Value,
            used = heldItem.Value,
            clickCoords = coords,
            clientsideEntityTargeted = IsClientSide(uid),
        };
        RaiseLocalEvent(heldItem.Value, targetedEvent);
        RaiseLocalEvent(uid, targetedEvent);
        RaiseLocalEvent(session.AttachedEntity.Value, targetedEvent);
        return false;
    }
}
