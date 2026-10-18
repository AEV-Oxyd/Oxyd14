using Content.Shared.Cargo.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Economy;

/// <summary>Eris ATM — personal/dept account teller using Credit stacks.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydAtmComponent : Component
{
    [DataField, AutoNetworkedField]
    public int? SessionAccount;
}

/// <summary>Eris Accounts uplink terminal (HoP) — list/create/suspend/adjust.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydAccountsDbComponent : Component
{
    [DataField, AutoNetworkedField]
    public int? DetailAccount;

    [DataField, AutoNetworkedField]
    public bool Creating;

    public const int FundCap = 1_000_000;
}

/// <summary>Eris cash register till. Linked account receives banked till funds.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydCashRegisterComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool DrawerOpen;

    [DataField, AutoNetworkedField]
    public int Stored;

    /// <summary>Personal/dept account number. 0 = resolve <see cref="DepartmentId"/>.</summary>
    [DataField, AutoNetworkedField]
    public int LinkedAccountNumber;

    [DataField, AutoNetworkedField]
    public ProtoId<CargoAccountPrototype> DepartmentId = "Service";
}

/// <summary>Eris EFTPOS — arm a charge, pay with cash or ID-linked account.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydEftposComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Charge;

    [DataField, AutoNetworkedField]
    public bool Charging;

    [DataField, AutoNetworkedField]
    public int LinkedAccountNumber;

    [DataField, AutoNetworkedField]
    public ProtoId<CargoAccountPrototype> DepartmentId = "Service";
}

/// <summary>Physical tip jar — accepts Credit stacks.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydTipJarComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Stored;
}

/// <summary>Charge card / ewallet (Eris spacecash/ewallet).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydEwalletComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Worth;

    [DataField, AutoNetworkedField]
    public string OwnerName = string.Empty;
}

/// <summary>Vendor sales credit department/personal account on paid eject.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydVendorEarningsComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<CargoAccountPrototype> DepartmentId = "Service";

    [DataField, AutoNetworkedField]
    public int LinkedAccountNumber;

    /// <summary>Credits charged per vend when item has no StaticPrice.</summary>
    [DataField, AutoNetworkedField]
    public int DefaultPrice = 50;

    /// <summary>If true, unpaid ejects are blocked.</summary>
    [DataField, AutoNetworkedField]
    public bool RequirePayment = true;
}

[Serializable, NetSerializable]
public enum OxydAtmUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OxydAtmUiState : BoundUserInterfaceState
{
    public bool LoggedIn;
    public int AccountNumber;
    public string OwnerName = string.Empty;
    public int Balance;
    public string? Error;
}

[Serializable, NetSerializable]
public sealed class OxydAtmLoginMessage : BoundUserInterfaceMessage
{
    public int AccountNumber;
    public int Pin;
}

[Serializable, NetSerializable]
public sealed class OxydAtmLogoutMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class OxydAtmWithdrawMessage : BoundUserInterfaceMessage
{
    public int Amount;
}

[Serializable, NetSerializable]
public sealed class OxydAtmTransferMessage : BoundUserInterfaceMessage
{
    public int TargetAccount;
    public int Amount;
}
