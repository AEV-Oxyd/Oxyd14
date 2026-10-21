using System.Linq;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Economy;

/// <summary>Eris TaxQuickly 2565 — PDA banking cartridge.</summary>
public sealed partial class TaxQuicklyCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private OxydEconomySystem _economy = default!;
    [Dependency] private IPrototypeManager _protos = default!;

    // Session keyed by loader uid
    // Session lives on the loader via OxydTaxQuicklySessionComponent — cleans up
    // with the entity and never leaks between rounds.

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TaxQuicklyCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<TaxQuicklyCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<TaxQuicklyCartridgeComponent, ComponentShutdown>(OnShutdown);

    }


    private void OnShutdown(Entity<TaxQuicklyCartridgeComponent> ent, ref ComponentShutdown args)
    {
        // Sessions are on loader; ignore.
    }

    private void OnReady(EntityUid uid, TaxQuicklyCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUi(uid, args.Loader);
    }

    private void OnMessage(EntityUid uid, TaxQuicklyCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not TaxQuicklyUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        string? error = null;

        switch (msg.Action)
        {
            case TaxQuicklyUiAction.Login:
                if (_economy.TryLogin(msg.AccountNumber, msg.Pin, out _, "TaxQuickly", loader))
                    EnsureComp<OxydTaxQuicklySessionComponent>(loader).AccountNumber = msg.AccountNumber;
                else
                    error = "Login failed";
                break;
            case TaxQuicklyUiAction.Logout:
                // Clear in place: a deferred removal would still read as logged in for this refresh.
                if (TryComp<OxydTaxQuicklySessionComponent>(loader, out var outSession))
                    outSession.AccountNumber = 0;
                break;
            case TaxQuicklyUiAction.Transfer:
                if (!RequireSession(loader, out var from))
                {
                    error = "Not logged in";
                    break;
                }

                if (!_economy.TryTransfer(from, msg.TargetAccount, msg.Amount, "TaxQuickly transfer", "TaxQuickly", out error))
                    error ??= "Transfer failed";
                break;
            case TaxQuicklyUiAction.Resign:
                if (RequireSession(loader, out var resignAcc) && _economy.TryGetAccount(resignAcc, out var r))
                {
                    if (r.IsDepartment)
                    {
                        error = "Departments cannot resign";
                        break;
                    }

                    r.Employer = null;
                    r.Wage = 0;
                    r.Debt = 0;
                }
                break;
            case TaxQuicklyUiAction.LinkEmployee:
                if (!RequireSession(loader, out var dept) || !_economy.TryGetAccount(dept, out var deptAcc) || !deptAcc.IsDepartment)
                {
                    error = "Department login required";
                    break;
                }

                if (!_economy.TryGetAccount(msg.TargetAccount, out var employee))
                {
                    error = "Unknown employee account";
                    break;
                }

                if (employee.IsDepartment)
                {
                    error = "Cannot employ a department";
                    break;
                }

                // Can't poach another department's employee without their consent —
                // only unemployed accounts (or our own employees) may be linked.
                if (employee.Employer != null && employee.Employer != deptAcc.DepartmentId)
                {
                    error = "Employee belongs to another department";
                    break;
                }

                employee.Employer = deptAcc.DepartmentId;
                break;
            case TaxQuicklyUiAction.SetWage:
                if (!RequireSession(loader, out var dept2) || !_economy.TryGetAccount(dept2, out var d2) || !d2.IsDepartment)
                {
                    error = "Department login required";
                    break;
                }

                if (!_economy.TryGetAccount(msg.TargetAccount, out var emp2))
                {
                    error = "Unknown employee account";
                    break;
                }

                if (emp2.Employer != d2.DepartmentId)
                {
                    error = "Not your employee";
                    break;
                }

                emp2.Wage = Math.Clamp(msg.Amount, 0, OxydEconomyConstants.WageCommand);
                emp2.WageManual = true;
                break;
            case TaxQuicklyUiAction.DisavowEmployee:
                if (!RequireSession(loader, out var dept3) || !_economy.TryGetAccount(dept3, out var d3) || !d3.IsDepartment)
                {
                    error = "Department login required";
                    break;
                }

                if (_economy.TryGetAccount(msg.TargetAccount, out var emp3))
                {
                    if (emp3.Employer != d3.DepartmentId)
                    {
                        error = "Not your employee";
                        break;
                    }

                    emp3.Employer = null;
                    emp3.Wage = 0;
                    emp3.Debt = 0;
                }
                break;
            case TaxQuicklyUiAction.RegisterPersonal:
                if (!RequireSession(loader, out var maker) || !_economy.TryGetAccount(maker, out var makerAcc) || !makerAcc.CanMakeAccounts)
                {
                    error = "No account creation rights";
                    break;
                }

                if (!_economy.TryAdjust(maker, -OxydEconomyConstants.PersonalAccountCreateFee, "Register personal account", "TaxQuickly"))
                {
                    error = "Insufficient funds for fee";
                    break;
                }

                var created = _economy.CreateAccount(msg.Text ?? "New Account", msg.Text, 0, makerAcc.DepartmentId, 0);
                error = $"Created personal acct={created.AccountNumber} pin={created.Pin}";
                break;
            case TaxQuicklyUiAction.RegisterDepartment:
                if (!RequireSession(loader, out var maker2) || !_economy.TryGetAccount(maker2, out var m2) || !m2.CanMakeAccounts)
                {
                    error = "No account creation rights";
                    break;
                }

                var deptName = (msg.Text ?? string.Empty).Trim();
                // The id flows into StationBankAccount.Accounts as a CargoAccountPrototype
                // key — free text crashes every cargo console's prototype lookup, and an
                // existing id creates a shadow department.
                if (deptName.Length == 0 || deptName.Length > 32 ||
                    !_protos.TryIndex<CargoAccountPrototype>(deptName, out _))
                {
                    error = "Unknown department id";
                    break;
                }

                if (_economy.FindDepartmentAccount(deptName) != null)
                {
                    error = "Department already exists";
                    break;
                }

                // Charge the fee only after every check has passed.
                if (!_economy.TryAdjust(maker2, -OxydEconomyConstants.DepartmentAccountCreateFee, "Register department account", "TaxQuickly"))
                {
                    error = "Insufficient funds for fee";
                    break;
                }

                var dCreated = _economy.CreateAccount(deptName, deptName, 0, m2.DepartmentId, 0, isDepartment: true, departmentId: deptName, canMakeAccounts: true);
                error = $"Created dept acct={dCreated.AccountNumber} pin={dCreated.Pin}";
                break;
            case TaxQuicklyUiAction.Refresh:
                break;
        }

        UpdateUi(uid, loader, error);
    }

    private bool RequireSession(EntityUid loader, out int account)
    {
        account = 0;
        if (!TryComp<OxydTaxQuicklySessionComponent>(loader, out var s) || s.AccountNumber == 0)
            return false;
        account = s.AccountNumber;
        return true;
    }

    private void UpdateUi(EntityUid cart, EntityUid loader, string? error = null)
    {
        var state = new TaxQuicklyUiState { Error = error };
        if (RequireSession(loader, out var number) && _economy.TryGetAccount(number, out var acc))
        {
            state.LoggedIn = true;
            state.AccountNumber = acc.AccountNumber;
            state.OwnerName = acc.OwnerName;
            state.Balance = acc.Balance;
            state.Wage = acc.Wage;
            state.Debt = acc.Debt;
            state.Employer = acc.Employer;
            state.IsDepartment = acc.IsDepartment;
            state.CanMakeAccounts = acc.CanMakeAccounts;
            state.Transactions = acc.Transactions.TakeLast(12).Reverse().ToList();
        }

        _cartridges.UpdateCartridgeUiState(loader, state);
    }
}
