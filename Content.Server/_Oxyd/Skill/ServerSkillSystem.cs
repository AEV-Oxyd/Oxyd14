using Content.Server._Oxyd.Framework.JobsAndSpawning;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Mobs.Components;

namespace Content.Server._Oxyd.Skill;

/// <summary>
/// This handles...
/// </summary>
public sealed class ServerSkillSystem : SharedSkillSystem
{

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JobAfterSpawnEvent>(OnCharacterSpawn);

    }

    void OnCharacterSpawn(JobAfterSpawnEvent ev)
    {
        if (!TryComp<MobSkillComponent>(ev.spawned, out var skillComp))
            return;
        foreach (var (skill, amount) in ev.job.skillsBonuses)
        {
            skillComp.skills[skill][0] += amount;
        }
    }
}
