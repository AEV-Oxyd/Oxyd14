using System;
using System.Linq;
using System.Text;
using Content.Shared._Oxyd.NeoTheology;
using NUnit.Framework;

namespace Content.Tests.Shared._Oxyd.NeoTheology;

[TestFixture]
[TestOf(typeof(LitanyPhraseParser))]
public sealed class LitanyPhraseParserTest
{
    [Test]
    public void Normalize_TrimsAndAppliesNfc()
    {
        var decomposed = "a\u0301";
        var spoken = "  " + decomposed + "  ";
        Assert.That(LitanyPhraseParser.Normalize(spoken), Is.EqualTo("á"));
        Assert.That(LitanyPhraseParser.TryMatchExact(spoken, "á"));
    }

    [Test]
    public void Match_IsCaseAndPunctuationSensitive()
    {
        Assert.That(LitanyPhraseParser.TryMatchExact("Semper invicta.", "Semper invicta."));
        Assert.That(LitanyPhraseParser.TryMatchExact("semper invicta.", "Semper invicta."), Is.False);
        Assert.That(LitanyPhraseParser.TryMatchExact("Semper invicta", "Semper invicta."), Is.False);
    }

    [Test]
    public void ParseTarget_CapturesLiteralPrefixAndSuffix()
    {
        const string phrase = "Excommunicatio [Target human]!";
        Assert.That(LitanyPhraseParser.TryParseTargetName("Excommunicatio Jane Doe!", phrase, out var name));
        Assert.That(name, Is.EqualTo("Jane Doe"));
        Assert.That(LitanyPhraseParser.TryParseTargetName("Excommunicatio Jane Doe", phrase, out _), Is.False);
        Assert.That(LitanyPhraseParser.TryParseTargetName("excommunicatio Jane Doe!", phrase, out _), Is.False);
        Assert.That(LitanyPhraseParser.TryParseTargetName("Excommunicatio !", phrase, out _), Is.False);
    }

    [Test]
    public void BookDuration_UsesScalarCountWithMinimum()
    {
        Assert.That(LitanyPhraseParser.BookChantDuration("Hi"), Is.EqualTo(TimeSpan.FromSeconds(0.25)));
        Assert.That(LitanyPhraseParser.BookChantDuration("Semper invicta."),
            Is.EqualTo(TimeSpan.FromSeconds(0.025 * "Semper invicta.".EnumerateRunes().Count())));
    }

    [Test]
    public void ScalarCount_RejectsOverCapInIsolation()
    {
        var tooLong = new string('a', LitanyPhraseParser.MaxPhraseScalars + 1);
        Assert.That(LitanyPhraseParser.ScalarCount(tooLong), Is.GreaterThan(LitanyPhraseParser.MaxPhraseScalars));
        Assert.That(LitanyPhraseParser.Normalize(tooLong.Normalize(NormalizationForm.FormC)).Length, Is.GreaterThan(0));
    }

    [Test]
    public void ScalarCount_CountsUnicodeScalarsRatherThanUtf16CodeUnits()
    {
        const string phrase = "A😀";

        Assert.That(phrase.Length, Is.EqualTo(3));
        Assert.That(LitanyPhraseParser.ScalarCount(phrase), Is.EqualTo(2));
    }

    [Test]
    public void ParseTarget_RejectsPlaceholderInjection()
    {
        const string phrase = "Excommunicatio [Target human]!";

        Assert.That(
            LitanyPhraseParser.TryParseTargetName("Excommunicatio Jane [Target human] Doe!", phrase, out _),
            Is.False);
    }

    [Test]
    public void ParseTarget_RejectsWhitespaceAndMultiplePlaceholders()
    {
        Assert.That(
            LitanyPhraseParser.TryParseTargetName("Excommunicatio !", "Excommunicatio [Target human]!", out _),
            Is.False);
        Assert.That(
            LitanyPhraseParser.TryParseTargetName(
                "Excommunicatio Jane Doe!",
                "Excommunicatio [Target human] and [Target human]!",
                out _),
            Is.False);
    }
}
