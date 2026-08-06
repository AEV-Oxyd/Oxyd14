using System.Collections.Frozen;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class LanguageKnowledgeComponent : Component
{
    [ViewVariables]
    public ProtoId<LanguagePrototype> chosen;
    [DataField]
    public HashSet<ProtoId<LanguagePrototype>> understanding = new();
    [DataField]
    public HashSet<ProtoId<LanguagePrototype>> speaking = new();
}

[RegisterComponent]
public sealed partial class LanguageDataCoreComponent : Component
{
    [DataField(required: false)]
    public FrozenDictionary<string, ProtoId<LanguagePrototype>> keyMapping = new Dictionary<string, ProtoId<LanguagePrototype>>().ToFrozenDictionary();
    // Holds all words
    [DataField(required: false)]
    public FrozenDictionary<ProtoId<LanguagePrototype>, FrozenDictionary<string, string>> wordMapping = new Dictionary<ProtoId<LanguagePrototype>, FrozenDictionary<string, string>>().ToFrozenDictionary();
    // Will hold generated words and periodically bake them into the wordMapping.
    // Prevents ESL's from DOS by forcing rebuilds
    [DataField(required: false)]
    public FrozenDictionary<ProtoId<LanguagePrototype>, Dictionary<string, string>> wordMappingCache = new Dictionary<ProtoId<LanguagePrototype>, Dictionary<string, string>>().ToFrozenDictionary();
    [DataField(required: false)]
    public TimeSpan unbakedTime = TimeSpan.Zero;
}
[Prototype]
public sealed partial class LanguagePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = String.Empty;

    [DataField("words")]
    public Dictionary<string, string> Words { get; set; } = new();

    // if a word is not in the dictionary it will have a new word generated out of groups
    [DataField("phonetics")]
    public string[] GenerationGroups;


    [DataField("lengthPhoneticsRatio")]
    public float lengthRatio = 0.5f;

    [DataField("chatIdentifier")]
    public string chatIdentifier = string.Empty;
}
