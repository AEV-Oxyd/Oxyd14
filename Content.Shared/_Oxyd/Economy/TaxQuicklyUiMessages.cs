using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

[Serializable, NetSerializable]
public sealed class TaxQuicklyUiState : BoundUserInterfaceState
{
    public bool LoggedIn;
    public string? Error;
    public int AccountNumber;
    public string OwnerName = string.Empty;
    public int Balance;
    public int Wage;
    public int Debt;
    public string? Employer;
    public bool IsDepartment;
    public bool CanMakeAccounts;
    public List<OxydTransactionData> Transactions = new();
}

[Serializable, NetSerializable]
public sealed class TaxQuicklyUiMessageEvent : CartridgeMessageEvent
{
    public TaxQuicklyUiAction Action;
    public int AccountNumber;
    public int Pin;
    public int TargetAccount;
    public int Amount;
    public string? Text;

    public TaxQuicklyUiMessageEvent(TaxQuicklyUiAction action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public enum TaxQuicklyUiAction : byte
{
    Login,
    Logout,
    Transfer,
    Resign,
    LinkEmployee,
    SetWage,
    DisavowEmployee,
    RegisterPersonal,
    RegisterDepartment,
    Refresh,
}

[RegisterComponent]
public sealed partial class TaxQuicklyCartridgeComponent : Component;
