using System.Linq;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared._Oxyd.Sanity;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.Skill;

/// <summary>
/// Eris mental_imprinter.dm: use the imprinter to pick one of the six skills,
/// gain +5 (or +10 when the base stat is at/below STAT_LEVEL_BASIC) at a
/// heavy sanity toll, and consume the device. The verb menu is the port of
/// Eris's input() picker. Eris gates on a living user with no eye covering;
/// those checks are preserved.
/// </summary>
public sealed partial class OxydMentalImprinterSystem : EntitySystem
{
    [Dependency] private SanitySystem _sanity = default!;
    [Dependency] private SharedSkillSystem _skills = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<OxydMentalImprinterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var skills = _proto.EnumeratePrototypes<SkillPrototype>()
            .OrderBy(s => s.name, StringComparer.Ordinal).ToList();
        foreach (var skill in skills)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = $"Imprint: {skill.name}",
                Act = () => Imprint(ent, user, skill.ID),
            });
        }
    }

    /// <summary>Direct imprint for toolshed/QA; same path the verbs take.</summary>
    public void Imprint(Entity<OxydMentalImprinterComponent> ent, EntityUid user, ProtoId<SkillPrototype> skill)
    {
        if (!TryComp<MobSkillComponent>(user, out var mobSkills))
        {
            _popup.PopupEntity("Your mind rejects the imprint.", user, user);
            return;
        }
        if (!TryComp<MobStateComponent>(user, out var mobState) || mobState.CurrentState != MobState.Alive)
        {
            _popup.PopupEntity("You are in no state to receive an imprint.", user, user);
            return;
        }
        // Eris: no eye covering — the needle must reach the eye itself.
        if (_inventory.TryGetSlotEntity(user, "eyes", out var eyes) && eyes != null)
        {
            _popup.PopupEntity("You can't focus the imprint through eyewear.", user, user);
            return;
        }

        var boosted = mobSkills.skills.TryGetValue(skill, out var stat) && stat[0] <= ent.Comp.LowSkillThreshold;
        var gain = boosted ? ent.Comp.BoostedGain : ent.Comp.NormalGain;
        var sanity = boosted ? ent.Comp.SanityDamage * 0.5f : ent.Comp.SanityDamage;

        _skills.ModifySkill((user, mobSkills), skill, gain);
        if (TryComp<SanityComponent>(user, out var sanityComp))
            _sanity.ApplySanityDelta((user, sanityComp), SanitySource.Mental, -sanity);

        var skillName = _proto.TryIndex<SkillPrototype>(skill, out var proto) ? proto.name : skill.Id;
        _popup.PopupEntity($"The imprinter burns {skillName} into your mind (+{gain}).",
            user, user);
        QueueDel(ent);
    }

    /// <summary>Plain use without a skill pick just explains the verbs.</summary>
    [SubscribeLocalEvent]
    private void OnUseInHand(Entity<OxydMentalImprinterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        _popup.PopupEntity("Choose a skill from the imprinter's verbs.", args.User, args.User);
    }
}
