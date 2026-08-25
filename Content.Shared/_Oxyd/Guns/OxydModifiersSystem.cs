using System.ComponentModel.Design;
using System.Diagnostics.CodeAnalysis;
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
using Robust.Shared.GameStates;
using Robust.Shared.Network;
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
    public int firerateAdd = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan waitAdd = TimeSpan.Zero;
    [ViewVariables(VVAccess.ReadWrite)]
    public double waitMult = 1d;
    [ViewVariables(VVAccess.ReadWrite)]
    public Angle accuracyAdd = Angle.Zero;
    [ViewVariables(VVAccess.ReadWrite)]
    public float accuracyMult = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public float firerateMult = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier? damageAdd;
    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier? damageMult;
    [ViewVariables(VVAccess.ReadWrite)]
            // unimplemented
    public float zoomMod = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public float soundRange = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float soundVolume = 0;
    [ViewVariables(VVAccess.ReadWrite)]
    public float soundPitch = 0;
    // unimplemented
    [ViewVariables(VVAccess.ReadWrite)]
    public float workspeedMult = 1;
    [ViewVariables(VVAccess.ReadWrite)]
    public float gunCapacityAdd = 0;
    [ViewVariables(VVAccess.ReadWrite)]
        // unimplemented
    public float toolCapacityAdd = 0;
    [ViewVariables(VVAccess.ReadWrite)]
        // unimplemented
    public SoundSpecifier? soundOverride;
    [ViewVariables(VVAccess.ReadWrite)]
    public float speedMult = 1;
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

[Serializable, NetSerializable]
public sealed partial class ModifiersUpdatedEvent : EntityEventArgs
{
    public required CompoundedModifiers mods;
}
public sealed partial class OxydModifiersSystem : EntitySystem
{
    [Dependency] private  SharedContainerSystem _container = default!;
    [Dependency] private  EntityWhitelistSystem _whitelist = default!;
    [Dependency] private  SharedPopupSystem _popup = default!;
    [Dependency] private  MetaDataSystem _meta = default!;
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  SharedRadialMenuSystem _radials = default!;
    [Dependency] private  SharedToolSystem _tools = default!;
    [Dependency] private  ISharedPlayerManager _player = default!;
    [Dependency] private  SharedDoAfterSystem _after = default!;
    [Dependency] private  INetManager _net = default!;

    public const string cid = "oAtts";
    private static  ProtoId<ToolQualityPrototype> ScrewingQuality = "Screwing";
    private EntityQuery<OxydAttachmentHolderComponent> oAttHoldQuery;
    /// <inheritdoc/>

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydAttachmentHolderComponent, ComponentInit>(onInit);
        SubscribeLocalEvent<OxydAttachmentHolderComponent, AfterInteractUsingEvent>(onUse);
        SubscribeLocalEvent<OxydAttachmentHolderComponent, RemoveAttachmentEvent>(onRemove);
        SubscribeLocalEvent<OxydAttachmentHolderComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<OxydAttachmentHolderComponent, ComponentHandleState>(OnHandleState);
        SubscribeLocalEvent<OxydAttachmentSpawnerComponent, ComponentStartup>(onStart);
        oAttHoldQuery = GetEntityQuery<OxydAttachmentHolderComponent>();
    }

    private void OnGetState(Entity<OxydAttachmentHolderComponent> ent, ref ComponentGetState args)
    {
        args.State = new OxydAttachmentHolderComponentState(ent.Comp.attachments, ent.Comp.slots, ent.Comp.mods);
    }

    private void OnHandleState(Entity<OxydAttachmentHolderComponent> ent, ref ComponentHandleState args)
    {
        if (args.Current is not OxydAttachmentHolderComponentState state)
            return;

        ent.Comp.attachments = new Dictionary<AttSlot, NetEntity>(state.Attachments);
        ent.Comp.slots = new List<AttSlot>(state.Slots);
        ent.Comp.mods = state.Mods;
        updateModifiers(ent);
    }

    public void onStart(Entity<OxydAttachmentSpawnerComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsClient)
            return;
        foreach (var id in ent.Comp.insert)
        {
            var entId = SpawnNextToOrDrop(id, ent.Owner);
            if (TerminatingOrDeleted(entId))
            {
                Log.Error($"Invalid entity {entId} in OxydAttachmentSpawnerComponent {ent}!");
                continue;
            }
            tryAddAttachment((ent, Comp<OxydAttachmentHolderComponent>(ent)), (entId, Comp<OxydAttachmentComponent>(entId)), out var errorMsg);
        }
        Dirty(ent, Comp<OxydAttachmentHolderComponent>(ent));
    }

    public bool tryGetModifiers(EntityUid target, [NotNullWhen(true)] out CompoundedModifiers? mods)
    {
        if (oAttHoldQuery.TryGetComponent(target, out var c))
        {
            mods = c.mods;
            return true;
        }

        mods = null;
        return false;
    }

    public CompoundedModifiers getModifiers(EntityUid target)
    {
        if (oAttHoldQuery.TryGetComponent(target, out var c))
        {
            return c.mods;
        }
        return new CompoundedModifiers();
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
        Dirty(ent, ent.Comp);
        RaiseLocalEvent(ent.Owner, new ModifiersUpdatedEvent { mods = compound });
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
