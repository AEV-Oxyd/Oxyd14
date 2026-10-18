using Content.Shared._Oxyd.AstersGuild;
using Robust.Shared.Map;

namespace Content.Server._Oxyd.AstersGuild;

/// <summary>
/// Shared Asters Guild account — ports the Eris department account
/// (code/modules/economy/Accounts.dm, department_account_access on guild jobs):
/// one station-wide balance every trade console and the ATM transact against.
/// The account entity is spawned lazily on first use and lives in nullspace.
/// </summary>
public sealed partial class OxydBankSystem : EntitySystem
{
    private EntityUid? _account;

    /// <summary>Find or create the shared guild account entity.</summary>
    public EntityUid GetAccount()
    {
        if (_account is { } cached && !Deleted(cached))
            return cached;
        var q = EntityQueryEnumerator<OxydBankAccountComponent>();
        while (q.MoveNext(out var uid, out _))
        {
            _account = uid;
            return uid;
        }
        _account = Spawn(null, MapCoordinates.Nullspace);
        AddComp<OxydBankAccountComponent>(_account.Value);
        return _account.Value;
    }

    public int Balance()
    {
        var a = GetAccount();
        return TryComp(a, out OxydBankAccountComponent? b) ? b.Balance : 0;
    }

    public void Credit(int amount)
    {
        var a = GetAccount();
        if (TryComp(a, out OxydBankAccountComponent? b))
            b.Balance += amount;
    }

    public bool TrySpend(int amount)
    {
        var a = GetAccount();
        if (!TryComp(a, out OxydBankAccountComponent? b) || b.Balance < amount)
            return false;
        b.Balance -= amount;
        return true;
    }
}
