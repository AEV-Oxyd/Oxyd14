using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

[Serializable, NetSerializable]
public enum OxydAccountsDbUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydAccountsDbAccountSummary
{
    public int AccountNumber;
    public string OwnerName = string.Empty;
    public int Balance;
    public bool Suspended;
    public bool IsDepartment;
}

[Serializable, NetSerializable]
public sealed class OxydAccountsDbUiState : BoundUserInterfaceState
{
    public bool HasAccess;
    public bool Creating;
    public int? DetailAccount;
    public string DetailOwner = string.Empty;
    public int DetailBalance;
    public bool DetailSuspended;
    public int DetailWage;
    public string? DetailEmployer;
    public List<OxydAccountsDbAccountSummary> Accounts = new();
    public List<OxydTransactionData> Transactions = new();
    public string? Error;
    public string? Status;
}

[Serializable, NetSerializable]
public sealed class OxydAccountsDbSelectMessage : BoundUserInterfaceMessage
{
    public int AccountNumber;
}

[Serializable, NetSerializable]
public sealed class OxydAccountsDbBackMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class OxydAccountsDbBeginCreateMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class OxydAccountsDbCreateMessage : BoundUserInterfaceMessage
{
    public string OwnerName = string.Empty;
    public int StartingFunds;
}

[Serializable, NetSerializable]
public sealed class OxydAccountsDbToggleSuspendMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class OxydAccountsDbAdjustMessage : BoundUserInterfaceMessage
{
    public int Delta;
}

[Serializable, NetSerializable]
public sealed class OxydAccountsDbRevokePayrollMessage : BoundUserInterfaceMessage;
