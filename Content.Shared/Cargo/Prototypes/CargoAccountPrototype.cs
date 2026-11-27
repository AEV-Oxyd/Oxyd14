using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared.Cargo.Prototypes;

/// <summary>
/// This is a prototype for a single account that stores money on StationBankAccountComponent
/// </summary>
[Prototype]
public sealed partial class CargoAccountPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Full IC name of the account.
    /// </summary>
    [DataField]
    public LocId Name;

    /// <summary>
    /// A shortened code used to refer to the account in UIs
    /// </summary>
    [DataField]
    public LocId Code;

    /// <summary>
    /// Color corresponding to the account.
    /// </summary>
    [DataField]
    public Color Color;

    /// <summary>
    /// Channel used for announcing transactions.
    /// </summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> RadioChannel;

    /// <summary>
    /// Paper prototype used for acquisition slips.
    /// </summary>
    [DataField]
    public EntProtoId AcquisitionSlip;

    /// <summary>
    /// Default bank account identifier used to authenticate cargo orders and withdrawals.
    /// </summary>
    [DataField]
    public string DefaultAccountId = string.Empty;

    /// <summary>
    /// Default PIN code used to authenticate cargo orders and withdrawals.
    /// </summary>
    [DataField]
    public string DefaultPin = "1111";
}
