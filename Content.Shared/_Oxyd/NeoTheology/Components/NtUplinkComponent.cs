using Content.Shared.FixedPoint;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Eris <c>datum/core_module/cruciform/uplink</c> (modules.dm:30-53) as state on the cruciform:
/// the inquisitor's hidden uplink and the telecrystals left in it. The store entity is created
/// on first use and lives in nullspace while the module is installed.
/// </summary>
[RegisterComponent]
public sealed partial class NtUplinkComponent : Component
{
    /// <summary>Eris <c>modules.dm:31</c> grants 15 telecrystals to a fresh uplink.</summary>
    [DataField]
    public FixedPoint2 StoredTelecrystals = FixedPoint2.New(15);

    /// <summary>The nullspace store entity, while the uplink module is installed.</summary>
    [ViewVariables]
    public EntityUid? Store;
}
