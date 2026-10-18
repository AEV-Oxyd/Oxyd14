using System.Linq;
using Content.Shared._Oxyd.Economy;
using Robust.Shared.Maths;

namespace Content.Server._Oxyd.Economy;

public sealed partial class OxydEconomySystem
{
    public void RunPayday()
    {
        EnsureDepartmentsForRound();

        // Refresh department balances from cargo before paying.
        foreach (var acc in _accounts.Values)
        {
            if (acc.IsDepartment)
                PullDepartmentFromCargo(acc);
        }

        var paidCrew = 0;
        var paidDept = 0;
        var totalCredits = 0;

        // 1) Department retainers (Command/Cargo → Service/Security/Engineering)
        foreach (var acc in _accounts.Values.ToList())
        {
            if (!acc.IsDepartment || acc.Employer == null || acc.Wage <= 0)
                continue;

            var amount = acc.Debt + acc.Wage;
            if (TryPayFromEmployer(acc, amount, "Department payroll"))
            {
                acc.Debt = 0;
                paidDept++;
                totalCredits += amount;
            }
            else
            {
                acc.Debt += acc.Wage;
            }
        }

        // 2) Personal wages
        foreach (var acc in _accounts.Values.ToList())
        {
            if (acc.IsDepartment || acc.Employer == null || acc.Wage <= 0)
                continue;

            var amount = acc.Debt + acc.Wage;
            if (TryPayFromEmployer(acc, amount, "Payroll"))
            {
                acc.Debt = 0;
                paidCrew++;
                totalCredits += amount;
            }
            else
            {
                acc.Debt += acc.Wage;
            }
        }

        _chat.DispatchGlobalAnnouncement(
            $"Payday complete. {paidCrew} crew and {paidDept} departments paid totaling {totalCredits} credits.",
            "Payroll",
            playSound: true,
            colorOverride: Color.Gold);
    }

    private bool TryPayFromEmployer(OxydMoneyAccountData recipient, int amount, string purpose)
    {
        if (recipient.Employer == null)
            return false;

        // External benefactor strings (Moebius / Church) mint money.
        var funder = FindDepartmentAccount(recipient.Employer);
        if (funder == null)
        {
            recipient.Balance += amount;
            LogTransaction(recipient, purpose + " (external)", amount, "OxydPayday");
            return true;
        }

        if (funder.Balance < amount)
            return false;

        funder.Balance -= amount;
        recipient.Balance += amount;
        SyncDepartmentToCargo(funder);
        SyncDepartmentToCargo(recipient);
        LogTransaction(funder, purpose, -amount, "OxydPayday");
        LogTransaction(recipient, purpose, amount, "OxydPayday");
        return true;
    }
}
