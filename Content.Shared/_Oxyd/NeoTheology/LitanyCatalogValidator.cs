using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Damage;
using Content.Shared.EntityConditions;
using Content.Shared.EntityEffects;
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
    public const int ExpectedFoundationLitanyCount = 60;
    public const int ExpectedDependencyGatedLitanyCount = ExpectedLitanyCount - ExpectedFoundationLitanyCount;

    /// <summary>
    /// Litanies whose Eris chant tolerates stuttering. Every other litany must leave
    /// <see cref="LitanyPrototype.IgnoreStuttering"/> unset.
    /// </summary>
    private static readonly HashSet<string> IgnoreStutteringLitanies =
    [
        "OxydLitanyRelief",
        "OxydLitanyEntreaty",
        "OxydLitanyRejection",
    ];

    public static List<string> Validate(
        IPrototypeManager prototypes,
        ILocalizationManager localization,
        bool requireRuntimeHandlers = true)
    {
        var errors = new List<string>();
        var litanies = prototypes.EnumeratePrototypes<LitanyPrototype>().ToList();
        var sets = prototypes.EnumeratePrototypes<LitanySetPrototype>().ToDictionary(s => s.ID);
        var profiles = prototypes.EnumeratePrototypes<NeoTheologyProfilePrototype>().ToDictionary(p => p.ID);
        var rules = prototypes.EnumeratePrototypes<NeoTheologyRulesPrototype>().Where(r => r.Selected).ToList();

        if (litanies.Count != ExpectedLitanyCount)
            errors.Add($"Expected {ExpectedLitanyCount} litanies, found {litanies.Count}.");

        ValidateCatalogPolicy(litanies, errors, requireRuntimeHandlers);

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
                localization,
                phrases,
                setMembership,
                requireRuntimeHandlers,
                errors);
        }

        ValidateReachableCosts(prototypes, litanies, sets, profiles, selectedRules, errors);
        return errors;
    }

    private static void ValidateCatalogPolicy(
        IReadOnlyCollection<LitanyPrototype> litanies,
        List<string> errors,
        bool requireRuntimeHandlers)
    {
        var foundationCount = litanies.Count(litany => litany.Dependency == NeoTheologyDependency.None);
        var dependencyGatedCount = litanies.Count - foundationCount;
        if (foundationCount != ExpectedFoundationLitanyCount)
            errors.Add($"Expected {ExpectedFoundationLitanyCount} foundation litanies, found {foundationCount}.");
        if (dependencyGatedCount != ExpectedDependencyGatedLitanyCount)
            errors.Add($"Expected {ExpectedDependencyGatedLitanyCount} dependency-gated litanies, found {dependencyGatedCount}.");

        // A litany "has a handler" by carrying at least one effect; there is no second
        // registry to drift against.
        if (requireRuntimeHandlers)
        {
            foreach (var litany in litanies.Where(litany => litany.IsAvailable && litany.Effects.Count == 0))
                errors.Add($"Available litany {litany.ID} has no effects.");
        }
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

            var modules = new HashSet<ProtoId<CoreModulePrototype>>();
            foreach (var moduleId in profile.StartingModules)
            {
                if (!modules.Add(moduleId))
                    errors.Add($"{profile.ID} lists starting module {moduleId} more than once.");
                if (!prototypes.TryIndex(moduleId, out var module))
                    errors.Add($"{profile.ID} references unknown starting module {moduleId}.");
                else
                    ValidateSetReferences($"{profile.ID}/{moduleId}", module.LitanySets, sets, errors);
            }
        }

        if (rules == null)
            return;

        foreach (var profileId in rules.Profiles)
        {
            if (!profiles.TryGetValue(profileId.Id, out var profile))
                continue;

            if (profile.AccessPrivileges.Count == 0)
                errors.Add($"{profile.ID} has no access privileges.");
            if (profile.StartingModules.Count == 0)
                errors.Add($"{profile.ID} has no starting modules.");
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
        bool requireRuntimeHandler = true)
    {
        if (litany.IsAvailable && requireRuntimeHandler && litany.Effects.Count == 0)
            return new List<string> { $"{litany.ID} is enabled without any effects." };

        return new List<string>();
    }

    private static void ValidateLitany(
        LitanyPrototype litany,
        ILocalizationManager localization,
        Dictionary<string, string> phrases,
        Dictionary<string, HashSet<string>> setMembership,
        bool requireRuntimeHandlers,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(litany.ID))
            errors.Add("A litany has an empty prototype ID.");
        else if (!litany.ID.StartsWith("OxydLitany", StringComparison.Ordinal))
            errors.Add($"{litany.ID} does not use the OxydLitany prefix.");

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

            if (targetPlaceholder >= 0 && !litany.SelectTarget)
                errors.Add($"{litany.ID} names a target in its phrase but does not offer a book choice.");

            if (phrases.TryGetValue(normalized, out var other))
                errors.Add($"Duplicate phrase between {other} and {litany.ID}.");
            else
                phrases[normalized] = litany.ID;
        }

        if (!localization.HasString(litany.Name.Id))
            errors.Add($"{litany.ID} missing localization {litany.Name.Id}.");
        if (!localization.HasString(litany.Description.Id))
            errors.Add($"{litany.ID} missing localization {litany.Description.Id}.");

        // P5.2: a ceremony needs a phrase list and the list must begin with the litany phrase.
        if (litany.TargetMode == LitanyTargetMode.Ceremony)
        {
            if (litany.CeremonyPhrases.Count < 2)
            {
                errors.Add($"{litany.ID} ceremony litanies need at least two phrases.");
            }
            else if (!string.Equals(
                         LitanyPhraseParser.Normalize(litany.CeremonyPhrases[0]),
                         LitanyPhraseParser.Normalize(litany.Phrase),
                         StringComparison.Ordinal))
            {
                errors.Add($"{litany.ID} ceremony phrase list must start with the litany phrase.");
            }

            for (var i = 1; i < litany.CeremonyPhrases.Count; i++)
            {
                var phrase = litany.CeremonyPhrases[i];
                if (string.IsNullOrWhiteSpace(phrase) || phrase.Contains('<') || phrase.Contains('\n'))
                {
                    errors.Add($"{litany.ID} ceremony phrase {i} contains forbidden formatting.");
                    continue;
                }

                if (LitanyPhraseParser.ScalarCount(phrase) > LitanyPhraseParser.MaxPhraseScalars)
                {
                    errors.Add($"{litany.ID} ceremony phrase {i} exceeds {LitanyPhraseParser.MaxPhraseScalars} scalars.");
                }
            }
        }
        else if (litany.CeremonyPhrases.Count > 0)
        {
            errors.Add($"{litany.ID} declares ceremony phrases outside Ceremony target mode.");
        }

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

        if (litany.IgnoreStuttering != IgnoreStutteringLitanies.Contains(litany.ID))
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

        errors.AddRange(ValidateMissingHandler(litany, requireRuntimeHandlers));

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

        ValidateEffects(litany, errors);
    }

    private static bool ConditionsDivergeFromCtor(EntityEffect effect)
    {
        EntityEffect fresh;
        try
        {
            if (Activator.CreateInstance(effect.GetType()) is not EntityEffect created)
                return false;
            fresh = created;
        }
        catch (Exception)
        {
            return false;
        }
        var declared = effect.Conditions ?? Array.Empty<EntityCondition>();
        var intrinsic = fresh.Conditions ?? Array.Empty<EntityCondition>();
        return !declared.Select(c => c.GetType()).SequenceEqual(intrinsic.Select(c => c.GetType()));
    }

    private static void ValidateEffects(LitanyPrototype litany, List<string> errors)
    {
        foreach (var effect in litany.Effects)
        {
            // A YAML conditions: list replaces the intrinsic guards the effect type
            // installs in its constructor. Declared condition types must match a fresh
            // instance exactly — anything else means YAML overwrote the ctor guards.
            if (ConditionsDivergeFromCtor(effect))
                errors.Add($"{litany.ID} declares conditions: on litany effect {effect.GetType().Name}; intrinsic guards own gating.");

            switch (effect)
            {
                case LitanyHealEffect heal:
                    foreach (var (damageType, value) in heal.Damage.DamageDict)
                    {
                        if (value.Value >= 0)
                            errors.Add($"{litany.ID} healing for {damageType} must use a negative damage value.");
                    }

                    if (heal.Damage.Empty)
                        errors.Add($"{litany.ID} has an empty healing effect.");
                    break;
                case LitanyInjectReagentsEffect injection:
                    if (injection.Reagents.Count == 0 || injection.Reagents.Values.Any(amount => amount <= 0))
                        errors.Add($"{litany.ID} requires positive reagent doses.");
                    break;
                case LitanySoulHungerEffect soulHunger:
                    if (!soulHunger.Damage.DamageDict.TryGetValue("Heat", out var heat) || heat.Value <= 0)
                        errors.Add($"{litany.ID} requires a positive Heat damage parameter.");
                    break;
                case LitanySkillEffect skills:
                    if (skills.Amounts.Count == 0)
                        errors.Add($"{litany.ID} has an empty skill effect.");
                    foreach (var skill in skills.Amounts.Keys)
                    {
                        if (skill.Id is not ("Mec" or "Cog" or "Bio" or "Rob" or "Tgh" or "Vig"))
                            errors.Add($"{litany.ID} references unknown skill {skill.Id}.");
                    }
                    break;
            }
        }

        // Cast atomicity is enforced structurally: one effect per litany, so a failed
        // apply can never refund a cast that already mutated state.
        if (litany.Effects.Count > 1)
            errors.Add($"{litany.ID} defines {litany.Effects.Count} effects; litanies are limited to one effect.");

        // Special-case data fields must be backed by a matching effect.
        if (litany.RequiresHeldOddity && !litany.Effects.OfType<LitanyDivineBlessingEffect>().Any())
            errors.Add($"{litany.ID} sets requiresHeldOddity without a LitanyDivineBlessingEffect.");
        if (litany.TargetShape == LitanyTargetShape.Cone && litany.Range <= 0)
            errors.Add($"{litany.ID} sets targetShape Cone without a positive range.");
    }

    private static void ValidateReachableCosts(
        IPrototypeManager prototypes,
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

                if (CanProfileUse(prototypes, profile, litany, sets))
                {
                    var capacity = profile.CruciformCapacity;
                    foreach (var moduleId in profile.StartingModules)
                    {
                        if (prototypes.TryIndex(moduleId, out var module))
                            capacity *= module.MaxHolinessMultiplier;
                    }
                    max = Math.Max(max, capacity);
                }
            }

            var debitTolerance = double.IsFinite(rules.DebitTolerance) && rules.DebitTolerance >= 0
                ? rules.DebitTolerance
                : 0d;
            if (max + debitTolerance < litany.Cost)
                errors.Add($"{litany.ID} cost {litany.Cost} is not reachable by any granting profile.");
        }
    }

    private static bool CanProfileUse(
        IPrototypeManager prototypes,
        NeoTheologyProfilePrototype profile,
        LitanyPrototype litany,
        Dictionary<string, LitanySetPrototype> sets)
    {
        var unlocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var moduleId in profile.StartingModules)
        {
            if (!prototypes.TryIndex(moduleId, out var module))
                continue;
            foreach (var set in module.LitanySets)
                unlocked.Add(set.Id);
        }

        return litany.GrantedBy.Any(grant => unlocked.Contains(grant.Id) &&
                                             sets.TryGetValue(grant.Id, out var set) &&
                                             set.Litanies.Any(id => id.Id == litany.ID));
    }
}
