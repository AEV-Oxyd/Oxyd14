namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Present only while a machine UI has at least one viewer. Update loops iterate this
/// instead of asking every machine whether its window is open.
/// </summary>
[RegisterComponent]
public sealed partial class OxydActiveInterfaceComponent : Component
{
}
