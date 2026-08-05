using System.Linq;
using Content.Shared._Oxyd.Tools;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.Skills;



public abstract partial class SharedSkillSystem : EntitySystem
{
    [Dependency] protected IPrototypeManager protoMan = default!;
    [Dependency] protected IGameTiming timing = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobSkillComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ToolComponent, OxydToolGetModifiersEvent>(OnToolUse);

    }

    public void ModifySkill(Entity<MobSkillComponent> ent, ProtoId<SkillPrototype> skill, int amount)
    {
        ent.Comp.skills[skill][0] += amount;
        Dirty(ent);
    }

    public void ModifySkills(Entity<MobSkillComponent> ent, Dictionary<ProtoId<SkillPrototype>, int> skills)
    {
        foreach (var (skill, amount) in skills)
        {
            ent.Comp.skills[skill][0] += amount;
        }
        Dirty(ent);
    }
    private void OnToolUse(Entity<ToolComponent> ent, ref OxydToolGetModifiersEvent args)
    {
        if (!TryComp<MobSkillComponent>(args.user, out var skills))
            return;
        Dictionary<SkillPrototype, int> relevant = new();
        foreach (var instance in protoMan.EnumeratePrototypes<SkillPrototype>())
        {
            foreach (var qual in args.qualities)
            {
                if (instance.affectingQualities.Contains(qual) && ent.Comp.ToolLevels.TryGetValue(qual, out var weight))
                {
                    if (relevant.ContainsKey(instance))
                        relevant[instance] += weight;
                    else
                        relevant[instance] = weight;
                }
            }
        }

        var weightSum = relevant.Values.Sum();
        foreach (var (key, value) in relevant)
        {
            var effect = TimeSpan.FromSeconds((float)value / weightSum * key.timeIncrements * (skills.skills[key.ID][0] + skills.skills[key.ID][1]));
            args.delay -= effect;
        }
    }

    private void OnInit(Entity<MobSkillComponent> ent, ref ComponentInit args)
    {
        foreach (var instance in protoMan.EnumeratePrototypes<SkillPrototype>())
        {
            if (ent.Comp.skills.ContainsKey(instance.ID))
                continue;
            ent.Comp.skills.Add(instance.ID, new int[] { 0, 0 });
        }
        Dirty(ent);
    }

    public MobSkillComponent.BuffData AddBuff(Entity<MobSkillComponent> ent, string id, int amount,ProtoId<SkillPrototype> skill, TimeSpan? expires)
    {
        if (!ent.Comp.buffSources.ContainsKey(id))
            ent.Comp.buffSources[id] = new List<MobSkillComponent.BuffData>();
        var buff = new MobSkillComponent.BuffData() { amount = amount, expires = expires is null ? TimeSpan.MaxValue : expires.Value, skill = skill };
        ent.Comp.buffSources[id].Add(buff);
        RecalculateBuffs(ent);
        return buff;
    }

    public MobSkillComponent.BuffData SetUniqueBuff(Entity<MobSkillComponent> ent, string id, int amount,ProtoId<SkillPrototype> skill, TimeSpan? expires)
    {
        if (!ent.Comp.buffSources.ContainsKey(id))
            ent.Comp.buffSources[id] = new List<MobSkillComponent.BuffData>();
        ent.Comp.buffSources[id].Clear();
        var buff = new MobSkillComponent.BuffData() { amount = amount, expires = expires is null ? TimeSpan.MaxValue : expires.Value, skill = skill };
        ent.Comp.buffSources[id].Add(buff);
        RecalculateBuffs(ent);
        return buff;
    }

    public void RemoveBuffs(Entity<MobSkillComponent> ent, string id)
    {
        if (!ent.Comp.buffSources.ContainsKey(id))
            return;
        ent.Comp.buffSources[id].Clear();
        RecalculateBuffs(ent);
    }

    public void RemoveBuff(Entity<MobSkillComponent> ent,string id,  MobSkillComponent.BuffData target)
    {
        if (!ent.Comp.buffSources.ContainsKey(id))
            return;
        ent.Comp.buffSources[id].Remove(target);

    }

    public void RecalculateBuffs(Entity<MobSkillComponent> ent)
    {
        foreach (var instance in protoMan.EnumeratePrototypes<SkillPrototype>())
        {
            var arrayRef = ent.Comp.skills[instance.ID];
            arrayRef[1] = 0;
            // slop iteration but this is gonna be low N anyway SPCR 2026
            foreach (var source in ent.Comp.buffSources[instance.ID])
            {
                if (source.skill != instance.ID)
                    continue;
                arrayRef[1] += source.amount;
            }
        }
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var iter = EntityQueryEnumerator<MobSkillComponent>();
        foreach (var instance in iter)
        {
            var hadUpdate = false;
            foreach (var (_, buffs) in instance.Comp.buffSources)
            {
                var oldC = buffs.Count;
                buffs.RemoveAll(buff => buff.expires < timing.CurTime);
                hadUpdate = buffs.Count != oldC;
            }
            if(hadUpdate)
                RecalculateBuffs(instance);

        }
    }
}
