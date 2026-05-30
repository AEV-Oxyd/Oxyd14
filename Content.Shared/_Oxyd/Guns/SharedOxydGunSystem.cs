

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Formats.Tar;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Content.Shared._Oxyd.Framework;
using Content.Shared.ActionBlocker;
using Content.Shared._Oxyd.Framework.RadialMenu;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Disposal;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory.Events;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Random.Helpers;
using Content.Shared.Stacks;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Exceptions;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.OxydGunSystem;


public record struct OxydFireDataWrap(GunFiremodePrototype firemode,Entity<OxydGunComponent> gun, EntityUid? shooter);
[Prototype("oxydGunConfig")]
public sealed partial class OxydGunConfig : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField]
    public SpriteSpecifier safetyOn = default!;

    [DataField]
    public SpriteSpecifier safetyOff = default!;

    [DataField]
    public SoundSpecifier jammedBallistic = default!;

    [DataField]
    public SoundSpecifier jammedLaser = default!;
}

[Serializable, NetSerializable]
public partial class ChamberInsertionEvent : EntityEventArgs
{
    public NetEntity inserting = NetEntity.Invalid;
    public NetEntity into = NetEntity.Invalid;
    public int slotId;

    public ChamberInsertionEvent(NetEntity inserting, NetEntity into, int slotId)
    {
        this.inserting = inserting;
        this.into = into;
        this.slotId = slotId;
    }
}

/// <summary>
/// This handles...
/// </summary>
public abstract partial class SharedOxydGunSystem : EntitySystem
{
    [Dependency] protected  SharedTransformSystem _transformSystem = default!;
    [Dependency] private  SharedOxydProjectileSystem _projectileSystem = default!;
    [Dependency] private  ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] protected  IGameTiming _gameTiming = default!;
    [Dependency] protected  INetManager _netManager = default!;
    [Dependency] private  IPrototypeManager _prototypeManager = default!;
    [Dependency] protected  SharedContainerSystem _containerSystem = default!;
    [Dependency] protected  IComponentFactory _factory = default!;
    [Dependency] protected  SharedAudioSystem _audio = default!;
    [Dependency] protected  GunChargeDecaySystem _charge = default!;
    [Dependency] protected  SharedBatterySystem _battery = default!;
    [Dependency] protected  SharedOxydHelpers _help = default!;
    [Dependency] protected  ISharedPlayerManager _players = default!;
    [Dependency] protected  OxydModifiersSystem _mods = default!;
    [Dependency] private  SharedRadialMenuSystem _radials = default!;
    [Dependency] protected  ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] protected  SharedHandsSystem _hands = default!;
    [Dependency] protected  SharedStackSystem _stacks = default!;

    private const string ammoChamberContainerName = "Oxyd_Ammo_Chamber";

    private const string magazineContainerName = "Oxyd_Magazine";

    protected const string oxydContents = "storagebase";

    protected const string configProto = "gunConfig";


    // in milisecunde
    private const float maxAcceptableFireGap = 500;

    protected HashSet<OxydFireDataWrap> checkActive = new();

    public SpriteSpecifier getSafetySprite(bool toggle)
    {
        var prot = _prototypeManager.Index<OxydGunConfig>(configProto);
        if (toggle)
            return prot.safetyOn;
        return prot.safetyOff;
    }

    public SoundSpecifier getJammedSound(bool laser)
    {
        var prot = _prototypeManager.Index<OxydGunConfig>(configProto);
        return laser ? prot.jammedLaser : prot.jammedBallistic;
    }


    public override void Initialize()
    {
        InitRecoil();
        SubscribeLocalEvent<OxydGunComponent, ComponentInit>(onGunInitialized);
        SubscribeLocalEvent<OxydMagazineChamberComponent, ComponentInit>(onMagazineChamberInit);
        SubscribeLocalEvent<OxydChamberComponent, ComponentInit>(onChamberInitialized);
        SubscribeLocalEvent<OxydMagazineChamberComponent, EntInsertedIntoContainerMessage>(OnEntInsertMag);
        SubscribeLocalEvent<OxydChamberComponent, EntInsertedIntoContainerMessage>(OnEntInsertChamber);
        SubscribeLocalEvent<OxydChamberComponent, EntRemovedFromContainerMessage>(OnEntRemoveChamber);
        SubscribeLocalEvent<OxydChamberComponent, InteractUsingEvent>(OnTryInsertLate, before: new []{typeof(ItemSlotsSystem)});
        SubscribeLocalEvent<OxydChargeComponent, ComponentInit>(onChargeInit);
        SubscribeLocalEvent<OxydChargeComponent, ChargeChangedEvent>(onBatteryCharge);
        SubscribeLocalEvent<OxydGunComponent, ModifiersUpdatedEvent>(onModifiersUpdated);
        SubscribeLocalEvent<OxydChamberExtensionComponent, AfterAutoHandleStateEvent>(OnExtensionHandleState);
        SubscribeLocalEvent<OxydRevolvingChamberComponent, AfterAutoHandleStateEvent>(OnRevolvingChamberHandleState);
        SubscribeLocalEvent<OxydRevolvingChamberComponent, InteractUsingEvent>(TryInsertRevolver, after: new []{typeof(ItemSlotsSystem)});
    }

    public void TryInsertRevolver(Entity<OxydRevolvingChamberComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        foreach (var chamber in ent.Comp.revolvingSlots)
        {
            if (chamber.count == chamber.loaded.Count)
                continue;

        }
    }

    public void OnRevolvingChamberHandleState(Entity<OxydRevolvingChamberComponent> ent,
        ref AfterAutoHandleStateEvent args)
    {
        foreach (var provider in ent.Comp.revolvingSlots)
        {
            provider.loaded.EnsureCapacity(provider.count);
        }
    }
    public void OnTryInsertLate(Entity<OxydChamberComponent> ent,ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        if (!TryComp<OxydChamberExtensionComponent>(ent, out var extend))
            return;
        var cont = _containerSystem.GetContainer(ent.Owner, oxydContents);
        var targets = _help.GetValidSlots(args.Used, (ent.Owner, null));
        if (targets.Count == 0)
        {
            return;
        }

        // check if any slots will be valid , if so , don't hijack
        foreach (var slot in targets)
        {
            if (_itemSlotsSystem.CanInsert(ent.Owner, args.Used, args.User, slot, false))
                return;
        }
        ent.Comp.silenceAutoInsert = true;
        foreach (var target in targets)
        {
            var targetIndex = ent.Comp.bulletSlot.FindIndex(inp => inp == target);
            if (targetIndex == -1)
            {
                Log.Error($"Entity {ent} had a bullet inserted for a chamber gun Slot without a linked Slot!");
                continue;
            }

            var item = target.Item;
            if (item is null || TerminatingOrDeleted(item))
            {
                Log.Error($"Slot had no item yet was returned as valid and did not return true on canInsert");
                continue;
            }
            Log.Debug($"Inserting at {targetIndex}");
            // will enter chamber, leaving itemslot open
            if (!TryInsertAmmo(extend, item, targetIndex, cont, true))
                continue;
            break;
        }
        ent.Comp.silenceAutoInsert = false;
    }

    public void OnEntInsertChamber(Entity<OxydChamberComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        var target = args.Container;
        var index = ent.Comp.bulletSlot.FindIndex(check => target == check.ContainerSlot);
        if (index == -1)
            return;
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        ent.Comp.realBullet[index] =  args.Entity;
        Log.Debug($"Inserted {args.Entity} into chamber at index {index} at tick {_gameTiming.CurTick}");
    }

    public void OnEntRemoveChamber(Entity<OxydChamberComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        var target = args.Container;
        var index = ent.Comp.bulletSlot.FindIndex(check => target == check.ContainerSlot);
        if (index == -1)
            return;
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        Log.Debug($"Removed {args.Entity} from chamber at index {index} at tick {_gameTiming.CurTick}");
        ent.Comp.realBullet[index] = EntityUid.Invalid;
        if (ent.Comp.silenceAutoInsert)
            return;
        FillAmmo(ent.Comp, (ent.Owner, Comp<OxydGunComponent>(ent.Owner)), index, _containerSystem.GetContainer(ent.Owner, oxydContents), CompOrNull<OxydChamberExtensionComponent>(ent));
    }


    private void OnExtensionHandleState(Entity<OxydChamberExtensionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        var c = CompOrNull<OxydAttachmentHolderComponent>(ent.Owner);
        if (c is null)
        {
            Log.Error($"OxydChamberExtensionComponent on {ent} has no attachmentHolder!");
            return;
        }

        foreach (var list in ent.Comp.extending)
        {
            if (list is null)
                continue;
            list.EnsureCapacity((int)c.mods.gunCapacityAdd);
        }

    }

    public void updateExtension(OxydChamberExtensionComponent ext, Entity<OxydGunComponent> gun, CompoundedModifiers? mods)
    {
        mods ??= CompOrNull<OxydAttachmentHolderComponent>(gun.Owner)?.mods;
        if (mods is null)
            return;
        var maxprovider = gun.Comp.InstanciatedFiremodes.Max(inp => inp.providerId)+1;
        Log.Debug($"OxydGunSystem: Updating chamber extension with max provider ID {maxprovider}");
        while (ext.extending.Count < maxprovider)
        {
            ext.extending.Add(null);
        }
        var providers = EntityManager.GetComponents<OxydGunProvidersComponent>(gun.Owner);
        foreach (var prov in providers)
        {
            switch(prov)
            {
                case OxydChamberComponent chamb:
                {
                    foreach (var slot in chamb.bulletSlot)
                        slot.Swap = false;
                    break;
                }
            }
        }
        for (var i = 0; i < maxprovider; i++)
        {
            var current = ext.extending[i];
            var targetSize = (int) mods.gunCapacityAdd;
            if (current == null)
            {
                ext.extending[i] = new List<NetEntity>(targetSize);
            }
            else if (current.Capacity != targetSize)
            {
                var newList = new List<NetEntity>(targetSize);
                var inserted = 0;
                if (newList.Capacity > current.Capacity)
                {
                    newList.AddRange(current);
                }
                else
                {
                    newList.AddRange(current.GetRange(0, targetSize));
                    foreach(var item in current.GetRange(targetSize, current.Capacity - targetSize))
                    {
                        _transformSystem.PlaceNextTo(GetEntity(item), gun.Owner);
                        break;
                    }
                }
                ext.extending[i] = newList;
            }
        }
        FillAmmo(gun);
        Dirty(gun.Owner, ext);
    }

    public void onModifiersUpdated(Entity<OxydGunComponent> gun, ref ModifiersUpdatedEvent args)
    {
        foreach (var firemode in gun.Comp.InstanciatedFiremodes)
        {
            firemode.ApplyMods(args.mods);
        }
        if (_netManager.IsClient)
            return;

        if (args.mods.gunCapacityAdd > 0)
        {
            var ext = EnsureComp<OxydChamberExtensionComponent>(gun);
            updateExtension(ext, gun, args.mods);
        }
    }


    public void onChargeInit(Entity<OxydChargeComponent> ent, ref ComponentInit args)
    {
        if (!TryComp<BatteryComponent>(ent, out var bat))
        {
            Log.Error($"OxydChargeComponent on {ent} has no BatteryComponent!");
            return;
        }
        ent.Comp.charge = bat.StartingCharge;
    }

    public void onBatteryCharge(Entity<OxydChargeComponent> ent, ref ChargeChangedEvent args)
    {
        // cancel update to battery if client-side and active
        if (_netManager.IsClient && _players.LocalEntity is not null)
        {
            if (_help.GetParentWithComp<OxydGunComponent>(ent.Owner, out var target))
            {
                if (_hands.EnumerateHeld((_players.LocalEntity.Value, null))
                    .ToList()
                    .Contains(target.Value.Owner) && target.Value.Comp.selectedFiremodePrototype.Active)
                    return;
            }
        }
        Log.Debug($"Oxyd battery charge from {ent.Comp.charge} to {args.CurrentCharge}");

        ent.Comp.charge = args.CurrentCharge;
    }

    public void OnEntInsertMag(Entity<OxydMagazineChamberComponent> ent,
        ref EntInsertedIntoContainerMessage args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        if (!HasComp<OxydMagazineComponent>(args.Entity))
            return;
        var target = args.Container;
        var targetIndex = ent.Comp.magazineSlot.FindIndex(itemSlot => itemSlot.ContainerSlot!.ID == target.ID);
        if (targetIndex == -1)
        {
            Log.Error($"Entity {ent} had a mag inserted for a magazine Slot without a linked Slot!");
            return;
        }
        FillAmmo(ent.Comp, (ent, Comp<OxydGunComponent>(ent)), targetIndex, _containerSystem.GetContainer(ent.Owner, oxydContents), CompOrNull<OxydChamberExtensionComponent>(ent));
    }

    public bool TryDoFiremodeSwitch(Entity<OxydGunComponent> gun, EntityUid initiator)
    {
        if (gun.Comp.selectedFiremodePrototype.Active)
            return false;
        var gcomp = gun.Comp;
        gcomp.selectedFiremodeIndex = (++gcomp.selectedFiremodeIndex) % gcomp.InstanciatedFiremodes.Count;
        Log.Debug($"Switched firemode to {gcomp.selectedFiremodeIndex}");
        return true;
    }

    public bool TryDoSafetySwitch(Entity<OxydGunComponent> gun, EntityUid initiator)
    {
        if (gun.Comp.selectedFiremodePrototype.Active)
            return false;
        gun.Comp.safety = !gun.Comp.safety;
        Log.Debug($"Switched safety to  {gun.Comp.safety}");
        return true;
    }

    public void onMagazineChamberInit(Entity<OxydMagazineChamberComponent> ent, ref ComponentInit args)
    {
        var index = 0;
        while(index < ent.Comp.bulletSlot.Count)
        {
            _itemSlotsSystem.AddItemSlot(ent.Owner, $"{ammoChamberContainerName}{index}", ent.Comp.bulletSlot[index]);
            _itemSlotsSystem.AddItemSlot(ent.Owner, $"{magazineContainerName}{index}", ent.Comp.magazineSlot[index]);
            ent.Comp.realBullet.Add(EntityUid.Invalid);
            index++;
        }

        if (TryComp<OxydChamberExtensionComponent>(ent, out var extension))
        {
            while (extension.extending.Count < ent.Comp.bulletSlot.Count)
            {
                extension.extending.Add(null);
            }
        }
    }
    public void onChamberInitialized(Entity<OxydChamberComponent> chamber, ref ComponentInit args)
    {
        var index = 0;
        while (index < chamber.Comp.bulletSlot.Count)
        {
            _itemSlotsSystem.AddItemSlot(chamber.Owner, ammoChamberContainerName, chamber.Comp.bulletSlot[index]);
            chamber.Comp.realBullet.Add(EntityUid.Invalid);
            index++;
        }

        if (TryComp<OxydChamberExtensionComponent>(chamber, out var extension))
        {
            while (extension.extending.Count < chamber.Comp.bulletSlot.Count)
            {
                extension.extending.Add(null);
            }
        }
    }

    public void FillAmmo(OxydMagazineChamberComponent magazines,Entity<OxydGunComponent> gun, int i, BaseContainer container, OxydChamberExtensionComponent? extend = null)
    {
        var slot = magazines.magazineSlot[i];
        var item = slot.Item;
        // pull from extension
        if (item is null)
        {
            FillAmmo((OxydChamberComponent)magazines, gun, i, container, extend);
            return;
        }
        if (!TryComp<OxydMagazineComponent>(slot.Item, out var magComp))
            return;
        var mag = (item.Value, magComp);
        if (extend is not null)
        {
            // fill buffer
            TryInsertAmmo(extend, mag, i, container);
            if(FillAmmo((OxydChamberComponent)magazines, gun, i, container, extend))
                TryInsertAmmo(extend,  mag, i, container);
        }
        else
        {
            var bull = TryGetMagBullet(i, mag);
            // pull from buffer
            if (bull is null)
            {
                FillAmmo((OxydChamberComponent)magazines, gun, i, container, extend);
                return;
            }
            FillAmmo((OxydChamberComponent)magazines, gun, i, container, bull.Value, extend);
        }
    }

    public void fillAmmo(OxydRevolvingChamberComponent revolving,
        Entity<OxydGunComponent> gun,
        int i,
        BaseContainer container,
        OxydChamberExtensionComponent? extend = null)
    {
        var item = revolving.bulletSlot[i].Item;
        if (item is not null)
        {
            return;
        }

        if (extend is not null && TryGetAmmo(extend, i, container, out var newBullet))
        {

        }
    }

    public bool FillAmmo(OxydChamberComponent chamber,
        Entity<OxydGunComponent> gun,
        int i,
        BaseContainer container,
        OxydChamberExtensionComponent? extend = null)
    {
        var item = chamber.bulletSlot[i].Item;
        if (item is not null)
            return false;
        if (extend is not null && TryGetAmmo(extend, i, container, out var newBullet))
        {
            if (_containerSystem.InsertOrDrop(newBullet.Value, chamber.bulletSlot[i].ContainerSlot!))
            {
                chamber.realBullet[i] = newBullet.Value;
                return true;
            }
        }
        return false;
    }

    public bool FillAmmo(OxydChamberComponent chamber,
        Entity<OxydGunComponent> gun,
        int i,
        BaseContainer container,
        EntityUid bullet,
        OxydChamberExtensionComponent? extend = null)
    {
        var item = chamber.bulletSlot[i].Item;
        if (item is not null)
            return false;
        if (extend is not null)
        {
            if (TryGetAmmo(extend, i, container, out var newBullet))
            {
                if (_containerSystem.InsertOrDrop(newBullet.Value, chamber.bulletSlot[i].ContainerSlot!))
                {
                    chamber.realBullet[i] = newBullet.Value;
                }

                if (!TryInsertAmmo(extend, newBullet, i, container))
                    return false;
                return true;
            }
        }
        if (_containerSystem.InsertOrDrop(bullet, chamber.bulletSlot[i].ContainerSlot!))
        {
            chamber.realBullet[i] = bullet;
            return true;
        }

        return false;
    }
    // USE INHAND VERSION for interactions , this is for internals, predicted
    public bool TryInsertAmmo(OxydChamberExtensionComponent extension, EntityUid? bullet, int i, BaseContainer container, bool pushBack = false)
    {
        if (bullet is null)
            return false;
        var targ = extension!.extending[i];
        if (targ is null)
            return false;
        if (bullet == EntityUid.Invalid)
            return false;
        var c = GetNetEntity(bullet.Value);
        // insertion hppened on predicted tick
        if (targ.Count >= targ.Capacity)
        {
            if(!(targ.Contains(c) && !_gameTiming.IsFirstTimePredicted))
                return false;
        }
        _containerSystem.Insert(bullet.Value, container, null, false);
        // prediction compatibility required hack
        if (!_gameTiming.IsFirstTimePredicted)
            return true;
        if(pushBack)
            targ.Insert(0, c);
        else
            targ.Add(c);
        return true;
    }

    public bool TryInsertAmmo(OxydChamberExtensionComponent extension, EntityUid bullet, int i, BaseContainer container, bool pushBack, EntityUid user)
    {
        var targ = extension!.extending[i];
        if (targ is null)
            return false;
        if(targ.Count >= targ.Capacity)
            return false;
        if (bullet == EntityUid.Invalid)
            return false;
        if (!_hands.TryDropIntoContainer(user, bullet, container))
            return false;

        var c = GetNetEntity(bullet);
        // prediction compatibility required hack
        if (!_gameTiming.IsFirstTimePredicted)
            return true;
        if(pushBack)
            targ.Insert(0, c);
        else
            targ.Add(c);
        return true;
    }
    public bool TryInsertAmmo(OxydChamberExtensionComponent extension,
        Entity<OxydMagazineComponent> magazine,
        int i,
        BaseContainer container,
        bool pushBack = false)
    {
        if (magazine.Comp.loadedBullets.Count == 0)
            return false;
        var targ = extension!.extending[i];
        if (targ is null)
            return false;
        if(targ.Count >= targ.Capacity)
            return false;
        while (targ.Count < targ.Capacity)
        {
            var bullet = TryGetMagBullet(i, magazine);
            if (bullet is null)
                break;
            _containerSystem.Insert(bullet.Value, container, null, false);
            if(pushBack)
                targ.Insert(0, GetNetEntity(bullet.Value));
            else
                targ.Add(GetNetEntity(bullet.Value));
        }
        return true;
    }

    public bool TryGetAmmo(OxydChamberExtensionComponent extension, int i ,BaseContainer container, [NotNullWhen(true)] out EntityUid? bullet)
    {
        bullet = null;
        var listRef = extension?.extending[i];
        if (listRef is null)
            return false;
        if (listRef.Count == 0)
            return false;
        bullet = GetEntity(listRef[0]);
        _containerSystem.Remove(bullet.Value, container);
        if(_gameTiming.IsFirstTimePredicted)
           listRef.RemoveAt(0);
        return true;

    }
    public void FillAmmo(Entity<OxydGunComponent> gun)
    {
        var comps = EntityManager.GetComponents<OxydGunProvidersComponent>(gun);
        var extend = CompOrNull<OxydChamberExtensionComponent>(gun);
        var container = _containerSystem.GetContainer(gun.Owner, oxydContents);
        foreach (var comp in comps)
        {
            switch (comp)
            {
                case OxydMagazineChamberComponent magazines:
                {
                    for (var i = 0; i < magazines.magazineSlot.Count; i++)
                    {
                        FillAmmo(magazines, gun, i, container,extend);
                    }
                    break;
                }
                case OxydChamberComponent chamber:
                {
                    for (var i = 0; i < chamber.bulletSlot.Count; i++)
                    {
                        var item = chamber.bulletSlot[i].Item;
                        if (item is not null)
                            continue;
                        if (extend is not null && TryGetAmmo(extend, i, container, out var newBullet))
                        {
                            if(_containerSystem.InsertOrDrop(newBullet.Value, chamber.bulletSlot[i].ContainerSlot!))
                                chamber.realBullet[i] = newBullet.Value;
                        }
                    }
                    break;
                }

            }
        }
    }

    public void FillAmmo(Entity<OxydGunComponent> gun,  GunFiremodePrototype fireProto)
    {
        var targ = fireProto.AmmoProviders;
        var cont = _containerSystem.GetContainer(gun.Owner, oxydContents);
        var extens = CompOrNull<OxydChamberExtensionComponent>(gun);
        switch (targ)
        {
            case OxydMagazineChamberComponent magazines:
            {
                FillAmmo(magazines, gun, fireProto.providerId, cont,extens);
                break;
            }
            case OxydChamberComponent chamber:
            {
                if (extens?.extending[fireProto.providerId] is not null && TryGetAmmo(extens, fireProto.providerId, cont, out var newBullet))
                {
                    if(_containerSystem.InsertOrDrop(newBullet.Value, chamber.bulletSlot[fireProto.providerId].ContainerSlot!))
                        chamber.realBullet[fireProto.providerId] = newBullet.Value;
                }

                break;
            }
            case OxydGunLaserProviderComponent:
                break;
            default:
                Log.Error($"Unimplemented case in fillAmmo, type {targ} for fireProto");
                break;

        }
    }
    public EntityUid? TryGetMagBullet(int index, Entity<OxydMagazineComponent> mag)
    {
        if (mag.Comp.loadedBullets.Count == 0)
             return null;
        var cnt = _containerSystem.GetContainer(mag, oxydContents);
        var ent = GetEntity(mag.Comp.loadedBullets.Pop());
        if (ent == EntityUid.Invalid)
        {
            Log.Debug($"Invalid entity popped in cycle mag!");
            return null;
        }
        _containerSystem.Remove(ent, cnt, true, true);
        return ent;
    }



    public abstract bool InterpretStep(
        GunFiremodePrototype firemodePrototype,
        OxydGunEffect effect,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter);




    public Vector2 GetBulletInitialMovementDirection(Entity<OxydProjectileComponent> projectile, Entity<OxydGunComponent> gun, CompoundedModifiers mods,  MapCoordinates shootingFrom, MapCoordinates targetPos, EntityUid shooter)
    {
        var firemode = gun.Comp.selectedFiremodePrototype;
        var seed = SharedRandomExtensions.HashCodeCombine( new int[]{ GetNetEntity(gun).Id, (int)gun.Comp.timesFired });
        //Log.Error($"Seed is {seed}");
        var rand = new System.Random(seed);
        var ev = new GunGetInaccuracyEvent()
        {
            addedInaccuracy = firemode.addedInaccuracyMaximum/2,
            baseInaccuracy = firemode.baseInaccuracy/2,
            simTick = gun.Comp.simulateAsTick
        };
        RaiseLocalEvent(gun.Owner, ev);
        if(shooter != gun.Owner)
            RaiseLocalEvent(shooter, ev);
        Log.Debug($"b: {ev.baseInaccuracy.Degrees}, a: {ev.addedInaccuracy.Degrees}");
        var inaccuracyDebuff = (ev.baseInaccuracy + rand.NextSingle() * ev.addedInaccuracy);
        inaccuracyDebuff *= rand.NextSingle() > 0.5f ? 1 : -1;
        //Log.Debug($"{inaccuracyDebuff.Degrees} shotCount of {gun.Comp.timesFired} at tick {_gameTiming.CurTick} , realTime {_gameTiming.RealTime}");
        return ((targetPos.Position - shootingFrom.Position).Normalized().ToAngle() + inaccuracyDebuff).ToVec();
    }

    public bool tryDischargeAmount(EntityUid gun, float amount,[NotNullWhen(true)] out EntityUid? used)
    {
        foreach (var container in _containerSystem.GetAllContainers(gun))
        {
            if (container is not ContainerSlot slot)
                continue;
            if (slot.ContainedEntity is null)
                continue;
            if (!TryComp<OxydChargeComponent>(slot.ContainedEntity, out var batt))
                continue;
            if (batt.charge < amount)
                continue;

            batt.charge -= amount;
            _battery.UseCharge((slot.ContainedEntity.Value, null), amount);
            used = slot.ContainedEntity;
            return true;

        }

        used = null;
        return false;
    }

    public bool hasDischargeAmount(EntityUid gun, float amount)
    {
        foreach (var container in _containerSystem.GetAllContainers(gun))
        {
            if (container is not ContainerSlot slot)
                continue;
            if (slot.ContainedEntity is null)
                continue;
            if (!TryComp<OxydChargeComponent>(slot.ContainedEntity, out var batt))
                continue;
            if (batt.charge >= amount)
                return true;
        }
        return false;
    }

    public bool tryGetProviderAmmo(Entity<OxydGunComponent> gun,
        [NotNullWhen(true)] out EntProtoId? projectile,
        [NotNullWhen(true)] out EntityUid? ammo)
    {
        ammo  = null;
        projectile = null;
        var frd = gun.Comp.selectedFiremodePrototype;
        switch (frd.AmmoProviders)
        {
            // magazine uses the same handling
            case OxydChamberComponent provider:
                ammo = provider.realBullet[frd.providerId];
                if (TerminatingOrDeleted(ammo))
                    return false;
                projectile = Comp<OxydBulletComponent>(ammo.Value).projectileEntity;
                return ammo.Value != EntityUid.Invalid;
            case OxydGunLaserProviderComponent provider:
                projectile = provider.laserProto[frd.providerId].laser;
                if (tryDischargeAmount(gun.Owner, provider.laserProto[frd.providerId].cost, out var used))
                {
                    ammo = used;
                }

                if (ammo is null)
                    return false;
                return ammo.Value != EntityUid.Invalid;
            default:
                Log.Error($"Unimplemented ammoProvider in getProviderAmmo,  type {frd.AmmoProviders}");
                projectile = null;
                return false;
        }
    }

    public bool hasProviderAmmo(Entity<OxydGunComponent> gun, int index)
    {
        var frd = gun.Comp.selectedFiremodePrototype;
        switch (frd.AmmoProviders)
        {
            // magazine also fits here
            case OxydChamberComponent provider:
                return provider.realBullet[index] != EntityUid.Invalid;
            case OxydGunLaserProviderComponent provider:
                return hasDischargeAmount(gun.Owner, provider.laserProto[index].cost);
            default:
                Log.Error($"Unimplemented hasProviderAmmo case ,  type {frd.AmmoProviders}");
                return false;
        }
    }

    public void afterProviderAmmo(Entity<OxydGunComponent> gun, EntityUid bullet)
    {
        var frd = gun.Comp.selectedFiremodePrototype;
        switch (frd.AmmoProviders)
        {
            case OxydChamberComponent provider:
                var slot = provider.bulletSlot[frd.providerId];
                // will cause a refill from EntEjectedMessage
                if (_itemSlotsSystem.TryEject(gun, slot, null, out _))
                {
                    _help.QueueDel(bullet);
                }

                break;
            case OxydGunLaserProviderComponent provider:
                break;
            default:
                Log.Error($"Unimplemented afterProviderAmmo case ,  type {frd.AmmoProviders}");
                break;
        }
    }

    public void applyMods(Entity<OxydProjectileComponent> projectile, CompoundedModifiers mods)
    {
        OxydProjectileApplyDamageComponent? dc = null;
        OxydBulletOnFireRecoilComponent? rc = null;
        TryComp<OxydProjectileApplyDamageComponent>(projectile, out dc);
        TryComp<OxydBulletOnFireRecoilComponent>(projectile, out rc);

        if (mods.damageAdd is not null)
        {
            dc ??= EnsureComp<OxydProjectileApplyDamageComponent>(projectile);
            dc.DamageSpecifier += mods.damageAdd;
        }

        if (mods.damageMult is not null && dc is not null)
        {
            dc.DamageSpecifier = DamageHelpers.multiply(dc.DamageSpecifier, mods.damageMult);
        }

        if (mods.recoilAdd != 0)
        {
            rc ??= EnsureComp<OxydBulletOnFireRecoilComponent>(projectile);
            rc.recoil += mods.recoilAdd;
        }

        if (mods.recoilMult != 1 && rc is not null)
        {
            rc.recoil *= mods.recoilMult;
        }

    }


    public bool getProjectileLoaded(EntityUid shooter, Entity<OxydGunComponent> gun,CompoundedModifiers mods,
        [NotNullWhen(true)] out Entity<OxydProjectileComponent>? outputComp,
        [NotNullWhen(true)] out EntityUid? used)
    {
        outputComp = null;
        used = null;
        if (!tryGetProviderAmmo(gun, out var proj, out var ammoEnt))
            return false;
        EntityUid projectile = Spawn(proj.ToString(), MapCoordinates.Nullspace);
        var projectileComp = EnsureComp<OxydProjectileComponent>(projectile);
        projectileComp.firedFrom = gun.Owner;
        projectileComp.shotBy = shooter;
        applyMods((projectile, projectileComp), mods);
        if (TryComp<OxydBulletComponent>(ammoEnt, out var ammoBullet))
        {
            projectileComp.initialMovement = new Vector2(ammoBullet.Speed, ammoBullet.Speed);
        }
        outputComp = (projectile, projectileComp);
        used = ammoEnt;
        return true;
    }


    public static TimeSpan getTotalWait(GunFiremodePrototype target)
    {
        TimeSpan totalWait = TimeSpan.Zero;
        foreach (var effect in target.Effects)
        {
            switch (effect)
            {
                case GunEffectWait wait:
                    totalWait += wait.waitPeriod;
                    break;
            }
        }
        return totalWait;
    }

    public HashSet<Entity<OxydProjectileComponent>> fireGun(EntityUid shooter,
        Entity<OxydGunComponent> gun,
        MapCoordinates shootingFrom,
        MapCoordinates targetPos)
    {
        GunFiremodePrototype gunFiremodePrototype = gun.Comp.selectedFiremodePrototype;
        var mods = _mods.getModifiers(gun.Owner);
        AudioParams param = AudioParams.Default;
        param.Volume += mods.soundVolume;
        param.Pitch += mods.soundPitch;
        var aFireDelay = gunFiremodePrototype.fireDelay;
        var aTotalWait = gunFiremodePrototype.totalWait;
        var lastFireDelta = _gameTiming.CurTime - gunFiremodePrototype.nextFire - aTotalWait;
        Log.Debug($"Last fire delta is {lastFireDelta}, totalWait {aTotalWait}, gap {gunFiremodePrototype.firingGaps}");
        gunFiremodePrototype.nextFire = _gameTiming.CurTime + aFireDelay;
        gun.Comp.firingTime += gunFiremodePrototype.fireDelay;
        //Log.Debug($"Fire Delta is {lastFireDelta}");
        if (lastFireDelta > aFireDelay && lastFireDelta < TimeSpan.FromMilliseconds(maxAcceptableFireGap) && gunFiremodePrototype.firingGaps < TimeSpan.FromMilliseconds(maxAcceptableFireGap))
        {
            gunFiremodePrototype.firingGaps += lastFireDelta - aFireDelay;
            Log.Debug($"Accumulating firegap of {gunFiremodePrototype.firingGaps}");
        }
        gunFiremodePrototype.lastFiredTick = _gameTiming.CurTick;
        if (aFireDelay < _gameTiming.TickPeriod)
        {
            gun.Comp.firingTime += (_gameTiming.TickPeriod - aFireDelay);
        }
        HashSet<Entity<OxydProjectileComponent>> projectiles = new();
        var sameTickCounter = 0;
        if (gunFiremodePrototype.SingleShot && gun.Comp.firingTime >= aFireDelay * 2)
            gun.Comp.firingTime = aFireDelay;
        while (gun.Comp.firingTime >= aFireDelay)
        {
            if(!getProjectileLoaded(shooter, gun, mods, out var projectileNullable, out var used))
                return projectiles;
            var shootSound = gunFiremodePrototype.fireSound;
            var shootEv = new GunBeforeFireIndividualProjectileEvent()
            {
                projectile = projectileNullable.Value,
                simTick = gun.Comp.simulateAsTick
            };
            RaiseLocalEvent(gun.Owner, shootEv);
            if(shooter != gun.Owner)
                RaiseLocalEvent(shooter, shootEv);
            gun.Comp.firingTime -= aFireDelay;
            Entity<OxydProjectileComponent> projectile = projectileNullable.Value;
            projectile.Comp.initialMovement *= gunFiremodePrototype.SpeedMultiplier;
            projectile.Comp.initialMovement *= GetBulletInitialMovementDirection(projectile, gun, mods, shootingFrom, targetPos, shooter);
            projectile.Comp.initialPosition = shootingFrom.Offset(projectile.Comp.initialMovement * sameTickCounter * (float)aFireDelay.TotalSeconds);
            _transformSystem.SetWorldRotationNoLerp(projectile.Owner, projectile.Comp.initialMovement.ToAngle());
            projectile.Comp.aimedPosition = targetPos;
            projectiles.Add(projectile);
            _projectileSystem.queueProjectile(projectile);
            gun.Comp.timesFired++;
            sameTickCounter++;
            var afterEv = new GunAfterFireIndividualProjectileEvent()
            {
                projectile = projectileNullable.Value,
                simTick = gun.Comp.simulateAsTick
            };
            var filter = Filter.Pvs(gun, _help.getRangeToPvsMultiplier(25f + mods.soundRange));
            if (_netManager.IsServer)
                filter.RemoveWhereAttachedEntity(play => play == shooter);
            _audio.PlayEntity(_audio.ResolveSound(shootSound), filter, gun.Owner, true, param.WithPlayOffset((float)aFireDelay.TotalSeconds));
            RaiseLocalEvent(gun.Owner, afterEv);
            if(shooter != gun.Owner)
                RaiseLocalEvent(shooter, afterEv);
            afterProviderAmmo(gun, used.Value);
        }

        RaiseLocalEvent(gun.Owner, new GunFiredEvent()
        {
            projectiles = projectiles,
            simTick = gun.Comp.simulateAsTick
        });

        // fallback
        if (gun.Comp.timesFired > int.MaxValue - 1000)
        {
            gun.Comp.timesFired = 0;
        }
        RaiseLocalEvent(new FiremodeProjectilesFiredEvent()
        {
            gun = gun,
            projectiles = projectiles,
            shooter = shooter
        });
        return projectiles;
    }

    public MapCoordinates resolveFiringPosition(Entity<OxydHandheldGunComponent> obj, MapCoordinates targetPos, EntityUid shooter)
    {
        if(!TryComp<FixturesComponent>(shooter, out var fixtHolder))
            return MapCoordinates.Nullspace;
        var map = _transformSystem.GetMapCoordinates(shooter);
        var radius = fixtHolder.Fixtures.Values.First().Shape.Radius;
        var mapOffset = (targetPos.Position - map.Position).Normalized();
        mapOffset *= radius;
        return map.Offset(mapOffset);
    }

    public void onGunInitialized(Entity<OxydGunComponent> gun, ref ComponentInit args)
    {


        foreach (var proto in gun.Comp.firemodes)
        {
            var newFiremode = _prototypeManager.Index<GunFiremodePrototype>(proto).createCopy();
            newFiremode.Initialize();
            if (!_factory.TryGetRegistration(newFiremode.providerComp, out var registration))
            {
                Log.Debug($"Invalid ammoprovider component {newFiremode.providerComp} for firemode prototype {proto}");
                continue;
            }

            if (!EntityManager.TryGetComponent(gun.Owner, registration, out var providerComp))
            {
                Log.Debug($"Gun Prototype {MetaData(gun.Owner).EntityPrototype} did not have the required providerComp {newFiremode.providerComp} forfiremode proto {proto}");
                continue;
            }

            newFiremode.AmmoProviders = (OxydGunProvidersComponent)providerComp;

            gun.Comp.InstanciatedFiremodes.Add(newFiremode);

        }
        gun.Comp.selectedFiremodeIndex = 0;
        _containerSystem.EnsureContainer<Container>(gun, oxydContents);
    }


    public void onEmptyShootAttempt()
    {

    }

    public void onInvalidShootAttempt()
    {

    }

    public void onSafetyShootAttempt()
    {

    }

    public bool preFireChecks(Entity<OxydGunComponent> gun)
    {
        if (gun.Comp.safety)
        {
            onSafetyShootAttempt();
            return false;
        }
        if (!hasProviderAmmo(gun, gun.Comp.selectedFiremodePrototype.providerId))
        {
            onEmptyShootAttempt();
            return false;
        }

        return true;
    }

    public virtual HashSet<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        var gfp = gun.Comp.selectedFiremodePrototype;
        if (gfp.nextFire > _gameTiming.CurTime)
        {
            Log.Debug("Firemode not ready");
            return null;
        }
        return fireGun(shooter, gun, firingCoordinates, targetCoordinates);
    }

    public void EnsureActiveUpdating(GunFiremodePrototype fireProto,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        checkActive.Add(new OxydFireDataWrap(fireProto, gun, shooter));
        gun.Comp.keepUpdating = true;
    }
    public void RemoveActiveUpdating(GunFiremodePrototype fireProto,
        Entity<OxydGunComponent> gun,
        EntityUid? shooter)
    {
        Log.Debug($"Queued for active removal {gun.Owner}");
        checkActive.Add(new OxydFireDataWrap(fireProto, gun, shooter));
        gun.Comp.keepUpdating = false;
    }


    public bool TryExecuteFiremodeCycle(GunFiremodePrototype firemodePrototype, Entity<OxydGunComponent> gun, EntityUid? shooter)
    {
        Log.Debug($"Executing firecycle at {_gameTiming.CurTick}");
        if (gun.Comp.jammed)
        {
            ResetFiremode(firemodePrototype, gun, shooter);
            Log.Debug($"Interpret failed: jam");
            return false;
        }

        if (firemodePrototype.nextFire > _gameTiming.CurTime && _netManager.IsClient)
        {
            Log.Debug($"Interpret failed: nextFire");
            return false;
        }

        if (firemodePrototype.lastInterpreted == _gameTiming.CurTick)
        {
            Log.Debug($"Interpret failed: sameTick");
            return false;
        }

        firemodePrototype.Active = true;
        firemodePrototype.lastInterpreted = _gameTiming.CurTick;
        while (firemodePrototype.currentStep < firemodePrototype.maxSteps)
        {
            //Log.Debug($"Interpreting step {firemodePrototype.currentStep} of {firemodePrototype.maxSteps} , step is {firemodePrototype.Effects[firemodePrototype.currentStep]} at tick {_gameTiming.CurTick}, time is {_gameTiming.CurTime}");
            if (!InterpretStep(firemodePrototype, firemodePrototype.Effects[firemodePrototype.currentStep], gun, shooter))
            {
                break;
            }
            firemodePrototype.currentStep++;
        }

        if (firemodePrototype.currentStep == firemodePrototype.maxSteps)
        {
            firemodePrototype.currentStep = 0;
            firemodePrototype.Active = false;
        }

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        HandleActiveRecoil();
    }
}
