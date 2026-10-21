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

    /// <summary>PIN-bruteforce state: failures counted in a rolling window
    /// measured from FirstFailAt; reaching the cap locks the account.</summary>
    public int FailCount;
    public TimeSpan FirstFailAt;
    public TimeSpan LoginLockedUntil;
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

    /// <summary>Mirrored balance for the character window. The PIN stays server-side.</summary>
    [DataField, AutoNetworkedField]
    public int Balance;

    [DataField, AutoNetworkedField]
    public string OwnerName = string.Empty;

    /// <summary>Department account number remembered for Manager/Worker style access.</summary>
    [DataField, AutoNetworkedField]
    public int? DepartmentAccountNumber;

    /// <summary>Server-only: networking this lets any client in range read the PIN.</summary>
    public int? DepartmentPin;

    /// <summary>Server-only: PIN delivery text waiting for the player to finish spawning.</summary>
    public string? PendingPinMessage;
}

/// <summary>Associates an ID card with a personal account number (Eris associated_account_number).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydBankIdComponent : Component
{
    [DataField, AutoNetworkedField]
    public int AccountNumber;
}
