using System.Linq;
using Content.Shared._Oxyd.Ntos;
using Robust.Shared.Map;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Station-wide NT email accounts (Eris email_client parity).
/// Reviewer round-2: accounts/inboxes live on a lazily-spawned nullspace entity
/// (OxydBankSystem pattern) so mail resets each round instead of leaking across
/// rounds, and every collection is bounded by the caps below.</summary>
public sealed partial class OxydEmailSystem : EntitySystem
{
    // Reviewer round-2 caps: bounded mail storage per round.
    private const int MaxAccounts = 128;
    private const int MaxInbox = 64;
    private const int MaxSent = 64;
    private const int MaxSubject = 256;
    private const int MaxBody = 4096;

    private const string MailerLogin = "mailer@nt.mail";

    private EntityUid? _stateEnt;

    /// <summary>Find or create the round-scoped mail state entity (nullspace).</summary>
    private OxydEmailStateComponent GetState()
    {
        if (_stateEnt is { } cached && !Deleted(cached))
            return Comp<OxydEmailStateComponent>(cached);
        var q = EntityQueryEnumerator<OxydEmailStateComponent>();
        while (q.MoveNext(out var uid, out var comp))
        {
            _stateEnt = uid;
            return comp;
        }
        _stateEnt = Spawn(null, MapCoordinates.Nullspace);
        return AddComp<OxydEmailStateComponent>(_stateEnt.Value);
    }

    public bool TryRegister(string login, string password, out string? error)
    {
        error = null;
        var state = GetState();
        EnsureSystemAccount(state);
        login = Normalize(login);
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            error = "Login and password required";
            return false;
        }

        if (state.Accounts.ContainsKey(login))
        {
            error = "Account exists";
            return false;
        }

        if (state.Accounts.Count >= MaxAccounts)
        {
            error = "Account limit reached";
            return false;
        }

        state.Accounts[login] = new OxydEmailAccount { Login = login, Password = password };
        // Welcome mail from the mailer-daemon.
        TrySend(MailerLogin, login, "Welcome to NT Mail",
            "Your NanoTrasen mail account is online. Stay professional.", out _);
        return true;
    }

    public bool TryLogin(EntityUid loader, string login, string password, out string? error)
    {
        error = null;
        var state = GetState();
        login = Normalize(login);
        // Reviewer: NonInteractive accounts (the mailer daemon) can never be
        // logged into — they only send automated notices, so TryLogin rejects
        // them regardless of the password supplied.
        if (!state.Accounts.TryGetValue(login, out var acc) || acc.NonInteractive || acc.Password != password)
        {
            error = "Bad credentials";
            return false;
        }

        if (acc.Suspended)
        {
            error = "Account suspended";
            return false;
        }

        state.Sessions[loader] = login;
        return true;
    }

    public void Logout(EntityUid loader) => GetState().Sessions.Remove(loader);

    public bool TryGetSession(EntityUid loader, out string login)
        => GetState().Sessions.TryGetValue(loader, out login!);

    public bool TrySend(string from, string to, string subject, string body, out string? error)
    {
        error = null;
        var state = GetState();
        EnsureSystemAccount(state);
        from = Normalize(from);
        to = Normalize(to);
        if (!state.Accounts.ContainsKey(from))
        {
            error = "Unknown sender";
            return false;
        }

        if (!state.Accounts.TryGetValue(to, out var dest))
        {
            error = "Recipient not found";
            return false;
        }

        var msg = new OxydEmailMessage
        {
            Id = state.NextMsgId++,
            From = from,
            To = to,
            // Reviewer caps: message text truncated to sane sizes.
            Subject = Truncate(string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim(), MaxSubject),
            Body = Truncate(body ?? string.Empty, MaxBody),
        };
        dest.Inbox.Insert(0, msg);
        if (dest.Inbox.Count > MaxInbox)
            dest.Inbox.RemoveRange(MaxInbox, dest.Inbox.Count - MaxInbox);
        if (state.Accounts.TryGetValue(from, out var src))
        {
            src.Sent.Insert(0, msg);
            if (src.Sent.Count > MaxSent)
                src.Sent.RemoveRange(MaxSent, src.Sent.Count - MaxSent);
        }
        return true;
    }

    public IReadOnlyList<OxydEmailMessage> GetInbox(string login)
        => GetState().Accounts.TryGetValue(Normalize(login), out var acc) ? acc.Inbox : Array.Empty<OxydEmailMessage>();

    public OxydEmailMessage? GetMessage(string login, int id)
        => GetInbox(login).FirstOrDefault(m => m.Id == id);

    public bool TryDelete(string login, int id)
    {
        if (!GetState().Accounts.TryGetValue(Normalize(login), out var acc))
            return false;
        return acc.Inbox.RemoveAll(m => m.Id == id) > 0;
    }

    public void MarkRead(string login, int id)
    {
        var msg = GetMessage(login, id);
        if (msg != null)
            msg.Read = true;
    }

    private void EnsureSystemAccount(OxydEmailStateComponent state)
    {
        // Reviewer: the mailer daemon gets no usable password and a NonInteractive
        // flag so nobody can log in as the official mailer.
        if (!state.Accounts.ContainsKey(MailerLogin))
            state.Accounts[MailerLogin] = new OxydEmailAccount { Login = MailerLogin, NonInteractive = true };
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max];

    private static string Normalize(string login)
    {
        login = login.Trim();
        if (!login.Contains('@') && !string.IsNullOrEmpty(login))
            login += "@nt.mail";
        return login.ToLowerInvariant();
    }
}
