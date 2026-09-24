using System.Globalization;
using System.Linq;
using System.Text;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Canonical phrase matching for litanies. Normalization is limited to trim and
/// Unicode NFC; matching stays case- and punctuation-sensitive.
/// </summary>
public static class LitanyPhraseParser
{
    public const string TargetPlaceholder = "[Target human]";
    public const int MaxPhraseScalars = 1024;

    public static string Normalize(string phrase)
    {
        return phrase.Trim().Normalize(NormalizationForm.FormC);
    }

    public static int ScalarCount(string phrase)
    {
        return phrase.EnumerateRunes().Count();
    }

    public static TimeSpan BookChantDuration(string finalPhrase)
    {
        var scalars = ScalarCount(finalPhrase);
        var seconds = Math.Max(0.25d, 0.025d * scalars);
        return TimeSpan.FromSeconds(seconds);
    }

    public static bool TryMatchExact(string spoken, string prototypePhrase)
    {
        return Normalize(spoken) == Normalize(prototypePhrase);
    }

    /// <summary>
    /// Parses an addressed chant as literal prefix + one captured name + literal suffix,
    /// anchored to the complete normalized string. No wildcards or user regex.
    /// </summary>
    public static bool TryParseTargetName(string spoken, string prototypePhrase, out string targetName)
    {
        targetName = string.Empty;
        var phrase = Normalize(prototypePhrase);
        var text = Normalize(spoken);
        var index = phrase.IndexOf(TargetPlaceholder, StringComparison.Ordinal);
        if (index < 0)
            return false;

        if (phrase.IndexOf(TargetPlaceholder, index + TargetPlaceholder.Length, StringComparison.Ordinal) >= 0)
            return false;

        var prefix = phrase[..index];
        var suffix = phrase[(index + TargetPlaceholder.Length)..];
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var capturedLength = text.Length - prefix.Length - suffix.Length;
        if (capturedLength <= 0)
            return false;

        targetName = text.Substring(prefix.Length, capturedLength);
        return !string.IsNullOrWhiteSpace(targetName) &&
               !targetName.Contains(TargetPlaceholder, StringComparison.Ordinal);
    }
}
