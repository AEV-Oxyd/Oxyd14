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
        ILocalizationManager localization,
        IReadOnlySet<LitanyEffectKind>? runtimeHandlers = null,
        bool requireRuntimeHandlers = true)
    {
        var errors = new List<string>();
        runtimeHandlers ??= LitanyHandlerCatalog.Implemented;
        var litanies = prototypes.EnumeratePrototypes<LitanyPrototype>().ToList();
        var sets = prototypes.EnumeratePrototypes<LitanySetPrototype>().ToDictionary(s => s.ID);
        var profiles = prototypes.EnumeratePrototypes<NeoTheologyProfilePrototype>().ToDictionary(p => p.ID);
        var rules = prototypes.EnumeratePrototypes<NeoTheologyRulesPrototype>().Where(r => r.Selected).ToList();

        if (litanies.Count != ExpectedLitanyCount)
            errors.Add($"Expected {ExpectedLitanyCount} litanies, found {litanies.Count}.");

        ValidateCatalogPolicy(litanies, errors, runtimeHandlers, requireRuntimeHandlers);

        if (rules.Count != 1)
            errors.Add($"Exactly one selected NeoTheology rules profile is required; found {rules.Count}.");

        var selectedRules = rules.Count == 1 ? rules[0] : null;
        ValidateRules(selectedRules, profiles, errors);

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

        ValidateProfiles(prototypes, localization, sets, profiles, selectedRules, errors);

        foreach (var litany in litanies)
        {
            ValidateLitany(
                litany,
                prototypes,
                localization,
                phrases,
                setMembership,
                runtimeHandlers,
                requireRuntimeHandlers,
                errors);
        }

        ValidateReachableCosts(litanies, sets, profiles, selectedRules, errors);
        return errors;
    }

    private static void ValidateCatalogPolicy(
        IReadOnlyCollection<LitanyPrototype> litanies,
        List<string> errors,
        IReadOnlySet<LitanyEffectKind> runtimeHandlers,
        bool requireRuntimeHandlers)
    {
        var foundationEffects = litanies
            .Where(litany => litany.Dependency == NeoTheologyDependency.None)
            .Select(litany => litany.Effect)
            .ToHashSet();
        var dependencyGatedCount = litanies.Count(litany => litany.Dependency != NeoTheologyDependency.None);
        if (!foundationEffects.SetEquals(LitanyHandlerCatalog.Foundation))
            errors.Add("Foundation litany entries do not match the declared foundation catalog.");
        if (foundationEffects.Count != ExpectedFoundationLitanyCount)
            errors.Add($"Expected {ExpectedFoundationLitanyCount} foundation litanies, found {foundationEffects.Count}.");
        if (dependencyGatedCount != ExpectedDependencyGatedLitanyCount)
            errors.Add($"Expected {ExpectedDependencyGatedLitanyCount} dependency-gated litanies, found {dependencyGatedCount}.");

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

        var availableEffects = litanies
            .Where(litany => litany.IsAvailable)
            .Select(litany => litany.Effect)
            .ToHashSet();
        foreach (var effect in availableEffects)
        {
            if (requireRuntimeHandlers)
            {
                if (!runtimeHandlers.Contains(effect))
                    errors.Add($"Available effect {effect} has no registered runtime handler.");
            }
            else if (!LitanyHandlerCatalog.AllowsEnabledCatalogEntry(effect))
            {
                errors.Add($"Available effect {effect} is not registered in the catalog rollout policy.");
            }
        }

        foreach (var effect in runtimeHandlers.Where(effect => !availableEffects.Contains(effect)))
            errors.Add($"Runtime handler {effect} is registered while its catalog entry is unavailable.");
    }

    private static void ValidateRules(
        NeoTheologyRulesPrototype? rules,
        Dictionary<string, NeoTheologyProfilePrototype> profiles,
        List<string> errors)
    {
        if (rules == null)
            return;

        if (!double.IsFinite(rules.BaseHolinessPerMinute) || rules.BaseHolinessPerMinute <= 0)
            errors.Add($"{rules.ID} has an invalid base holiness regeneration value.");
        if (!double.IsFinite(rules.DebitTolerance) || rules.DebitTolerance < 0)
            errors.Add($"{rules.ID} has an invalid debit tolerance.");

        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profileId in rules.Profiles)
        {
            if (!profileIds.Add(profileId.Id))
                errors.Add($"{rules.ID} lists profile {profileId.Id} more than once.");
            else if (!profiles.ContainsKey(profileId.Id))
                errors.Add($"{rules.ID} references unknown profile {profileId.Id}.");
        }

        if (rules.Profiles.Count == 0)
            errors.Add($"{rules.ID} must configure at least one NeoTheology profile.");
    }

    private static void ValidateProfiles(
        IPrototypeManager prototypes,
        ILocalizationManager localization,
        Dictionary<string, LitanySetPrototype> sets,
        Dictionary<string, NeoTheologyProfilePrototype> profiles,
        NeoTheologyRulesPrototype? rules,
        List<string> errors)
    {
        foreach (var profile in profiles.Values)
        {
            if (!localization.HasString(profile.Name.Id))
                errors.Add($"{profile.ID} missing localization {profile.Name.Id}.");
            if (!double.IsFinite(profile.CruciformCapacity) || profile.CruciformCapacity < 0)
                errors.Add($"{profile.ID} has an invalid cruciform capacity.");
            if (!double.IsFinite(profile.RegenerationMultiplier) || profile.RegenerationMultiplier < 0)
                errors.Add($"{profile.ID} has an invalid regeneration multiplier.");

            var accessIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var access in profile.AccessPrivileges)
            {
                if (!accessIds.Add(access.Id))
                    errors.Add($"{profile.ID} lists access privilege {access.Id} more than once.");
                else if (!prototypes.TryIndex<Content.Shared.Access.AccessLevelPrototype>(access, out _))
                    errors.Add($"{profile.ID} references unknown access privilege {access.Id}.");
            }

            ValidateSetReferences(profile.ID, profile.LitanySets, sets, errors);
        }

        if (rules == null)
            return;

        foreach (var profileId in rules.Profiles)
        {
            if (!profiles.TryGetValue(profileId.Id, out var profile))
                continue;

            if (profile.AccessPrivileges.Count == 0)
                errors.Add($"{profile.ID} has no access privileges.");
            if (profile.LitanySets.Count == 0)
                errors.Add($"{profile.ID} has no base litany sets.");
        }
    }

    private static void ValidateSetReferences(
        string ownerId,
        IEnumerable<ProtoId<LitanySetPrototype>> references,
        Dictionary<string, LitanySetPrototype> sets,
        List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setId in references)
        {
            if (!ids.Add(setId.Id))
                errors.Add($"{ownerId} lists litany set {setId.Id} more than once.");
            else if (!sets.ContainsKey(setId.Id))
                errors.Add($"{ownerId} references unknown litany set {setId.Id}.");
        }
    }

    public static List<string> ValidateMissingHandler(
        LitanyPrototype litany,
        IReadOnlySet<LitanyEffectKind>? runtimeHandlers = null,
        bool requireRuntimeHandler = true)
    {
        return ValidateMissingHandler(
            litany.ID,
            litany.Effect,
            litany.IsAvailable,
            runtimeHandlers,
            requireRuntimeHandler);
    }

    public static List<string> ValidateMissingHandler(
        string id,
        LitanyEffectKind effect,
        bool isAvailable,
        IReadOnlySet<LitanyEffectKind>? runtimeHandlers = null,
        bool requireRuntimeHandler = true)
    {
        runtimeHandlers ??= LitanyHandlerCatalog.Implemented;
        if (isAvailable && requireRuntimeHandler && !runtimeHandlers.Contains(effect))
            return [$"{id} is enabled without a registered runtime handler for {effect}."];

        return [];
    }

    private static void ValidateLitany(
        LitanyPrototype litany,
        IPrototypeManager prototypes,
        ILocalizationManager localization,
        Dictionary<string, string> phrases,
        Dictionary<string, HashSet<string>> setMembership,
        IReadOnlySet<LitanyEffectKind> runtimeHandlers,
        bool requireRuntimeHandlers,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(litany.ID))
            errors.Add("A litany has an empty prototype ID.");
        else if (!string.Equals(litany.ID, $"OxydLitany{litany.Effect}", StringComparison.Ordinal))
            errors.Add($"{litany.ID} does not match the canonical ID for effect {litany.Effect}.");

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
            if (litany.UnavailableReason is not { } reason || !localization.HasString(reason.Id))
                errors.Add($"{litany.ID} is disabled without a localized unavailable reason.");
        }

        errors.AddRange(ValidateMissingHandler(litany, runtimeHandlers, requireRuntimeHandlers));

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

        ValidateParameters(litany, prototypes, errors);
    }

    private static void ValidateParameters(
        LitanyPrototype litany,
        IPrototypeManager prototypes,
        List<string> errors)
    {
        var parameters = litany.Parameters;
        var allowsHealing = litany.Effect is LitanyEffectKind.Relief
            or LitanyEffectKind.SoulHunger
            or LitanyEffectKind.HandOfMercy
            or LitanyEffectKind.Convalescence
            or LitanyEffectKind.Succour;
        var allowsSkills = litany.Effect is LitanyEffectKind.GraceOfPerseverance
            or LitanyEffectKind.UpholdHolyWord;
        var allowsMachine = litany.Effect is LitanyEffectKind.Resurrection
            or LitanyEffectKind.MakeCruciform
            or LitanyEffectKind.ActivateDoor
            or LitanyEffectKind.RepairDoor
            or LitanyEffectKind.PowerBiogenerator
            or LitanyEffectKind.BioreactorSolution
            or LitanyEffectKind.BioreactorChamber;
        var allowsGroup = litany.Effect is LitanyEffectKind.PoundingWhisper
            or LitanyEffectKind.RevelationOfSecrets
            or LitanyEffectKind.LispOfVitae
            or LitanyEffectKind.CantoOfCourage
            or LitanyEffectKind.ChantOfObservance
            or LitanyEffectKind.ReclamationOfEndurance
            or LitanyEffectKind.Sanctify
            or LitanyEffectKind.Crusade;

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
            if (parameters.Machine != null && !allowsMachine)
                errors.Add($"{litany.ID} has unexpected machine parameters.");
            if (parameters.Group != null && !allowsGroup)
                errors.Add($"{litany.ID} has unexpected group parameters.");
            if (parameters.Machine != null)
            {
                if (string.IsNullOrWhiteSpace(parameters.Machine.Command))
                    errors.Add($"{litany.ID} machine parameters require a command.");
                if (parameters.Machine.Output is { } output && !prototypes.TryIndex(output, out _))
                    errors.Add($"{litany.ID} references unknown machine output {output.Id}.");
            }
            if (parameters.Group != null)
            {
                if (parameters.Group.MinimumFollowers < 0)
                    errors.Add($"{litany.ID} has a negative group follower threshold.");
                if (litany.TargetMode != LitanyTargetMode.Ceremony)
                    errors.Add($"{litany.ID} has group parameters without Ceremony targeting.");
            }

            if (parameters.Healing is { } healing)
            {
                foreach (var (damageType, value) in healing.Damage.DamageDict)
                {
                    if (value.Value >= 0)
                        errors.Add($"{litany.ID} healing for {damageType} must use a negative damage value.");
                }
            }
        }

        switch (litany.Effect)
        {
            case LitanyEffectKind.Relief:
            case LitanyEffectKind.HandOfMercy:
                if (parameters?.Healing is null || parameters.Healing.Damage.Empty)
                    errors.Add($"{litany.ID} requires healing parameters.");
                break;
            case LitanyEffectKind.SoulHunger:
                if (parameters?.Healing is null ||
                    !parameters.Healing.Damage.DamageDict.TryGetValue("Heat", out var heat) ||
                    heat.Value >= 0)
                    errors.Add($"{litany.ID} requires a negative Heat damage parameter.");
                break;
            case LitanyEffectKind.Convalescence:
            case LitanyEffectKind.Succour:
                if (parameters?.Healing is null || parameters.Healing.Damage.Empty)
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

        if (parameters?.Machine != null && litany.TargetMode is not (LitanyTargetMode.FrontMachine or LitanyTargetMode.NearbyMachine))
            errors.Add($"{litany.ID} machine parameters require machine targeting.");
    }

    private static void ValidateReachableCosts(
        List<LitanyPrototype> litanies,
        Dictionary<string, LitanySetPrototype> sets,
        Dictionary<string, NeoTheologyProfilePrototype> profiles,
        NeoTheologyRulesPrototype? rules,
        List<string> errors)
    {
        if (rules == null)
            return;

        foreach (var litany in litanies.Where(l => l.IsAvailable && l.Cost > 0))
        {
            var max = 0d;
            foreach (var profileId in rules.Profiles)
            {
                if (!profiles.TryGetValue(profileId.Id, out var profile))
                    continue;

                if (CanProfileUse(profile, litany, sets))
                    max = Math.Max(max, profile.CruciformCapacity);
            }

            var debitTolerance = double.IsFinite(rules.DebitTolerance) && rules.DebitTolerance >= 0
                ? rules.DebitTolerance
                : 0d;
            if (max + debitTolerance < litany.Cost)
                errors.Add($"{litany.ID} cost {litany.Cost} is not reachable by any granting profile.");
        }
    }

    private static bool CanProfileUse(
        NeoTheologyProfilePrototype profile,
        LitanyPrototype litany,
        Dictionary<string, LitanySetPrototype> sets)
    {
        var unlocked = profile.LitanySets.Select(set => set.Id).ToHashSet(StringComparer.Ordinal);

        return litany.GrantedBy.Any(grant => unlocked.Contains(grant.Id) &&
                                             sets.TryGetValue(grant.Id, out var set) &&
                                             set.Litanies.Any(id => id.Id == litany.ID));
    }
}
