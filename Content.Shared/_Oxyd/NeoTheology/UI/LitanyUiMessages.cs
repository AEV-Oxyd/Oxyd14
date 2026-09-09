using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.UI;

/// <summary>
/// Shared BUI state broadcast via <c>SetUiState</c>. Contains only public catalog
/// revision metadata — never holiness, roles, or per-viewer availability.
/// Private viewer data must use <see cref="LitanyViewerSnapshotMessage"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyBookPublicState : BoundUserInterfaceState
{
    public uint CatalogRevision { get; }

    public LitanyBookPublicState(uint catalogRevision)
    {
        CatalogRevision = catalogRevision;
    }
}

[Serializable, NetSerializable]
public sealed class LitanyViewerSnapshot : BoundUserInterfaceState
{
    public uint Revision { get; }
    public double Holiness { get; }
    public double Cap { get; }
    public double RegenerationPerSecond { get; }
    public ProtoId<NeoTheologyProfilePrototype>? Profile { get; }
    public ProtoId<NeoTheologySpecializationPrototype>? Specialization { get; }
    public bool Active { get; }
    public List<LitanyViewerEntry> Entries { get; }
    public string? BusyReason { get; }

    public LitanyViewerSnapshot(
        uint revision,
        double holiness,
        double regenerationPerSecond,
        ProtoId<NeoTheologyProfilePrototype>? profile,
        ProtoId<NeoTheologySpecializationPrototype>? specialization,
        bool active,
        List<LitanyViewerEntry> entries,
        string? busyReason,
        double cap = 0d)
    {
        Revision = revision;
        Holiness = holiness;
        Cap = cap;
        RegenerationPerSecond = regenerationPerSecond;
        Profile = profile;
        Specialization = specialization;
        Active = active;
        Entries = entries;
        BusyReason = busyReason;
    }
}

[Serializable, NetSerializable]
public sealed class LitanyViewerEntry
{
    public ProtoId<LitanyPrototype> Litany { get; }
    public bool Available { get; }
    public LocId? UnavailableReason { get; }
    /// <summary>Exact invariant phrase for display; never localized.</summary>
    public string Phrase { get; }
    public double Cost { get; }
    public LitanyCategory Category { get; }

    public LitanyViewerEntry(
        ProtoId<LitanyPrototype> litany,
        bool available,
        LocId? unavailableReason,
        string phrase,
        double cost,
        LitanyCategory category)
    {
        Litany = litany;
        Available = available;
        UnavailableReason = unavailableReason;
        Phrase = phrase;
        Cost = cost;
        Category = category;
    }
}

/// <summary>
/// The role information shown to one viewer. This is presentation data only;
/// authority remains on the server-side cruciform and bearer components.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyRolePresentation
{
    public ProtoId<NeoTheologyProfilePrototype>? Profile { get; }
    public ProtoId<NeoTheologySpecializationPrototype>? Specialization { get; }
    public bool HasCruciform { get; }
    public bool Active { get; }

    public LitanyRolePresentation(
        ProtoId<NeoTheologyProfilePrototype>? profile,
        ProtoId<NeoTheologySpecializationPrototype>? specialization,
        bool hasCruciform,
        bool active)
    {
        Profile = profile;
        Specialization = specialization;
        HasCruciform = hasCruciform;
        Active = active;
    }
}

/// <summary>
/// Stages that may be shown while a server-owned litany request is pending.
/// A missing busy state represents the idle state; there is deliberately no
/// client-provided stage or effect selection.
/// </summary>
[Serializable, NetSerializable]
public enum LitanyCastStage : byte
{
    Choosing,
    Chanting,
    ExtraDelay,
    Committing,
}

/// <summary>
/// Server-authored busy presentation for the current viewer. Request IDs are
/// opaque per-request values and timestamps let the client render progress
/// locally without a high-frequency broadcast.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyBusyState
{
    public string RequestId { get; }
    public ProtoId<LitanyPrototype> Litany { get; }
    public LitanyCastStage Stage { get; }
    public TimeSpan StartedAt { get; }
    public TimeSpan EndsAt { get; }
    public bool CanCancel { get; }

    public LitanyBusyState(
        string requestId,
        ProtoId<LitanyPrototype> litany,
        LitanyCastStage stage,
        TimeSpan startedAt,
        TimeSpan endsAt,
        bool canCancel)
    {
        RequestId = requestId;
        Litany = litany;
        Stage = stage;
        StartedAt = startedAt;
        EndsAt = endsAt;
        CanCancel = canCancel;
    }
}

/// <summary>
/// Actor-targeted catalog/profile snapshot. The server should send this with
/// <c>ServerSendUiMessage</c>; it contains viewer-specific holiness, role and
/// availability data and must not be broadcast as a common UI state.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyViewerSnapshotMessage : BoundUserInterfaceMessage
{
    public uint Revision { get; }
    public double Holiness { get; }
    public double Cap { get; }
    public double RegenerationPerSecond { get; }
    public LitanyRolePresentation RolePresentation { get; }
    public List<LitanyViewerEntry> Entries { get; }
    public LitanyBusyState? BusyState { get; }

    public LitanyViewerSnapshotMessage(
        uint revision,
        double holiness,
        double cap,
        double regenerationPerSecond,
        LitanyRolePresentation rolePresentation,
        List<LitanyViewerEntry> entries,
        LitanyBusyState? busyState = null)
    {
        Revision = revision;
        Holiness = holiness;
        Cap = cap;
        RegenerationPerSecond = regenerationPerSecond;
        RolePresentation = rolePresentation;
        Entries = entries;
        BusyState = busyState;
    }
}

/// <summary>
/// One eligible target/recipe choice. The token is the only value that can
/// identify the choice in a client request; the display label is not an
/// authority-bearing name or permanent identity.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyChoiceOption
{
    public string Token { get; }
    public string DisplayLabel { get; }

    public LitanyChoiceOption(string token, string displayLabel)
    {
        Token = token;
        DisplayLabel = displayLabel;
    }
}

/// <summary>
/// Actor-targeted choices for one pending request. Tokens expire with the
/// request and the revision is checked alongside the request ID server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyChoiceSnapshotMessage : BoundUserInterfaceMessage
{
    public uint Revision { get; }
    public string RequestId { get; }
    public TimeSpan ExpiresAt { get; }
    public List<LitanyChoiceOption> AllowedChoices { get; }

    public LitanyChoiceSnapshotMessage(
        uint revision,
        string requestId,
        TimeSpan expiresAt,
        List<LitanyChoiceOption> allowedChoices)
    {
        Revision = revision;
        RequestId = requestId;
        ExpiresAt = expiresAt;
        AllowedChoices = allowedChoices;
    }
}

/// <summary>
/// Low-frequency progress update for a pending cast. The client derives the
/// displayed fraction from the server timestamps and its synchronized clock.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyProgressMessage : BoundUserInterfaceMessage
{
    public uint Revision { get; }
    public string RequestId { get; }
    public ProtoId<LitanyPrototype> Litany { get; }
    public LitanyCastStage Stage { get; }
    public TimeSpan StartedAt { get; }
    public TimeSpan EndsAt { get; }
    public bool CanCancel { get; }

    public LitanyProgressMessage(
        uint revision,
        string requestId,
        ProtoId<LitanyPrototype> litany,
        LitanyCastStage stage,
        TimeSpan startedAt,
        TimeSpan endsAt,
        bool canCancel)
    {
        Revision = revision;
        RequestId = requestId;
        Litany = litany;
        Stage = stage;
        StartedAt = startedAt;
        EndsAt = endsAt;
        CanCancel = canCancel;
    }
}

/// <summary>
/// Actor-targeted completion or rejection. Reasons are localization IDs and
/// request IDs are opaque; no server-side entity or actor identity is sent.
/// </summary>
[Serializable, NetSerializable]
public sealed class LitanyResultMessage : BoundUserInterfaceMessage
{
    public uint Revision { get; }
    public bool Success { get; }
    public LocId? Reason { get; }
    public string? RequestId { get; }

    public LitanyResultMessage(
        uint revision,
        bool success,
        LocId? reason = null,
        string? requestId = null)
    {
        Revision = revision;
        Success = success;
        Reason = reason;
        RequestId = requestId;
    }

    public LitanyResultMessage(uint revision, LitanyActionResult result)
        : this(revision, result.Success, result.Reason, result.RequestId)
    {
    }

    public LitanyActionResult ToActionResult()
    {
        return new LitanyActionResult(Success, Reason, RequestId);
    }
}

[Serializable, NetSerializable]
public sealed class BeginLitanyMessage : BoundUserInterfaceMessage
{
    public ProtoId<LitanyPrototype> Litany { get; }
    public uint StateRevision { get; }
    public string? ChoiceToken { get; }

    public BeginLitanyMessage(ProtoId<LitanyPrototype> litany, uint stateRevision, string? choiceToken = null)
    {
        Litany = litany;
        StateRevision = stateRevision;
        ChoiceToken = choiceToken;
    }
}

[Serializable, NetSerializable]
public sealed class SubmitLitanyChoicesMessage : BoundUserInterfaceMessage
{
    public string RequestId { get; }
    public List<string> SelectedTokens { get; }
    public ProtoId<NeoTheologySpecializationPrototype>? Specialization { get; }
    public string? RecipeId { get; }
    public string? PlainText { get; }

    public SubmitLitanyChoicesMessage(
        string requestId,
        List<string> selectedTokens,
        ProtoId<NeoTheologySpecializationPrototype>? specialization = null,
        string? recipeId = null,
        string? plainText = null)
    {
        RequestId = requestId;
        SelectedTokens = selectedTokens;
        Specialization = specialization;
        RecipeId = recipeId;
        PlainText = plainText;
    }
}

[Serializable, NetSerializable]
public sealed class CancelLitanyMessage : BoundUserInterfaceMessage
{
    public string RequestId { get; }

    public CancelLitanyMessage(string requestId)
    {
        RequestId = requestId;
    }
}
