using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.16: the NeoTheology armaments printer (Eris <c>datum/armament</c> + the EOTP armory UI).
/// <para>
/// P3.5: the points and the purchase counters now live on the Eye of the Protector (Eris keeps them
/// on the EOTP datum), so the printer only knows how far the buyer may stand.
/// </para>
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ArmamentsPrinterComponent : Component
{
    /// <summary>
    /// Eris <c>get_dist(eotp.loc, H.loc) > 3</c>: how far the buyer may stand from the printer.
    /// </summary>
    [DataField]
    public float Range = 3f;
}
