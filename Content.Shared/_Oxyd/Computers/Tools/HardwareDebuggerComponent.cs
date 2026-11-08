using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.Computers.Tools;

[RegisterComponent, NetworkedComponent]
public sealed partial class HardwareDebuggerComponent : Component
{
    [DataField] public bool Active = true;
}
