using System.IO;
using Content.Shared.Mobs.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Oxyd.Economy;

/// <summary>
/// Eris <c>get_artwork_name</c> and statue <c>make_art_review</c>.
/// Word lists are the CEV-Eris <c>strings/artist_strings</c> tables.
/// </summary>
public sealed partial class OxydArtworkTextSystem : EntitySystem
{
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private readonly Dictionary<string, string[]> _lines = new();

    public (string Name, string Description) NameAndDescribe(EntityUid origin, string kind, string artist)
    {
        var name = Capitalize(ArtworkName(origin));
        var description = kind == "statue" ? StatueStory(origin) : GeneralReview();
        if (!string.IsNullOrWhiteSpace(artist))
            description += $" Signed {artist}.";
        return (name, description);
    }

    private string ArtworkName(EntityUid origin)
    {
        var crew = CrewName(origin);
        return _random.Next(3) switch
        {
            0 => $"the {Pick("big", "terrifying", "mysterious", "fantastic", "secret", "haunting")} secret of {crew}",
            1 => $"the {crew}s {Pick("trip", "journey")} to the {PickFile("descriptors/art_locations.txt")}",
            _ => $"the {Pick("heart", "soul", "honor", "beauty", "feet", "true face", "true form")} of {crew}",
        };
    }

    private string StatueStory(EntityUid origin)
    {
        var type = PickFile("names/art_types_statue.txt");
        var opening = _random.Prob(0.5f)
            ? $"{Pick("This", "The")} {type} {PickFile("descriptors/art_type_actions_statue.txt")} {PickFile("descriptors/art_type_ofs_statue.txt")}"
            : $"{PickFile("descriptors/art_sculpting_method_descriptors.txt")} {Pick("this", "the")} {type} is {PickFile("descriptors/art_type_ofs_statue.txt")}";
        return $"{opening} {ArtworkDescription(origin)}";
    }

    private string ArtworkDescription(EntityUid origin)
    {
        var subject = _random.Prob(0.5f) ? PickFile("names/art_objectsubject.txt") : CrewName(origin);
        var thing = _random.Prob(0.5f) ? PickFile("names/art_objectsubject.txt") : CrewName(origin);
        var place = PickFile("descriptors/art_locations.txt");
        return _random.Next(3) switch
        {
            0 => $"A {subject} {PickFile("descriptors/art_subject_actions.txt")} a {thing}.",
            1 => $"A {thing} {PickFile("descriptors/art_object_actions.txt")} by a {subject}.",
            _ => $"The {PickFile("names/art_types_statue.txt")} {PickFile("descriptors/art_locations_descriptors.txt")} {PickFile("descriptors/art_locations_specifics.txt")} a {place}.",
        };
    }

    private string GeneralReview()
    {
        return $"A {PickFile("descriptors/art_style.txt")} {PickFile("descriptors/art_types.txt")} {PickFile("descriptors/art_sculpting_method.txt")}. {Pick("Inspires", "Infuses")} {Pick("fear", "joy", "laughter", "sadness", "respect", "terror", "vigor", "encouragement")} in those who look at it.";
    }

    private string CrewName(EntityUid origin)
    {
        var names = new List<string>();
        foreach (var uid in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(origin).Coordinates, 32f))
            names.Add(Name(uid));

        return names.Count == 0 ? "a stranger" : _random.Pick(names);
    }

    private string Pick(params string[] options) => _random.Pick(options);

    private string PickFile(string relative)
    {
        var lines = Lines(relative);
        return lines.Length == 0 ? "art" : _random.Pick(lines);
    }

    private string[] Lines(string relative)
    {
        if (_lines.TryGetValue(relative, out var cached))
            return cached;

        var path = new ResPath($"/_Oxyd/ArtistStrings/{relative}");
        if (!_resources.TryContentFileRead(path, out var stream))
            return _lines[relative] = [];

        using var reader = new StreamReader(stream);
        var list = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length > 0)
                list.Add(line);
        }

        return _lines[relative] = list.ToArray();
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
