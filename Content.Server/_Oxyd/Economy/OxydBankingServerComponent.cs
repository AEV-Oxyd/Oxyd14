using Content.Shared._Oxyd.Economy;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// The round's banking server (Eris station accounts database). One per round: it holds every
/// money account so the economy runs from entity state instead of system fields. The department
/// accounts it opens are listed on its prototype.
/// </summary>
[RegisterComponent, Access(typeof(OxydEconomySystem))]
public sealed partial class OxydBankingServerComponent : Component
{
    /// <summary>Department accounts opened when the server starts.</summary>
    [DataField]
    public List<OxydDepartmentAccountEntry> Departments = new();

    [DataField]
    public int FirstAccountNumber = 111111;

    [DataField]
    public TimeSpan PaydayInterval = TimeSpan.FromMinutes(OxydEconomyConstants.PaydayIntervalMinutes);

    [ViewVariables]
    public Dictionary<int, OxydMoneyAccountData> Accounts = new();

    /// <summary>Balance last pushed to StationBankAccount per account. Syncs apply only the change.</summary>
    [ViewVariables]
    public Dictionary<int, int> CargoSyncedBalance = new();

    /// <summary>Account number to the mob that holds it, for the character-window balance.</summary>
    [ViewVariables]
    public Dictionary<int, EntityUid> Holders = new();

    [ViewVariables]
    public int NextAccountNumber;

    [ViewVariables]
    public TimeSpan NextPayday;
}

/// <summary>One department account opened by the banking server.</summary>
[DataDefinition]
public sealed partial class OxydDepartmentAccountEntry
{
    /// <summary>Department id, matching job <c>employer</c> strings.</summary>
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField]
    public string? Name;

    /// <summary>Cargo account this department mirrors. Its balance seeds and tracks the account.</summary>
    [DataField]
    public ProtoId<CargoAccountPrototype>? CargoAccount;

    /// <summary>Opening balance when there is no cargo account to read.</summary>
    [DataField]
    public int StartingBalance;

    /// <summary>Department that pays this one's retainer each payday.</summary>
    [DataField]
    public string? Employer;

    [DataField]
    public int Wage;

    [DataField]
    public bool CanMakeAccounts;
}
