using System.Text;
using Content.Shared._Oxyd;
using Robust.Shared.Prototypes;

namespace Content.Shared.Chat;

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
        // Follow the order of the message blocks.
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

    public string GetMessage(HashSet<ProtoId<LanguagePrototype>> validLanguages, out int key)
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
