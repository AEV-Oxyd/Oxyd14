using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Skills;

/// <summary>
/// This is used for...
public abstract partial class SharedSkillSystem : EntitySystem
{
    [Dependency] protected IPrototypeManager protoMan = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobSkillComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<MobSkillComponent> ent, ref ComponentInit args)
    {
        foreach (var instance in protoMan.EnumeratePrototypes<SkillPrototype>())
        {
            ent.Comp.skills.Add(instance.ID, new int[] { 0, 0 });
        }
        Dirty(ent);
    }
}
