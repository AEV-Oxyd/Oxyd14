using System.Linq;
using Content.Shared._Oxyd.Skills;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology;

/// <summary>
/// Structural validation for the litany catalog. Used by the server startup check
/// and by integration tests so the same rules cannot drift.
/// </summary>
public static class LitanyCatalogValidator
{
    public const int ExpectedLitanyCount = 60;
    public const int ExpectedFoundationLitanyCount = 23;
    public const int ExpectedDependencyGatedLitanyCount = ExpectedLitanyCount - ExpectedFoundationLitanyCount;

    private static readonly HashSet<LitanyEffectKind> IgnoreStutteringEffects =
    [
        LitanyEffectKind.Relief,
        LitanyEffectKind.Entreaty,
        LitanyEffectKind.Rejection,
    ];

    public static List<string> Validate(
        IPrototypeManager prototypes,
        ILocalizationManager localization)
    {
        var errors = new List<string>();
        var litanies = prototypes.EnumeratePrototypes<LitanyPrototype>().ToList();
        var sets = prototypes.EnumeratePrototypes<LitanySetPrototype>().ToDictionary(s => s.ID);
        var rules = prototypes.EnumeratePrototypes<NeoTheologyRulesPrototype>().Where(r => r.Selected).ToList();

        if (litanies.Count != ExpectedLitanyCount)
            errors.Add($"Expected {ExpectedLitanyCount} litanies, found {litanies.Count}.");

        ValidateCatalogPolicy(litanies, errors);

        if (rules.Count != 1)
            errors.Add($"Exactly one selected NeoTheology rules profile is required; found {rules.Count}.");

        var selectedRules = rules.Count == 1 ? rules[0] : null;
        ValidateRules(selectedRules, errors);

        var phrases = new Dictionary<string, string>(StringComparer.Ordinal);
        var setMembership = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var (setId, set) in sets)
        {
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (var litanyId in set.Litanies)
            {
                if (!members.Add(litanyId.Id))
                    errors.Add($"Set {setId} lists {litanyId.Id} more than once.");

                if (!prototypes.TryIndex<LitanyPrototype>(litanyId, out _))
                    errors.Add($"Set {setId} references unknown litany {litanyId.Id}.");
            }

            setMembership[setId] = members;
        }

        foreach (var litany in litanies)
        {
            ValidateLitany(litany, localization, phrases, setMembership, errors);
        }

        ValidateReachableCosts(litanies, sets, selectedRules, errors);
        return errors;
    }

    private static void ValidateCatalogPolicy(
        IReadOnlyCollection<LitanyPrototype> litanies,
        List<string> errors)
    {
        var availableCount = litanies.Count(l => l.IsAvailable);
        var gatedCount = litanies.Count - availableCount;
        if (availableCount != ExpectedFoundationLitanyCount)
            errors.Add($"Expected {ExpectedFoundationLitanyCount} available foundation litanies, found {availableCount}.");
        if (gatedCount != ExpectedDependencyGatedLitanyCount)
            errors.Add($"Expected {ExpectedDependencyGatedLitanyCount} dependency-gated litanies, found {gatedCount}.");

        var effectGroups = litanies.GroupBy(l => l.Effect).ToList();
        foreach (var group in effectGroups.Where(group => group.Count() > 1))
        {
            var ids = string.Join(", ", group.Select(litany => litany.ID));
            errors.Add($"Effect {group.Key} is represented by multiple litanies: {ids}.");
        }

        var representedEffects = effectGroups.Select(group => group.Key).ToHashSet();
        foreach (var effect in Enum.GetValues<LitanyEffectKind>())
        {
            if (!representedEffects.Contains(effect))
                errors.Add($"Catalog is missing a litany for effect {effect}.");
        }

        foreach (var implemented in LitanyHandlerCatalog.Implemented)
        {
            if (LitanyHandlerCatalog.PendingFoundation.Contains(implemented))
                errors.Add($"Effect {implemented} is listed as both implemented and pending foundation.");
        }

        var availableEffects = litanies
            .Where(litany => litany.IsAvailable)
            .Select(litany => litany.Effect)
            .ToHashSet();
        foreach (var effect in availableEffects)
        {
            if (!LitanyHandlerCatalog.HasExactlyOneRegistration(effect))
                errors.Add($"Available effect {effect} is not registered exactly once in the handler catalog.");
        }

        foreach (var effect in LitanyHandlerCatalog.PendingFoundation)
        {
            if (!availableEffects.Contains(effect))
                errors.Add($"Pending foundation effect {effect} is not represented by an available litany.");
        }
    }

    private static void ValidateRules(NeoTheologyRulesPrototype? rules, List<string> errors)
    {
        if (rules == null)
            return;

        if (!double.IsFinite(rules.BaseHolinessPerMinute) || rules.BaseHolinessPerMinute <= 0)
            errors.Add($"{rules.ID} has an invalid base holiness regeneration value.");
        if (!double.IsFinite(rules.DiscipleCapacity) || rules.DiscipleCapacity < 0)
            errors.Add($"{rules.ID} has an invalid disciple capacity.");
        if (!double.IsFinite(rules.PreacherCapacity) || rules.PreacherCapacity < 0)
            errors.Add($"{rules.ID} has an invalid preacher capacity.");
        if (!double.IsFinite(rules.InquisitorCapacity) || rules.InquisitorCapacity < 0)
            errors.Add($"{rules.ID} has an invalid inquisitor capacity.");
        if (!double.IsFinite(rules.PreacherRegenMultiplier) || rules.PreacherRegenMultiplier <= 0)
            errors.Add($"{rules.ID} has an invalid preacher regeneration multiplier.");
        if (!double.IsFinite(rules.InquisitorRegenMultiplier) || rules.InquisitorRegenMultiplier <= 0)
            errors.Add($"{rules.ID} has an invalid inquisitor regeneration multiplier.");
        if (!double.IsFinite(rules.DebitTolerance) || rules.DebitTolerance < 0)
            errors.Add($"{rules.ID} has an invalid debit tolerance.");
    }

    public static List<string> ValidateMissingHandler(LitanyPrototype litany)
    {
        var errors = new List<string>();
        if (litany.IsAvailable && !LitanyHandlerCatalog.HasExactlyOneRegistration(litany.Effect))
            errors.Add($"{litany.ID} is enabled without exactly one handler or pending-foundation registration.");
        if (LitanyHandlerCatalog.Implemented.Contains(litany.Effect) &&
            LitanyHandlerCatalog.PendingFoundation.Contains(litany.Effect))
            errors.Add($"{litany.Effect} is listed as both implemented and pending.");
        return errors;
    }

    private static void ValidateLitany(
        LitanyPrototype litany,
        ILocalizationManager localization,
        Dictionary<string, string> phrases,
        Dictionary<string, HashSet<string>> setMembership,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(litany.ID))
            errors.Add("A litany has an empty prototype ID.");

        if (string.IsNullOrWhiteSpace(litany.Phrase))
            errors.Add($"{litany.ID} has an empty phrase.");
        else
        {
            if (litany.Phrase.Contains('<') || litany.Phrase.Contains('\n'))
                errors.Add($"{litany.ID} phrase contains forbidden formatting.");
            if (LitanyPhraseParser.ScalarCount(litany.Phrase) > LitanyPhraseParser.MaxPhraseScalars)
                errors.Add($"{litany.ID} phrase exceeds {LitanyPhraseParser.MaxPhraseScalars} scalars.");

            var normalized = LitanyPhraseParser.Normalize(litany.Phrase);
            var targetPlaceholder = normalized.IndexOf(LitanyPhraseParser.TargetPlaceholder, StringComparison.Ordinal);
            if (targetPlaceholder >= 0 &&
                normalized.IndexOf(
                    LitanyPhraseParser.TargetPlaceholder,
                    targetPlaceholder + LitanyPhraseParser.TargetPlaceholder.Length,
                    StringComparison.Ordinal) >= 0)
            {
                errors.Add($"{litany.ID} phrase contains multiple target placeholders.");
            }

            if (phrases.TryGetValue(normalized, out var other))
                errors.Add($"Duplicate phrase between {other} and {litany.ID}.");
            else
                phrases[normalized] = litany.ID;
        }

        if (!localization.HasString(litany.Name.Id))
            errors.Add($"{litany.ID} missing localization {litany.Name.Id}.");
        if (!localization.HasString(litany.Description.Id))
            errors.Add($"{litany.ID} missing localization {litany.Description.Id}.");

        if (!double.IsFinite(litany.Cost) || litany.Cost < 0)
            errors.Add($"{litany.ID} has a nonfinite or negative cost.");
        if (!float.IsFinite(litany.Range) || litany.Range < 0)
            errors.Add($"{litany.ID} has a nonfinite or negative range.");
        if ((litany.TargetMode is LitanyTargetMode.Self or LitanyTargetMode.None) && litany.Range != 0)
            errors.Add($"{litany.ID} has a nonzero range for a non-targeted litany.");
        if (litany.ExtraDelay < TimeSpan.Zero)
            errors.Add($"{litany.ID} has a negative extra delay.");
        if (string.IsNullOrWhiteSpace(litany.SourcePath))
            errors.Add($"{litany.ID} has no source path.");
        if (litany.EffectDuration < TimeSpan.Zero)
            errors.Add($"{litany.ID} has a negative effect duration.");

        var hasCooldown = litany.CooldownDuration > TimeSpan.Zero;
        if (hasCooldown)
        {
            if (string.IsNullOrWhiteSpace(litany.CooldownKey) || litany.CooldownScope == LitanyCooldownScope.None)
                errors.Add($"{litany.ID} has a cooldown duration without a key/scope.");
        }
        else if (!string.IsNullOrEmpty(litany.CooldownKey) || litany.CooldownScope != LitanyCooldownScope.None)
        {
            errors.Add($"{litany.ID} has cooldown metadata without a positive duration.");
        }

        if (litany.IgnoreStuttering != IgnoreStutteringEffects.Contains(litany.Effect))
            errors.Add($"{litany.ID} ignoreStuttering does not match the declared stutter exceptions.");

        if (litany.Enabled)
        {
            if (litany.Dependency != NeoTheologyDependency.None)
                errors.Add($"{litany.ID} is enabled while still dependency-gated.");
            if (litany.UnavailableReason != null)
                errors.Add($"{litany.ID} is enabled but still has an unavailable reason.");
        }
        else
        {
            if (litany.Dependency == NeoTheologyDependency.None)
                errors.Add($"{litany.ID} is disabled without a named dependency.");
            if (litany.UnavailableReason is not { } reason || !localization.HasString(reason.Id))
                errors.Add($"{litany.ID} is disabled without a localized unavailable reason.");
        }

        errors.AddRange(ValidateMissingHandler(litany));

        if (litany.GrantedBy.Count == 0)
            errors.Add($"{litany.ID} is not granted by any litany set.");

        var grantIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in litany.GrantedBy)
        {
            if (!grantIds.Add(grant.Id))
                errors.Add($"{litany.ID} lists grant set {grant.Id} more than once.");

            if (!setMembership.TryGetValue(grant.Id, out var members))
            {
                errors.Add($"{litany.ID} grantedBy unknown set {grant.Id}.");
                continue;
            }

            if (!members.Contains(litany.ID))
                errors.Add($"{litany.ID} lists grant set {grant.Id} but that set does not include it.");
        }

        foreach (var (setId, members) in setMembership)
        {
            if (members.Contains(litany.ID) && litany.GrantedBy.All(g => g.Id != setId))
                errors.Add($"Set {setId} includes {litany.ID} but the litany does not list that grant.");
        }

        ValidateParameters(litany, errors);
    }

    private static void ValidateParameters(LitanyPrototype litany, List<string> errors)
    {
        var parameters = litany.Parameters;
        var allowsHealing = litany.Effect is LitanyEffectKind.Relief
            or LitanyEffectKind.SoulHunger
            or LitanyEffectKind.HandOfMercy
            or LitanyEffectKind.Convalescence
            or LitanyEffectKind.Succour;
        var allowsSkills = litany.Effect is LitanyEffectKind.GraceOfPerseverance
            or LitanyEffectKind.UpholdHolyWord;

        if (parameters != null)
        {
            var parameterFamilies = (parameters.Healing != null ? 1 : 0)
                                    + (parameters.Skills != null ? 1 : 0)
                                    + (parameters.Machine != null ? 1 : 0)
                                    + (parameters.Group != null ? 1 : 0);
            if (parameterFamilies == 0)
                errors.Add($"{litany.ID} has an empty parameter block.");
            if (parameters.Healing != null && !allowsHealing)
                errors.Add($"{litany.ID} has unexpected healing parameters.");
            if (parameters.Skills != null && !allowsSkills)
                errors.Add($"{litany.ID} has unexpected skill parameters.");
            if (parameters.Machine != null)
                errors.Add($"{litany.ID} has unsupported machine parameters.");
            if (parameters.Group != null)
                errors.Add($"{litany.ID} has unsupported group parameters.");

            if (parameters.Healing is { } healing)
            {
                if (!float.IsFinite(healing.Brute) || healing.Brute < 0)
                    errors.Add($"{litany.ID} has an invalid brute healing value.");
                if (!float.IsFinite(healing.Heat) || healing.Heat < 0)
                    errors.Add($"{litany.ID} has an invalid heat healing value.");
                if (!float.IsFinite(healing.Asphyxiation) || healing.Asphyxiation < 0)
                    errors.Add($"{litany.ID} has an invalid asphyxiation healing value.");
            }
        }

        switch (litany.Effect)
        {
            case LitanyEffectKind.Relief:
            case LitanyEffectKind.HandOfMercy:
                if (parameters?.Healing?.Analgesia is null)
                    errors.Add($"{litany.ID} requires analgesia parameters.");
                break;
            case LitanyEffectKind.SoulHunger:
                if (parameters?.Healing is null || parameters.Healing.Heat <= 0)
                    errors.Add($"{litany.ID} requires a Heat injury parameter.");
                break;
            case LitanyEffectKind.Convalescence:
            case LitanyEffectKind.Succour:
                if (parameters?.Healing is null)
                    errors.Add($"{litany.ID} requires healing parameters.");
                break;
            case LitanyEffectKind.GraceOfPerseverance:
            case LitanyEffectKind.UpholdHolyWord:
                if (parameters?.Skills?.Amounts is not { Count: > 0 })
                    errors.Add($"{litany.ID} requires skill parameters.");
                break;
        }

        if (parameters?.Skills is { Amounts.Count: > 0 } skills)
        {
            foreach (var skill in skills.Amounts.Keys)
            {
                if (skill.Id is not ("Mec" or "Cog" or "Bio" or "Rob" or "Tgh" or "Vig"))
                    errors.Add($"{litany.ID} references unknown skill {skill.Id}.");
            }
        }

        if (parameters?.Skills is { } skillData && skillData.Amounts.Count == 0)
            errors.Add($"{litany.ID} has an empty skill parameter map.");
    }

    private static void ValidateReachableCosts(
        List<LitanyPrototype> litanies,
        Dictionary<string, LitanySetPrototype> sets,
        NeoTheologyRulesPrototype? rules,
        List<string> errors)
    {
        if (rules == null)
            return;

        foreach (var litany in litanies.Where(l => l.IsAvailable && l.Cost > 0))
        {
            var max = 0d;
            if (CanRankUse(NeoTheologyRank.Disciple, NeoTheologySpecialization.None, litany, sets))
                max = Math.Max(max, rules.DiscipleCapacity);
            if (CanRankUse(NeoTheologyRank.Disciple, NeoTheologySpecialization.Acolyte, litany, sets))
                max = Math.Max(max, rules.DiscipleCapacity);
            if (CanRankUse(NeoTheologyRank.Disciple, NeoTheologySpecialization.Agrolyte, litany, sets))
                max = Math.Max(max, rules.DiscipleCapacity);
            if (CanRankUse(NeoTheologyRank.Disciple, NeoTheologySpecialization.Custodian, litany, sets))
                max = Math.Max(max, rules.DiscipleCapacity);
            if (CanRankUse(NeoTheologyRank.Preacher, NeoTheologySpecialization.None, litany, sets))
                max = Math.Max(max, rules.PreacherCapacity);
            if (CanRankUse(NeoTheologyRank.Inquisitor, NeoTheologySpecialization.None, litany, sets))
                max = Math.Max(max, rules.InquisitorCapacity);

            var debitTolerance = double.IsFinite(rules.DebitTolerance) && rules.DebitTolerance >= 0
                ? rules.DebitTolerance
                : NeoTheologyHoliness.DebitTolerance;
            if (max + debitTolerance < litany.Cost)
                errors.Add($"{litany.ID} cost {litany.Cost} is not reachable by any granting profile.");
        }
    }

    private static bool CanRankUse(
        NeoTheologyRank rank,
        NeoTheologySpecialization specialization,
        LitanyPrototype litany,
        Dictionary<string, LitanySetPrototype> sets)
    {
        var unlocked = UnlockSets(rank, specialization);
        return litany.GrantedBy.Any(grant => unlocked.Contains(grant.Id) &&
                                             sets.TryGetValue(grant.Id, out var set) &&
                                             set.Litanies.Any(id => id.Id == litany.ID));
    }

    private static HashSet<string> UnlockSets(NeoTheologyRank rank, NeoTheologySpecialization specialization)
    {
        var sets = new HashSet<string> { "OxydLitanyCommon", "OxydLitanyMachinery" };
        if (specialization == NeoTheologySpecialization.Acolyte || rank is NeoTheologyRank.Preacher or NeoTheologyRank.Inquisitor)
            sets.Add("OxydLitanyAcolyte");
        if (specialization == NeoTheologySpecialization.Agrolyte)
            sets.Add("OxydLitanyAgrolyte");
        if (specialization == NeoTheologySpecialization.Custodian)
            sets.Add("OxydLitanyCustodian");
        if (rank == NeoTheologyRank.Preacher)
            sets.Add("OxydLitanyPriest");
        if (rank == NeoTheologyRank.Inquisitor)
        {
            sets.Add("OxydLitanyPriest");
            sets.Add("OxydLitanyInquisitor");
        }

        return sets;
    }
}
