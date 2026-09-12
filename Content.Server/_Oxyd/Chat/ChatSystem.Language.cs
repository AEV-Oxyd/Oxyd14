using System.Collections.Frozen;
using System.Linq;
using System.Text;
using Content.Server.GameTicking.Events;
using Content.Shared._Oxyd;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Chat.Systems;

public record struct MessageBlock(string raw, string unspoken, ProtoId<LanguagePrototype> language);

public sealed record MessageData(
    EntityUid speaker,
    ChatTransmitRange range,
    InGameICChatType category,
    string raw,
    List<MessageBlock> messageBlocks,
    List<ProtoId<LanguagePrototype>> containedLanguages,
    Dictionary<int, string> builtMessages) // hash of concatenated language ID's -> message
{

    public static int hashFromList(List<ProtoId<LanguagePrototype>> list)
    {
        var hash = 0;
        foreach (var lang in list)
            hash = (hash * 397) ^ lang.Id.GetHashCode();
        return hash;
    }
    public int GetLanguageHash(HashSet<ProtoId<LanguagePrototype>> validLanguages)
    {
        int hash = 0;
        int hits = 0;
        // we follow order of messageData block
        foreach (var lang in containedLanguages)
        {
            if (!validLanguages.Contains(lang))
                continue;
            hash = (hash * 397) ^ lang.Id.GetHashCode();
            if (++hits >= validLanguages.Count)
                break;
        }

        return hash;
    }

    public string GetMessage(HashSet<ProtoId<LanguagePrototype>> validLanguages,out int key)
    {

        key = GetLanguageHash(validLanguages);
        if (builtMessages.TryGetValue(key, out var msg))
            return msg;
        var sb = new StringBuilder(128);
        foreach (var block in messageBlocks)
        {
            if (validLanguages.Contains(block.language))
                sb.Append(block.raw);
            else
                sb.Append(block.unspoken);
            sb.Append(' ');
        }
        builtMessages[key] = sb.ToString();
        return sb.ToString();
    }
    
    public string GetMessage(int key)
    {
        return builtMessages[key];
    }
}

public sealed partial class ChatSystem
{
    [Dependency] private IGameTiming timing = default!;
    [Dependency] private EntityQuery<LanguageKnowledgeComponent> langquery = default!;

    private RobustRandom random = new();
    private LanguageDataCoreComponent data => Comp<LanguageDataCoreComponent>(core);

    public ProtoId<LanguagePrototype> standardLanguage = "Universal";

    public char languageMarker = '=';

    [ViewVariables] public EntityUid core = EntityUid.Invalid; 
    
    [SubscribeLocalEvent]
    public void LanguageInitialize(RoundStartingEvent ev)
    {
        random.SetSeed(800853);
        var single = Spawn();
        core = single;
        EnsureComp<LanguageDataCoreComponent>(single);
        LanguagePrototypesInitialize();
    }
    
    [SubscribeLocalEvent]
    public void LanguageInitialize(RoundEndMessageEvent ev)
    {
        core = EntityUid.Invalid;
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

    public MessageData BuildMessage(EntityUid speaker,
        string message,
        ChatTransmitRange range,
        InGameICChatType category,
        ProtoId<LanguagePrototype> defaultLang,
        HashSet<ProtoId<LanguagePrototype>> validLanguages)
    {
        var blocks = BuildLanguageBlocks(message, defaultLang, validLanguages);
        List<ProtoId<LanguagePrototype>> usedLangs = new();
        var stringBuilder = new StringBuilder(message.Length);
        foreach (var block in blocks)
        {
            usedLangs.Add(block.language);
            stringBuilder.Append(block.raw);
        }
        var dict = new Dictionary<int, string>();
        var newRaw = stringBuilder.ToString();
        dict[MessageData.hashFromList(usedLangs)] = newRaw;
        return new MessageData(speaker,range, category, newRaw, blocks,usedLangs, dict);
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
        var mes = message.AsSpan();
        var spanLooker = data.keyMapping.GetAlternateLookup<ReadOnlySpan<char>>();
        // whilest you might want to make this areadonly span we need the strings for the RAW msg in the blocks . SPCR 2026
        var segmented = mes.Split(languageMarker);
        foreach (var segment in segmented)
        {
            var t = mes[segment];
            var sliceEnd = t.IndexOf(' ');
            if (sliceEnd == -1)
            {
                sliceEnd = 0;
            }
            var block = new MessageBlock(string.Empty, string.Empty, defaultLang);
            var identitySlice = t[..sliceEnd];
            if(spanLooker.TryGetValue(identitySlice, out var langId))
            {
                if (validLanguages.Contains(langId))
                {
                    block.language = langId;
                }

                block.raw = t[sliceEnd..].ToString();
                block.unspoken = CreateNonSpeakerMessage(t[sliceEnd..], block.language);
            }
            else
            {
                block.raw = t.ToString();
                block.unspoken = CreateNonSpeakerMessage(t, defaultLang);
            }
            ret.Add(block);
        }
        return ret;
    }

    public string generateWord(string original, ProtoId<LanguagePrototype> language)
    {
        var phonetics = ProtoMan.Index<LanguagePrototype>(language);
        var w =  string.Concat(random.GetItems<string>(phonetics.GenerationGroups, (int)(original.Length * phonetics.lengthRatio)+1));
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
        {
            build.Append(getWord(message[indices], language));
            build.Append(' ');
        }

        return build.ToString();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // happens when ending round , stupid , SPCR 2026
        if (TerminatingOrDeleted(core))
            return;
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
