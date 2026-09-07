using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.UI;

[Serializable, NetSerializable]
public sealed class LitanyViewerSnapshot : BoundUserInterfaceState
{
    public uint Revision { get; }
    public double Holiness { get; }
    public double MaximumHoliness { get; }
    public double RegenerationPerSecond { get; }
    public NeoTheologyRank Rank { get; }
    public NeoTheologySpecialization Specialization { get; }
    public NeoTheologyClearance Clearance { get; }
    public bool Active { get; }
    public List<LitanyViewerEntry> Entries { get; }
    public string? BusyReason { get; }

    public LitanyViewerSnapshot(
        uint revision,
        double holiness,
        double maximumHoliness,
        double regenerationPerSecond,
        NeoTheologyRank rank,
        NeoTheologySpecialization specialization,
        NeoTheologyClearance clearance,
        bool active,
        List<LitanyViewerEntry> entries,
        string? busyReason)
    {
        Revision = revision;
        Holiness = holiness;
        MaximumHoliness = maximumHoliness;
        RegenerationPerSecond = regenerationPerSecond;
        Rank = rank;
        Specialization = specialization;
        Clearance = clearance;
        Active = active;
        Entries = entries;
        BusyReason = busyReason;
    }
}

[Serializable, NetSerializable]
public sealed class LitanyViewerEntry
{
    public ProtoId<LitanyPrototype> Litany { get; }
    public LitanyCategory Category { get; }
    public LocId Name { get; }
    public LocId Description { get; }
    public string Phrase { get; }
    public double Cost { get; }
    public TimeSpan Cooldown { get; }
    public TimeSpan CastDuration { get; }
    public LitanyTargetMode TargetMode { get; }
    public bool Available { get; }
    public LocId? UnavailableReason { get; }

    public LitanyViewerEntry(
        ProtoId<LitanyPrototype> litany,
        LitanyCategory category,
        LocId name,
        LocId description,
        string phrase,
        double cost,
        TimeSpan cooldown,
        TimeSpan castDuration,
        LitanyTargetMode targetMode,
        bool available,
        LocId? unavailableReason)
    {
        Litany = litany;
        Category = category;
        Name = name;
        Description = description;
        Phrase = phrase;
        Cost = cost;
        Cooldown = cooldown;
        CastDuration = castDuration;
        TargetMode = targetMode;
        Available = available;
        UnavailableReason = unavailableReason;
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
    public NeoTheologySpecialization? Specialization { get; }
    public string? RecipeId { get; }
    public string? PlainText { get; }

    public SubmitLitanyChoicesMessage(
        string requestId,
        List<string> selectedTokens,
        NeoTheologySpecialization? specialization = null,
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
