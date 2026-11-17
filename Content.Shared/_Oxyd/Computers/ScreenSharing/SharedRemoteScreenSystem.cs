using Content.Shared._Oxyd.Computers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.ScreenSharing;

/// <summary>
/// Server -> Client notification asking the client to pull an already-open,
/// remote-viewed target window (currently stashed in RemoteOffscreenStash)
/// back onto the user's visible desktop when they re-activate the device in-world.
/// </summary>
[Serializable, NetSerializable]
public sealed class RemoteScreenBringToFrontEvent : EntityEventArgs
{
    public NetEntity TargetEntity { get; }

    public RemoteScreenBringToFrontEvent(NetEntity targetEntity)
    {
        TargetEntity = targetEntity;
    }
}

/// <summary>
/// Client -> Server notification keeping track of whether a remote-viewed target window
/// is currently open on the player's visible desktop (WindowRoot) vs stashed offscreen.
/// Ensures closing RemoteScreenView closes only hidden offscreen windows while preserving
/// physically opened desktop windows.
/// </summary>
[Serializable, NetSerializable]
public sealed class RemoteScreenLocalWindowStateEvent : EntityEventArgs
{
    public NetEntity TargetEntity { get; }
    public bool IsOpenOnScreen { get; }

    public RemoteScreenLocalWindowStateEvent(NetEntity targetEntity, bool isOpenOnScreen)
    {
        TargetEntity = targetEntity;
        IsOpenOnScreen = isOpenOnScreen;
    }
}

/// <summary>
/// Attached to a player entity while they are actively streaming one or more remote device screens
/// (e.g. APCs, Atmospheric Alarms, Static Computers, or remote PDAs), allowing BUI interactions and
/// range checks to succeed over the network link.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveRemoteScreenViewerComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<NetEntity> AuthorizedTargets = new();
}

public abstract partial class SharedRemoteScreenSystem : EntitySystem
{
    [Dependency] private EntityQuery<ActiveRemoteScreenViewerComponent> _viewerQuery = default!;
    [Dependency] private EntityQuery<EmbeddedComputerHostComponent> _hostQuery = default!;

    public bool IsRemoteScreenAuthorized(EntityUid actor, EntityUid target)
    {
        if (!_viewerQuery.TryComp(actor, out var comp))
            return false;

        if (comp.AuthorizedTargets.Contains(GetNetEntity(target)))
            return true;

        if (_hostQuery.TryComp(target, out var host) &&
            host.ComputerEntity is { } compUid &&
            comp.AuthorizedTargets.Contains(GetNetEntity(compUid)))
        {
            return true;
        }

        return false;
    }
}
