using System.ComponentModel.Design;
using Content.Shared._Oxyd.Framework.RadialMenu;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
[Serializable, NetSerializable]
public sealed class CompoundedModifiers
{
    [ViewVariables(VVAccess.ReadWrite)]
    public float recoilAdd = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float recoilMult = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public float firerateAdd = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float firerateMult = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier? damageAdd;
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier? damageMult;
    [ViewVariables(VVAccess.ReadWrite)]
    public float zoomMod = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public float soundRange = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float soundVolume = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float soundPitch = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float useSpeedMult = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public float gunCapacityAdd = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float toolCapacityAdd = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? soundOverride;

}
[Serializable, NetSerializable]
public sealed partial class RemoveAttachmentEvent : DoAfterEvent
{
    public AttSlot attachment;

    public RemoveAttachmentEvent(AttSlot attachment)
    {
        this.attachment = attachment;
    }

    public override DoAfterEvent Clone() => new RemoveAttachmentEvent(attachment);
}
public sealed class OxydModifiersSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedRadialMenuSystem _radials = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly SharedDoAfterSystem _after = default!;

    public const string cid = "oAtts";
    private static readonly ProtoId<ToolQualityPrototype> ScrewingQuality = "Screwing";
    /// <inheritdoc/>

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydAttachmentHolderComponent, ComponentInit>(onInit);
        SubscribeLocalEvent<OxydAttachmentHolderComponent, AfterInteractUsingEvent>(onUse);
        SubscribeLocalEvent<OxydAttachmentHolderComponent, RemoveAttachmentEvent>(onRemove);
    }

    public void onRemove(Entity<OxydAttachmentHolderComponent> holder, ref RemoveAttachmentEvent args)
    {
        tryRemoveAttachment(holder, args.attachment, out var removed, out var errorMsg);
    }

    public void onUse(Entity<OxydAttachmentHolderComponent> holder, ref AfterInteractUsingEvent args)
    {
        EntityUid user = args.User;
        EntityUid used = args.Used;
        if (!_player.TryGetSessionByEntity(user, out var session))
            return;
        if (TryComp<ToolComponent>(used, out var tool) && _tools.HasQuality(used, ScrewingQuality, tool))
        {
            var options = new List<RadialMenuOption>();
            var revMap = new List<AttSlot>();
            foreach (var (key , thing) in holder.Comp.attachments)
            {
                options.Add(new EntityRadialMenuOption(){ Entity = thing});
                revMap.Add(key);
            }
            _radials.ShowRadial(session, options,
                selection =>
                {
                    _after.TryStartDoAfter(new DoAfterArgs(EntityManager,
                        user,
                        TimeSpan.FromSeconds(2),
                        new RemoveAttachmentEvent(revMap[selection.Index]),
                        holder.Owner,
                        holder.Owner,
                        used));
                } , holder, true, false );
            return;
        }
        if (!TryComp<OxydAttachmentComponent>(used, out var att))
            return;
        if (!tryAddAttachment(holder, (used, att), out var errorMsg))
        {
            _popup.PopupClient(errorMsg, user, PopupType.Small);
            return;
        }
        _popup.PopupClient($"You attach the {MetaData(args.Used).EntityName}!", user);
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

    public void updateModifiers(Entity<OxydAttachmentHolderComponent> ent)
    {
        var mods = getModifiers(ent, typeof(OxydModifier));
        var compound = new CompoundedModifiers();
        foreach (var mod in mods)
            mod.addToCompound(compound);
        ent.Comp.mods = compound;
    }

    public bool tryAddAttachment(Entity<OxydAttachmentHolderComponent> ent, Entity<OxydAttachmentComponent> attachment, out string errorMsg)
    {
        errorMsg = "";
        var cont = (Container)_container.GetContainer(ent.Owner, cid);
        if (!_whitelist.IsValid(ent.Comp.whitelist, attachment.Owner))
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
