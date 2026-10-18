namespace Content.Shared._Oxyd.Ntos;

/// <summary>
/// Round-scoped NT mail state (reviewer round-2: accounts/inboxes must reset each
/// round instead of living in a system dictionary). Carried on one lazily-spawned
/// nullspace entity — the OxydBankSystem / OxydBankAccountComponent pattern.
/// </summary>
[RegisterComponent]
public sealed partial class OxydEmailStateComponent : Component
{
    public int NextMsgId = 1;

    /// <summary>login -> account. Keyed case-insensitively.</summary>
    public Dictionary<string, OxydEmailAccount> Accounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>PDA loader entity -> logged-in login.</summary>
    public Dictionary<EntityUid, string> Sessions = new();
}

/// <summary>
/// Round-scoped NTNRC channel chat state (same round-reset requirement as
/// OxydEmailStateComponent).
/// </summary>
[RegisterComponent]
public sealed partial class OxydNtChatStateComponent : Component
{
    public int NextChannelId = 1;
    public Dictionary<int, OxydChatChannel> Channels = new();

    /// <summary>PDA loader entity -> display name.</summary>
    public Dictionary<EntityUid, string> Names = new();

    /// <summary>PDA loader entity -> joined channel id.</summary>
    public Dictionary<EntityUid, int?> Joined = new();
}

public sealed class OxydEmailAccount
{
    public string Login = string.Empty;
    public string Password = string.Empty;
    public bool Suspended;

    /// <summary>Reviewer: service accounts (e.g. the mailer daemon) must not be
    /// loggable — they exist only to send automated notices.</summary>
    public bool NonInteractive;

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

public sealed class OxydChatChannel
{
    public int Id;
    public string Title = string.Empty;
    public HashSet<EntityUid> Clients = new();
    public List<string> Messages = new();
}
