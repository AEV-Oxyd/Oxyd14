using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
///
public sealed class CompoundedModifiers
{
    public float recoilAdd = 0;
    public float recoilMult = 1;
    public float firerateAdd = 0;
    public float firerateMult = 1;
    public DamageSpecifier? damageAdd;
    public DamageSpecifier? damageMult;
    public float zoomMod = 1;
    public float soundRange = 0;
    public float soundVolume = 0;
    public float soundPitch = 0;
    public float useSpeedMult = 1;
    public float gunCapacityAdd = 0;
    public float toolCapacityAdd = 0;
    public SoundSpecifier? soundOverride;
}
public sealed class OxydModifiersSystem : EntitySystem
{
    public List<OxydModifier> getModifiers(Entity<OxydAttachmentHolderComponent> ent, Type filter)
    {
        var list = new List<OxydModifier>();
        foreach (var attachment in ent.Comp.attachments.Values)
        {
            if (!TryComp<OxydAttachmentComponent>(attachment, out var attComp))
                continue;
            foreach (var modifier in attComp.mods)
            {
                if (filter.IsAssignableFrom(modifier.GetType()))
                    list.Add(modifier);
            }
        }
        return list;
    }

    public CompoundedModifiers getModifiers(Entity<OxydAttachmentHolderComponent> ent)
    {
        var mods = getModifiers(ent, typeof(OxydModifier));
        var compound = new CompoundedModifiers();
        foreach (var mod in mods)
            mod.addToCompound(compound);
        return compound;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<OxydAttachmentHolderComponent, GetModsEvent>(onGetMod);
    }

    public void onGetMod(Entity<OxydAttachmentHolderComponent> ent, ref GetModsEvent args)
    {
        args.mods = getModifiers(ent, args.typeFilter);
    }
}
