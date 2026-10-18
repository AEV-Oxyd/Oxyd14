using Content.Server.Cargo.Systems;
using Content.Shared._Oxyd.Economy;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// After cargo pallet sales, mirror StationBankAccount department balances into OxydMoneyAccount
/// (Guild receives OverrideSell art cuts).
/// </summary>
public sealed partial class OxydGuildTradeBridgeSystem : EntitySystem
{
    [Dependency] private readonly OxydEconomySystem _economy = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySoldEvent>(OnSold);
    }

    private void OnSold(ref EntitySoldEvent args)
    {
        // Cargo already credited OverrideAccount (e.g. Guild) via UpdateBankAccount.
        // Pull those balances into the Eris-style personal/dept ledger.
        if (args.Station != default)
            _economy.EnsureDepartmentAccounts(args.Station);

        _economy.SyncDepartmentsFromCargo();
    }
}
