using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

/// <summary>Round-scoped money account (Eris /datum/money_account).</summary>
[Serializable, NetSerializable]
public sealed class OxydMoneyAccountData
{
    public int AccountNumber;
    public int Pin;
    public string OwnerName = string.Empty;
    public string AccountName = string.Empty;
    public int Balance;
    public string? Employer;
    public int Wage;
    public int WageOriginal;
    public bool WageManual;
    public int Debt;
    public bool IsDepartment;
    public string? DepartmentId;
    public bool CanMakeAccounts;
    public int SecurityLevel; // 0 auto / 1 pin / 2 card+pin
    public bool Suspended;
    public List<OxydTransactionData> Transactions = new();
}

[Serializable, NetSerializable]
public sealed class OxydTransactionData
{
    public string TargetName = string.Empty;
    public string Purpose = string.Empty;
    public int Amount;
    public string SourceTerminal = string.Empty;
    public TimeSpan Time;
}

/// <summary>Links a mob mind/body to a personal money account number.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydPersonalAccountComponent : Component
{
    [DataField, AutoNetworkedField]
    public int AccountNumber;

    /// <summary>Department account number remembered for Manager/Worker style access.</summary>
    [DataField, AutoNetworkedField]
    public int? DepartmentAccountNumber;

    [DataField, AutoNetworkedField]
    public int? DepartmentPin;
}

/// <summary>Associates an ID card with a personal account number (Eris associated_account_number).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydBankIdComponent : Component
{
    [DataField, AutoNetworkedField]
    public int AccountNumber;
}
