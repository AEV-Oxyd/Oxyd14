using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// P2.16: the NeoTheology armaments printer (Eris <c>datum/armament</c> + the EOTP armory UI).
/// <para>
/// Eris keeps the points and the purchase counters on the Eye of the Protector; Phase 2 does not
/// have the Eye yet, so they live here and P3.5 rebases the debit onto the Eye (see the plan).
/// </para>
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArmamentsPrinterComponent : Component
{
    /// <summary>
    /// Eris <c>get_dist(eotp.loc, H.loc) > 3</c>: how far the buyer may stand from the printer.
    /// </summary>
    [DataField]
    public float Range = 3f;

    /// <summary>
    /// Armament points available to spend. Eris: <c>eotp.armaments_points</c>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Points;

    /// <summary>
    /// Ceiling on accrued points. Eris: <c>eotp.max_armaments_points</c>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaxPoints = 300;

    /// <summary>
    /// Eris <c>max_increase</c>: the first purchase of an armament lifts the point ceiling by the
    /// armament's own <see cref="ArmamentPrototype.MaxPointsIncrease"/>.
    /// </summary>
    [ViewVariables]
    public bool FirstPurchaseMade;

    /// <summary>
    /// Per-armament purchase counters, keyed by prototype id. Eris keeps these on the datum; the
    /// datum is shared state here, so they belong to the printer that sold them.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int> PurchaseCount = new();
}
