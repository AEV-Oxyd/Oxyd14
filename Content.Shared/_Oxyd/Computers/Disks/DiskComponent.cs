using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Disks;

/// <summary>
/// Identifies an entity as a computer/data disk that can be inserted into PDAs, tablets, lathes, and terminals.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DiskComponent : Component
{
}
