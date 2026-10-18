using Content.Shared._Oxyd.Economy;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// TaxQuickly bank session for one loader (PDA). Per-program so logging into
/// or out of one cartridge does not touch another. Lives on the loader entity.
/// </summary>
[RegisterComponent]
public sealed partial class OxydTaxQuicklySessionComponent : Component
{
    /// <summary>Account the cartridge is logged into; 0 = logged out.</summary>
    public int AccountNumber;
}

/// <summary>
/// Trade orders bank session + per-loader UI state for one loader (PDA).
/// </summary>
[RegisterComponent]
public sealed partial class OxydTradeOrdersSessionComponent : Component
{
    /// <summary>Account the cartridge is logged into; 0 = logged out.</summary>
    public int AccountNumber;

    /// <summary>Current UI tab.</summary>
    public OxydTradeUiTab Tab = OxydTradeUiTab.Stations;

    /// <summary>Selected trade station.</summary>
    public string? SelectedStation;
}

/// <summary>
/// Trade cart lines for a loader (PDA running the orders cartridge).
/// </summary>
[RegisterComponent]
public sealed partial class OxydTradeCartComponent : Component
{
    public List<OxydTradeCartLineRuntime> Lines = new();
}

/// <summary>
/// PIN-bruteforce state for one user entity. Counts failures in a rolling
/// window measured from the FIRST failure; reaching the cap locks the user out.
/// </summary>
[RegisterComponent]
public sealed partial class OxydLoginFailuresComponent : Component
{
    public int Count;
    public TimeSpan FirstFailAt;
    public TimeSpan LockedUntil;
}

/// <summary>Which email message is open in the mail cartridge on this loader.</summary>
[RegisterComponent]
public sealed partial class OxydEmailUiComponent : Component
{
    public int? OpenMessageId;
}

/// <summary>Which record is selected in the records cartridge on this loader.</summary>
[RegisterComponent]
public sealed partial class OxydRecordsUiComponent : Component
{
    public uint? ActiveRecord;
}
