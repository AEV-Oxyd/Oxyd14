using System.Linq;

namespace Content.Server._Oxyd.Ntos;

/// <summary>Station-wide NT email accounts (Eris email_client parity).</summary>
public sealed partial class OxydEmailSystem : EntitySystem
{
    private int _nextMsgId = 1;
    private readonly Dictionary<string, OxydEmailAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<EntityUid, string> _sessions = new();

    public bool TryRegister(string login, string password, out string? error)
    {
        error = null;
        login = Normalize(login);
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            error = "Login and password required";
            return false;
        }

        if (_accounts.ContainsKey(login))
        {
            error = "Account exists";
            return false;
        }

        _accounts[login] = new OxydEmailAccount { Login = login, Password = password };
        // Welcome mail from mailer-daemon
        EnsureSystemAccount();
        TrySend("mailer@nt.mail", login, "Welcome to NT Mail",
            "Your NanoTrasen mail account is online. Stay professional.", out _);
        return true;
    }

    public bool TryLogin(EntityUid loader, string login, string password, out string? error)
    {
        error = null;
        login = Normalize(login);
        if (!_accounts.TryGetValue(login, out var acc) || acc.Password != password)
        {
            error = "Bad credentials";
            return false;
        }

        if (acc.Suspended)
        {
            error = "Account suspended";
            return false;
        }

        _sessions[loader] = login;
        return true;
    }

    public void Logout(EntityUid loader) => _sessions.Remove(loader);

    public bool TryGetSession(EntityUid loader, out string login)
        => _sessions.TryGetValue(loader, out login!);

    public bool TrySend(string from, string to, string subject, string body, out string? error)
    {
        error = null;
        from = Normalize(from);
        to = Normalize(to);
        if (!_accounts.ContainsKey(from))
        {
            error = "Unknown sender";
            return false;
        }

        if (!_accounts.TryGetValue(to, out var dest))
        {
            error = "Recipient not found";
            return false;
        }

        var msg = new OxydEmailMessage
        {
            Id = _nextMsgId++,
            From = from,
            To = to,
            Subject = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject.Trim(),
            Body = body ?? string.Empty,
        };
        dest.Inbox.Insert(0, msg);
        if (_accounts.TryGetValue(from, out var src))
            src.Sent.Insert(0, msg);
        return true;
    }

    public IReadOnlyList<OxydEmailMessage> GetInbox(string login)
        => _accounts.TryGetValue(Normalize(login), out var acc) ? acc.Inbox : Array.Empty<OxydEmailMessage>();

    public OxydEmailMessage? GetMessage(string login, int id)
        => GetInbox(login).FirstOrDefault(m => m.Id == id);

    public bool TryDelete(string login, int id)
    {
        if (!_accounts.TryGetValue(Normalize(login), out var acc))
            return false;
        return acc.Inbox.RemoveAll(m => m.Id == id) > 0;
    }

    public void MarkRead(string login, int id)
    {
        var msg = GetMessage(login, id);
        if (msg != null)
            msg.Read = true;
    }

    private void EnsureSystemAccount()
    {
        if (!_accounts.ContainsKey("mailer@nt.mail"))
            _accounts["mailer@nt.mail"] = new OxydEmailAccount { Login = "mailer@nt.mail", Password = "system" };
    }

    private static string Normalize(string login)
    {
        login = login.Trim();
        if (!login.Contains('@') && !string.IsNullOrEmpty(login))
            login += "@nt.mail";
        return login.ToLowerInvariant();
    }
}

public sealed class OxydEmailAccount
{
    public string Login = string.Empty;
    public string Password = string.Empty;
    public bool Suspended;
    public List<OxydEmailMessage> Inbox = new();
    public List<OxydEmailMessage> Sent = new();
}

public sealed class OxydEmailMessage
{
    public int Id;
    public string From = string.Empty;
    public string To = string.Empty;
    public string Subject = string.Empty;
    public string Body = string.Empty;
    public bool Read;
}
