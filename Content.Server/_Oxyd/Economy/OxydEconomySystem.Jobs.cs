using Content.Shared._Oxyd.Economy;
using Content.Shared.Access.Components;
using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Robust.Shared.Player;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Economy;

public sealed partial class OxydEconomySystem
{
    [Dependency] private IChatManager _chatManager = default!;

    /// <summary>Economy settings for jobs without their own entry.</summary>
    private static readonly ProtoId<OxydJobEconomyPrototype> DefaultJobEconomy = "Default";

    public void SetupAccountForJob(EntityUid mob, JobPrototype job)
    {
        if (HasComp<OxydPersonalAccountComponent>(mob))
            return;

        var eco = _protos.TryIndex<OxydJobEconomyPrototype>(job.ID, out var jobEco) ? jobEco : _protos.Index(DefaultJobEconomy);
        if (!eco.AccountAllowed)
            return;

        var employer = eco.Employer;
        var meta = MetaData(mob);
        var account = CreateAccount(
            ownerName: meta.EntityName,
            accountName: meta.EntityName,
            balance: ComputeSpawnBalance(eco),
            employer: employer,
            wage: eco.Wage,
            canMakeAccounts: eco.CanMakeAccounts);
        Bank.Comp.Holders[account.AccountNumber] = mob;

        var personal = EnsureComp<OxydPersonalAccountComponent>(mob);
        personal.AccountNumber = account.AccountNumber;
        personal.Balance = account.Balance;
        personal.OwnerName = account.OwnerName;

        if (eco.DepartmentAccountAccess && employer != null)
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

        // JobAfterSpawnEvent fires before the player's session attaches to the body,
        // so delivery is deferred to PlayerSpawnCompleteEvent (OnPlayerSpawnComplete).
        personal.PendingPinMessage = msg;
    }

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!TryComp<OxydPersonalAccountComponent>(args.Mob, out var personal) ||
            personal.PendingPinMessage is not { } msg)
            return;

        personal.PendingPinMessage = null;
        _chatManager.ChatMessageToOne(ChatChannel.Server, msg, msg, args.Mob, false, args.Player.Channel);
    }

    private int ComputeSpawnBalance(OxydJobEconomyPrototype eco)
    {
        // Eris one_time_payment: initial_balance * rand(14,17)/10 or wage * rand(30,70)/10.
        // Each job can set its own range.
        if (eco.InitialBalance >= 0)
            return (int)Math.Round(eco.InitialBalance * _random.NextFloat(eco.InitialBalanceMultiplier.X, eco.InitialBalanceMultiplier.Y));

        if (eco.Wage <= 0)
            return 0;

        return (int)Math.Round(eco.Wage * _random.NextFloat(eco.WageBalanceMultiplier.X, eco.WageBalanceMultiplier.Y));
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
}
