namespace Content.Server._Oxyd.Medical;

/// <summary>
/// An organ that is not installed in a body. Decay ticks this set instead of every organ.
/// </summary>
[RegisterComponent]
public sealed partial class OutOfBodyComponent : Component
{
}
