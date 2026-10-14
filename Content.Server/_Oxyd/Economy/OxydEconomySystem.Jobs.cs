using System.Linq;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Access.Components;
using Content.Shared.Chat;
using Content.Shared.PDA;
using Content.Shared.Roles;

namespace Content.Server._Oxyd.Economy;

public sealed partial class OxydEconomySystem
{
    public void SetupAccountForJob(EntityUid mob, JobPrototype job)
    {
        if (HasComp<OxydPersonalAccountComponent>(mob))
            return;

        EnsureDepartmentsForRound();

        if (!_protos.TryIndex<OxydJobEconomyPrototype>(job.ID, out var eco))
        {
            ApplyDefaults(mob,
                wage: OxydEconomyConstants.WageLabour,
                initialBalance: -1,
                accountAllowed: true,
                deptAccess: false,
                employer: OxydEconomyConstants.EmployerService,
                canMakeAccounts: false);
            return;
        }

        ApplyDefaults(mob, eco.Wage, eco.InitialBalance, eco.AccountAllowed, eco.DepartmentAccountAccess, eco.Employer, eco.CanMakeAccounts);
    }

    private void ApplyDefaults(
        EntityUid mob,
        int wage,
        int initialBalance,
        bool accountAllowed,
        bool deptAccess,
        string? employer,
        bool canMakeAccounts)
    {
        if (!accountAllowed)
            return;

        var meta = MetaData(mob);
        var money = ComputeSpawnBalance(wage, initialBalance);
        var account = CreateAccount(
            ownerName: meta.EntityName,
            accountName: meta.EntityName,
            balance: money,
            employer: employer,
            wage: wage,
            canMakeAccounts: canMakeAccounts);

        var personal = EnsureComp<OxydPersonalAccountComponent>(mob);
        personal.AccountNumber = account.AccountNumber;

        if (deptAccess && employer != null)
        {
            var dept = FindDepartmentAccount(employer);
            if (dept != null)
            {
                personal.DepartmentAccountNumber = dept.AccountNumber;
                personal.DepartmentPin = dept.Pin;
            }
        }

        Dirty(mob, personal);
        LinkIdCard(mob, account.AccountNumber);

        var msg = $"Account #{account.AccountNumber} PIN {account.Pin}";
        if (personal.DepartmentAccountNumber is { } d)
            msg += $" | Dept #{d} PIN {personal.DepartmentPin}";

        _chat.TrySendInGameICMessage(mob, msg, InGameICChatType.Emote, hideChat: true);
    }

    private int ComputeSpawnBalance(int wage, int initialBalance)
    {
        // Eris one_time_payment: initial_balance * rand(14,17)/10 or wage * rand(30,70)/10
        if (initialBalance >= 0)
            return (int)Math.Round(initialBalance * _random.Next(14, 18) / 10.0);

        if (wage <= 0)
            return 0;

        return (int)Math.Round(wage * _random.Next(30, 71) / 10.0);
    }

    private void LinkIdCard(EntityUid mob, int accountNumber)
    {
        if (!_inventory.TryGetSlotEntity(mob, "id", out var idUid))
            return;

        var cardId = idUid.Value;
        if (TryComp<PdaComponent>(idUid, out var pda) && pda.ContainedId != null)
            cardId = pda.ContainedId.Value;

        if (!HasComp<IdCardComponent>(cardId))
            return;

        var bankId = EnsureComp<OxydBankIdComponent>(cardId);
        bankId.AccountNumber = accountNumber;
        Dirty(cardId, bankId);
    }

    public void EnsureDepartmentsForRound()
    {
        if (_departmentsReady && _accounts.Values.Any(a => a.IsDepartment))
            return;

        foreach (var station in _station.GetStations())
        {
            EnsureDepartmentAccounts(station);
        }
    }
}
