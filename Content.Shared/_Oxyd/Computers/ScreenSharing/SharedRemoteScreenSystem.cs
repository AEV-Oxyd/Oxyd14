using Content.Shared._Oxyd.Computers;
using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.ScreenSharing;

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
