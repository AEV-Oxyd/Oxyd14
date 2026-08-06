using System.Collections.Frozen;
using System.Linq;
using System.Text;
using Content.Shared._Oxyd;
using Content.Shared.Chat;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Chat.Systems;

public record struct MessageBlock(string raw, string unspoken, ProtoId<LanguagePrototype> language);

public sealed class MessageData(
    string raw,
    List<MessageBlock> messageBlocks,
    HashSet<ProtoId<LanguagePrototype>> containedLanguages,
    InGameICChatType category = InGameICChatType.Speak,
    EntityUid? speaker = null);

public sealed partial class ChatSystem
{
    [Dependency] private IGameTiming timing = default!;
    private LanguageDataCoreComponent data => Single<LanguageDataCoreComponent>().Comp;
    public void LanguageInitialize()
    {
        var single = Spawn(null, MapCoordinates.Nullspace);
        EnsureComp<LanguageDataCoreComponent>(single);
        LanguagePrototypesInitialize();
    }

    public void LanguagePrototypesInitialize()
    {
        var newKeyMap = new Dictionary<string, ProtoId<LanguagePrototype>>();
        var newWordMap = new Dictionary<ProtoId<LanguagePrototype>, FrozenDictionary<string, string>>();
        var newWordCache = new Dictionary<ProtoId<LanguagePrototype>, Dictionary<string, string>>();
        foreach (var lang in ProtoMan.EnumeratePrototypes<LanguagePrototype>())
        {
            newKeyMap[lang.chatIdentifier] = lang.ID;
            newWordMap[lang.ID] = lang.Words.ToFrozenDictionary();
            newWordCache[lang.ID] = new();
        }
        data.keyMapping = newKeyMap.ToFrozenDictionary();
        data.wordMapping = newWordMap.ToFrozenDictionary();
        data.wordMappingCache = newWordCache.ToFrozenDictionary();
        data.unbakedTime = TimeSpan.Zero;
    }

    public void LanguageCachesBake(int count)
    {
        Log.Info($"Baking language caches. Count : {count} at {timing.RealTime}");
        var newWordMap = new Dictionary<ProtoId<LanguagePrototype>, FrozenDictionary<string, string>>();
        var newCache = new Dictionary<ProtoId<LanguagePrototype>, Dictionary<string, string>>();
        foreach (var lang in ProtoMan.EnumeratePrototypes<LanguagePrototype>())
        {
            newWordMap[lang.ID] = lang.Words.Concat(data.wordMappingCache[lang.ID]).ToFrozenDictionary();
            newCache[lang.ID] = new();
        }
        data.wordMapping = newWordMap.ToFrozenDictionary();
        data.wordMappingCache = newCache.ToFrozenDictionary();
        Log.Info($"Finished baking language caches at {timing.RealTime}");
    }

    public MessageData BuildMessage(string message,
        InGameICChatType category,
        ProtoId<LanguagePrototype> defaultLang,
        HashSet<ProtoId<LanguagePrototype>> validLanguages,
        EntityUid? speaker = null)
    {
        var blocks = BuildLanguageBlocks(message, defaultLang, validLanguages);
        HashSet<ProtoId<LanguagePrototype>> usedLangs = new();
        foreach (var block in blocks)
        {
            usedLangs.Add(block.language);
        }
        return new MessageData(message, blocks,usedLangs, category, speaker);
    }

    /// <summary>
    /// Message is assumed to have already been processed for the radio string
    /// </summary>
    /// <param name="message"></param>
    /// <param name="defaultLang"></param>
    /// <returns></returns>
    public List<MessageBlock> BuildLanguageBlocks(string message, ProtoId<LanguagePrototype> defaultLang, HashSet<ProtoId<LanguagePrototype>> validLanguages)
    {
        var ret = new List<MessageBlock>();
        var spanLooker = data.keyMapping.GetAlternateLookup<ReadOnlySpan<char>>();
        var idString = new StringBuilder(8);
        // whilest you might want to make this areadonly span we need the strings for the RAW msg in the blocks . SPCR 2026
        var segmented = message.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segmented)
        {
            var ss = segment.AsSpan();
            var sliceEnd = ss.IndexOf(' ');
            var block = new MessageBlock(segment, string.Empty, defaultLang);
            if(spanLooker.TryGetValue(ss.Slice(0, sliceEnd), out var langId))
            {
                if (validLanguages.Contains(langId))
                {
                    block.language = langId;
                }
                block.unspoken = CreateNonSpeakerMessage(ss.Slice(sliceEnd), block.language);
            }
            else
            {
                block.unspoken = CreateNonSpeakerMessage(ss, defaultLang);
            }
        }
        return ret;
    }

    public string generateWord(string original, ProtoId<LanguagePrototype> language)
    {
        var phonetics = ProtoMan.Index<LanguagePrototype>(language);
        var rand = new Random();
        var w =  rand.GetItems<string>(phonetics.GenerationGroups, (int)(original.Length * phonetics.lengthRatio)+1).Aggregate(String.Concat);
        data.wordMappingCache[language].Add(original, w);
        return w;
    }

    public string getWord(ReadOnlySpan<char> original, ProtoId<LanguagePrototype> language)
    {
        var map = data.wordMapping[language].GetAlternateLookup<ReadOnlySpan<char>>();
        var cache = data.wordMappingCache[language].GetAlternateLookup<ReadOnlySpan<char>>();
        if (map.TryGetValue(original, out var w))
            return w;
        if (cache.TryGetValue(original, out var w2))
            return w2;
        return generateWord(original.ToString(), language);
    }

    /// <summary>
    /// Assumed to be a message stripped of all radio+languge markers
    /// </summary>
    /// <param name="message"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    public string CreateNonSpeakerMessage(ReadOnlySpan<char> message, ProtoId<LanguagePrototype> language)
    {
        var build = new StringBuilder(message.Length);
        var splits = message.Split(' ');
        foreach (var indices in splits)
            build.Append(getWord(message.Slice(indices.Start.Value, indices.End.Value), language));
        return build.ToString();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        data.unbakedTime += TimeSpan.FromSeconds(frameTime);
        if (data.unbakedTime > TimeSpan.FromMinutes(5))
        {
            var count = data.wordMappingCache.Values.Sum(x => x.Count);
            if (count > 100)
                LanguageCachesBake(count);
            data.unbakedTime = TimeSpan.Zero;
        }
    }
}
