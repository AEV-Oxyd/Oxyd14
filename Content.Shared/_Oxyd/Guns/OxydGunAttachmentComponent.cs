using Content.Shared.Damage;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;

[ImplicitDataDefinitionForInheritors]
[MeansImplicitUse]
public abstract partial class OxydModifier
{
    public abstract void addToCompound(CompoundedModifiers target);
}

public interface GunMod;

public interface ToolMod;

public sealed partial class RecoilMod : OxydModifier, GunMod
{
    [DataField]
    public float addMod;
    [DataField]
    public float multMod;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.recoilAdd += addMod;
        target.recoilMult *= multMod;
    }
}

public sealed partial class FirerateMod : OxydModifier, GunMod
{
    [DataField]
    public float addMod;
    [DataField]
    public float multMod;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.firerateAdd += addMod;
        target.firerateMult *= multMod;
    }
}

public sealed partial class DamageMod : OxydModifier, GunMod, ToolMod
{
    [DataField]
    public DamageSpecifier addMod;
    [DataField]
    public DamageSpecifier multMod;

    public override void addToCompound(CompoundedModifiers target)
    {
        if (target.damageAdd is null)
            target.damageAdd = addMod;
        else
            target.damageAdd += addMod;

        if (multMod.Empty)
            return;

        if (target.damageMult is null)
        {
            target.damageMult = multMod;
        }
        else
        {
            foreach (var (damageType, multiplier) in multMod.DamageDict)
            {
                if (target.damageMult.DamageDict.ContainsKey(damageType))
                    target.damageMult.DamageDict[damageType] *= multiplier;
                else
                    target.damageMult.DamageDict.Add(
                        damageType,
                        target.damageMult.DamageDict.GetValueOrDefault(damageType) * multiplier
                    );
            }
        }
    }
}

public sealed partial class SoundMod : OxydModifier, GunMod, ToolMod
{
    [DataField]
    public float range;
    [DataField]
    public float volume;
    [DataField]
    public float pitch;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.soundVolume += volume;
        target.soundPitch += pitch;
        target.soundRange += range;
    }
}

public sealed partial class SoundOverrideMod : OxydModifier, GunMod, ToolMod
{
    [DataField]
    public SoundSpecifier sound;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.soundOverride = sound;
    }
}

public sealed partial class ZoomMod : OxydModifier, GunMod
{
    [DataField]
    public float zoom;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.zoomMod *= zoom;
    }
}

public sealed partial class WeaponCapacityMod : OxydModifier, GunMod
{
    [DataField]
    public int capacityAdd;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.gunCapacityAdd += capacityAdd;
    }
}

public sealed partial class ToolFuelCapacityMod : OxydModifier, ToolMod
{
    [DataField]
    public int capacityAdd;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.toolCapacityAdd += capacityAdd;
    }
}

public sealed partial class UseSpeedMod : OxydModifier, ToolMod
{
    [DataField]
    public float useSpeedMult;

    public override void addToCompound(CompoundedModifiers target)
    {
        target.useSpeedMult *= useSpeedMult;
    }
}

/// <summary>
/// This handles...
/// </summary>
[RegisterComponent, NetworkedComponent]
public partial class OxydGunAttachmentComponent : Component
{
    [DataField]
    public SpriteSpecifier onGun = default!;
}

[RegisterComponent, NetworkedComponent]
public partial class OxydAttachmentComponent : Component
{
    [DataField]
    public List<OxydModifier> mods = default!;
    [DataField]
    public AttSlots slot = default!;
}

