using System.Globalization;
using System.Text;
using Content.Shared.Security;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

// =============================================================================
// STATION RECORDS MANAGER (/bin/station_records.nte -> /records/station_records.db)
// =============================================================================

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationRecordsProgramComponent : Component
{
    public const string CacheFilePath = "/records/station_records.db";

    [DataField, AutoNetworkedField]
    public string SelectedKey = string.Empty;

    [DataField, AutoNetworkedField]
    public string StatusMessage = string.Empty;
}

[Serializable, NetSerializable]
public sealed class StationRecordEntryDto
{
    public string Key { get; }
    public string Name { get; }
    public string JobTitle { get; }
    public string JobPrototype { get; }
    public string JobIcon { get; }
    public int Age { get; }
    public string Species { get; }
    public string Gender { get; }
    public string Fingerprint { get; }
    public string Dna { get; }
    public string CriminalStatus { get; }

    public StationRecordEntryDto(
        string key,
        string name,
        string jobTitle,
        string jobPrototype,
        string jobIcon,
        int age,
        string species,
        string gender,
        string fingerprint,
        string dna,
        string criminalStatus)
    {
        Key = key;
        Name = name;
        JobTitle = jobTitle;
        JobPrototype = jobPrototype;
        JobIcon = jobIcon;
        Age = age;
        Species = species;
        Gender = gender;
        Fingerprint = fingerprint;
        Dna = dna;
        CriminalStatus = criminalStatus;
    }
}

[Serializable, NetSerializable]
public enum StationRecordsAction : byte
{
    Refresh,
    Select,
    SaveEntry,
    CreateEntry,
    DeleteEntry
}

[Serializable, NetSerializable]
public sealed class StationRecordsActionMessage : BoundUserInterfaceMessage
{
    public StationRecordsAction Action { get; }
    public string Key { get; }
    public string Name { get; }
    public string JobTitle { get; }
    public int Age { get; }
    public string Species { get; }
    public string Gender { get; }
    public string Fingerprint { get; }
    public string Dna { get; }

    public StationRecordsActionMessage(
        StationRecordsAction action,
        string key = "",
        string name = "",
        string jobTitle = "",
        int age = 25,
        string species = "Human",
        string gender = "Epicene",
        string fingerprint = "",
        string dna = "")
    {
        Action = action;
        Key = key;
        Name = name;
        JobTitle = jobTitle;
        Age = age;
        Species = species;
        Gender = gender;
        Fingerprint = fingerprint;
        Dna = dna;
    }
}

[Serializable, NetSerializable]
public sealed class StationRecordsUiState : BoundUserInterfaceState
{
    public string StationName { get; }
    public bool Online { get; }
    public bool CanEdit { get; }
    public string StatusMessage { get; }
    public TimeSpan? CacheTimestamp { get; }
    public string SelectedKey { get; }
    public List<StationRecordEntryDto> Entries { get; }

    public StationRecordsUiState(
        string stationName,
        bool online,
        bool canEdit,
        string statusMessage,
        TimeSpan? cacheTimestamp,
        string selectedKey,
        List<StationRecordEntryDto> entries)
    {
        StationName = stationName;
        Online = online;
        CanEdit = canEdit;
        StatusMessage = statusMessage;
        CacheTimestamp = cacheTimestamp;
        SelectedKey = selectedKey;
        Entries = entries;
    }
}

// =============================================================================
// CRIMINAL RECORDS MANAGER (/bin/criminal_records.nte -> /records/criminal_records.db)
// =============================================================================

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CriminalRecordsProgramComponent : Component
{
    public const string CacheFilePath = "/records/criminal_records.db";

    [DataField, AutoNetworkedField]
    public string SelectedKey = string.Empty;

    [DataField, AutoNetworkedField]
    public string StatusMessage = string.Empty;
}

[Serializable, NetSerializable]
public sealed class CrimeHistoryEntryDto
{
    public int Index { get; }
    public string TimestampText { get; }
    public string Line { get; }
    public string Initiator { get; }

    public CrimeHistoryEntryDto(int index, string timestampText, string line, string initiator)
    {
        Index = index;
        TimestampText = timestampText;
        Line = line;
        Initiator = initiator;
    }
}

[Serializable, NetSerializable]
public sealed class CriminalRecordEntryDto
{
    public string Key { get; }
    public string Name { get; }
    public string JobTitle { get; }
    public SecurityStatus Status { get; }
    public string Reason { get; }
    public string Initiator { get; }
    public List<CrimeHistoryEntryDto> History { get; }

    public CriminalRecordEntryDto(
        string key,
        string name,
        string jobTitle,
        SecurityStatus status,
        string reason,
        string initiator,
        List<CrimeHistoryEntryDto> history)
    {
        Key = key;
        Name = name;
        JobTitle = jobTitle;
        Status = status;
        Reason = reason;
        Initiator = initiator;
        History = history;
    }
}

[Serializable, NetSerializable]
public enum CriminalRecordsAction : byte
{
    Refresh,
    Select,
    SetStatus,
    AddHistory,
    DeleteHistory
}

[Serializable, NetSerializable]
public sealed class CriminalRecordsActionMessage : BoundUserInterfaceMessage
{
    public CriminalRecordsAction Action { get; }
    public string Key { get; }
    public SecurityStatus Status { get; }
    public string Text { get; }
    public int HistoryIndex { get; }

    public CriminalRecordsActionMessage(
        CriminalRecordsAction action,
        string key = "",
        SecurityStatus status = SecurityStatus.None,
        string text = "",
        int historyIndex = -1)
    {
        Action = action;
        Key = key;
        Status = status;
        Text = text;
        HistoryIndex = historyIndex;
    }
}

[Serializable, NetSerializable]
public sealed class CriminalRecordsUiState : BoundUserInterfaceState
{
    public bool Online { get; }
    public bool CanEdit { get; }
    public string StatusMessage { get; }
    public TimeSpan? CacheTimestamp { get; }
    public string SelectedKey { get; }
    public List<CriminalRecordEntryDto> Entries { get; }

    public CriminalRecordsUiState(
        bool online,
        bool canEdit,
        string statusMessage,
        TimeSpan? cacheTimestamp,
        string selectedKey,
        List<CriminalRecordEntryDto> entries)
    {
        Online = online;
        CanEdit = canEdit;
        StatusMessage = statusMessage;
        CacheTimestamp = cacheTimestamp;
        SelectedKey = selectedKey;
        Entries = entries;
    }
}

// =============================================================================
// SANDBOX-SAFE RECORD DB CODEC (NO PARAMS COLLECTION EXPRESSIONS)
// =============================================================================

public static class RecordsDbCodec
{
    public static string SanitizeField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\u001E', ' ')
            .Replace('\u001F', ' ')
            .Trim();
    }

    public static string EncodeStationRecordRow(
        string name,
        string jobTitle,
        string jobPrototype,
        string jobIcon,
        int age,
        string species,
        string gender,
        string fingerprint,
        string dna)
    {
        var sb = new StringBuilder(128);
        sb.Append(SanitizeField(name)).Append('\t');
        sb.Append(SanitizeField(jobTitle)).Append('\t');
        sb.Append(SanitizeField(jobPrototype)).Append('\t');
        sb.Append(SanitizeField(jobIcon)).Append('\t');
        sb.Append(age.ToString(CultureInfo.InvariantCulture)).Append('\t');
        sb.Append(SanitizeField(species)).Append('\t');
        sb.Append(SanitizeField(gender)).Append('\t');
        sb.Append(SanitizeField(fingerprint)).Append('\t');
        sb.Append(SanitizeField(dna));
        return sb.ToString();
    }

    public static bool TryDecodeStationRecordRow(
        string key,
        string raw,
        string criminalStatus,
        out StationRecordEntryDto entry)
    {
        var cols = raw.Split('\t');
        if (cols.Length < 2)
        {
            entry = default!;
            return false;
        }

        var age = cols.Length > 4 && int.TryParse(cols[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAge)
            ? parsedAge
            : 25;

        entry = new StationRecordEntryDto(
            key,
            cols[0],
            cols[1],
            cols.Length > 2 ? cols[2] : string.Empty,
            cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3]) ? cols[3] : "JobIconUnknown",
            age,
            cols.Length > 5 && !string.IsNullOrWhiteSpace(cols[5]) ? cols[5] : "Human",
            cols.Length > 6 && !string.IsNullOrWhiteSpace(cols[6]) ? cols[6] : "Epicene",
            cols.Length > 7 ? cols[7] : string.Empty,
            cols.Length > 8 ? cols[8] : string.Empty,
            criminalStatus);
        return true;
    }

    public static string EncodeCriminalRecordRow(
        string name,
        string jobTitle,
        SecurityStatus status,
        string reason,
        string initiator,
        IReadOnlyList<CrimeHistoryEntryDto> history)
    {
        var sb = new StringBuilder(128);
        sb.Append(SanitizeField(name)).Append('\t');
        sb.Append(SanitizeField(jobTitle)).Append('\t');
        sb.Append(status.ToString()).Append('\t');
        sb.Append(SanitizeField(reason)).Append('\t');
        sb.Append(SanitizeField(initiator)).Append('\t');

        for (var i = 0; i < history.Count; i++)
        {
            if (i > 0)
                sb.Append('\u001E');
            var h = history[i];
            sb.Append(SanitizeField(h.TimestampText)).Append('\u001F');
            sb.Append(SanitizeField(h.Line)).Append('\u001F');
            sb.Append(SanitizeField(h.Initiator));
        }

        return sb.ToString();
    }

    public static bool TryDecodeCriminalRecordRow(string key, string raw, out CriminalRecordEntryDto entry)
    {
        var cols = raw.Split('\t');
        if (cols.Length < 3)
        {
            entry = default!;
            return false;
        }

        Enum.TryParse<SecurityStatus>(cols[2], true, out var status);
        var reason = cols.Length > 3 ? cols[3] : string.Empty;
        var initiator = cols.Length > 4 ? cols[4] : string.Empty;
        var history = new List<CrimeHistoryEntryDto>();

        if (cols.Length > 5 && !string.IsNullOrEmpty(cols[5]))
        {
            var items = cols[5].Split('\u001E');
            for (var i = 0; i < items.Length; i++)
            {
                if (string.IsNullOrEmpty(items[i]))
                    continue;

                var parts = items[i].Split('\u001F');
                var ts = parts.Length > 0 ? parts[0] : "00:00:00";
                var line = parts.Length > 1 ? parts[1] : items[i];
                var init = parts.Length > 2 ? parts[2] : string.Empty;
                history.Add(new CrimeHistoryEntryDto(i, ts, line, init));
            }
        }

        entry = new CriminalRecordEntryDto(key, cols[0], cols[1], status, reason, initiator, history);
        return true;
    }
}
