using Content.Shared._Oxyd.Economy;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// Bank account a loader (PDA) is currently logged into, plus per-loader
/// trade UI state. Lives on the loader entity so entity deletion and round
/// end clean it up instead of a system dictionary leaking between rounds.
/// </summary>
[RegisterComponent]
public sealed partial class OxydPdaBankSessionComponent : Component
{
    /// <summary>Account the cartridge is logged into; 0 = logged out.</summary>
    public int AccountNumber;

    /// <summary>Current UI tab (trade orders cartridge).</summary>
    public OxydTradeUiTab Tab = OxydTradeUiTab.Stations;

    /// <summary>Selected trade station (trade orders cartridge).</summary>
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
