using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Economy;

/// <summary>
/// Per-job economy wiring. <see cref="ID"/> matches the <c>JobPrototype</c> id.
/// </summary>
[Prototype("oxydJobEconomy")]
public sealed partial class OxydJobEconomyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Credits paid each payday. 0 = WAGE_NONE.</summary>
    [DataField]
    public int Wage = OxydEconomyConstants.WageLabour;

    /// <summary>
    /// Eris initial_balance. -1 means derive from wage (wage * 3.0–7.0).
    /// Otherwise spawn balance is initial * 1.4–1.7.
    /// </summary>
    [DataField]
    public int InitialBalance = -1;

    [DataField]
    public bool AccountAllowed = true;

    [DataField]
    public bool DepartmentAccountAccess;

    /// <summary>Employer department id (CargoAccount / Eris department string).</summary>
    [DataField]
    public string? Employer = OxydEconomyConstants.EmployerService;

    [DataField]
    public bool CanMakeAccounts;
}
