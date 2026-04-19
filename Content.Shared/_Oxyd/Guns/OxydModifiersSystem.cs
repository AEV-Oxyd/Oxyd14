using System.ComponentModel.Design;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
[Serializable, NetSerializable]
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
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly CircularSelect

    public const string cid = "oAtts";
    /// <inheritdoc/>

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydAttachmentHolderComponent, ComponentInit>(onInit);
        SubscribeLocalEvent<OxydAttachmentHolderComponent, AfterInteractUsingEvent>(onUse);
    }

    public void onUse(Entity<OxydAttachmentHolderComponent> holder, ref AfterInteractUsingEvent args)
    {
        if (!TryComp<OxydAttachmentComponent>(args.Used, out var att))
            return;
        if (!tryAddAttachment(holder, (args.Used, att), out var errorMsg))
        {
            _popup.PopupClient(errorMsg, holder, PopupType.Small);
            return;
        }
        _popup.PopupClient($"You attach the {MetaData(args.Used).EntityName}!", holder);
    }

    public void onInit(Entity<OxydAttachmentHolderComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<Container>(ent.Owner, cid);
        foreach (var key in ent.Comp.slots)
        {
            ent.Comp.attachments.TryAdd(key, NetEntity.Invalid);
        }
    }
    public List<OxydModifier> getModifiers(Entity<OxydAttachmentHolderComponent> ent, Type filter)
    {
        var list = new List<OxydModifier>();
        foreach (var attachment in ent.Comp.attachments.Values)
        {
            var targ = GetEntity(attachment);
            if (TerminatingOrDeleted(targ))
                continue;
            if (!TryComp<OxydAttachmentComponent>(targ, out var attComp))
                continue;
            foreach (var modifier in attComp.mods)
            {
                if (filter.IsAssignableFrom(modifier.GetType()))
                    list.Add(modifier);
            }
        }
        return list;
    }

    public List<OxydModifier> getGunModifiers(Entity<OxydAttachmentHolderComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
        {
            Log.Error($"Entity {ent}, with prototype {MetaData(ent)?.EntityPrototype!.ID} has no OxydAttachmentHolderComponent!");
            return new List<OxydModifier>();
        }
        return getModifiers((ent.Owner, ent.Comp), typeof(GunMod));
    }

    public List<OxydModifier> getToolModifiers(Entity<OxydAttachmentHolderComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
        {
            Log.Error($"Entity {ent}, with prototype {MetaData(ent)?.EntityPrototype!.ID} has no OxydAttachmentHolderComponent!");
            return new List<OxydModifier>();
        }
        return getModifiers((ent.Owner, ent.Comp), typeof(ToolMod));
    }

    public CompoundedModifiers updateModifiers(Entity<OxydAttachmentHolderComponent> ent)
    {
        var mods = getModifiers(ent, typeof(OxydModifier));
        var compound = new CompoundedModifiers();
        foreach (var mod in mods)
            mod.addToCompound(compound);
        return compound;
    }

    public bool tryAddAttachment(Entity<OxydAttachmentHolderComponent> ent, Entity<OxydAttachmentComponent> attachment, out string errorMsg)
    {
        errorMsg = "";
        var cont = (Container)_container.GetContainer(ent.Owner, cid);
        if (!_whitelist.IsValid(ent.Comp.allowedAttachments, attachment.Owner))
        {
            errorMsg = "The attachment cannot be installed on this!";
            return false;
        }

        if (ent.Comp.attachments.TryGetValue(attachment.Comp.Slot, out var oldAtt) && oldAtt != NetEntity.Invalid)
        {
            errorMsg = "A attachment is already installed in that slot!";
            return false;
        }
        _container.Insert(attachment.Owner, cont, null, true);
        ent.Comp.attachments[attachment.Comp.Slot] = GetNetEntity(attachment.Owner);
        updateModifiers(ent);
        return true;
    }

    public bool tryRemoveAttachment(Entity<OxydAttachmentHolderComponent> ent, AttSlot slot, out EntityUid removed, out string errorMsg)
    {
        errorMsg = "";
        removed = EntityUid.Invalid;

        if (!ent.Comp.attachments.TryGetValue(slot, out var netEnt) || netEnt == NetEntity.Invalid)
        {
            errorMsg = "No attachment is installed in that slot!";
            return false;
        }

        var attachment = GetEntity(netEnt);
        if (TerminatingOrDeleted(attachment))
        {
            ent.Comp.attachments.Remove(slot);
            errorMsg = "The attachment no longer exists!";
            return false;
        }

        var cont = (Container)_container.GetContainer(ent.Owner, cid);
        _container.Remove(attachment, cont, destination: Transform(ent.Owner).Coordinates);
        ent.Comp.attachments[slot] = NetEntity.Invalid;
        updateModifiers(ent);
        removed = attachment;
        return true;
    }
}
