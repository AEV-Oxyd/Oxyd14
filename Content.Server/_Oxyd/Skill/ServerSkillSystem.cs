using Content.Server._Oxyd.Framework.JobsAndSpawning;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server._Oxyd.Skill;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class ServerSkillSystem : SharedSkillSystem
{
    [SubscribeLocalEvent]
    void OnCharacterSpawn(JobAfterSpawnEvent ev)
    {
        if (!TryComp<MobSkillComponent>(ev.spawned, out var skillComp))
            return;
        foreach (var (skill, amount) in ev.job.skillsBonuses)
        {
            skillComp.skills[skill][0] += amount;
        }
    }

    [SubscribeLocalEvent]
    void OnTaste(Entity<SkillOnEatComponent> ent, ref FlavorProfileModificationEvent args)
    {
        if (!sq.TryComp(args.User, out var skillComp))
            return;
        foreach (var (sid, amount) in ent.Comp.skills)
        {
            SetUniqueBuff((args.User, skillComp), ent.Comp.buffId, amount, sid, ent.Comp.duration);
        }
    }
    
}
