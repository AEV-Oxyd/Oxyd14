using System.Numerics;
using Content.Client._Oxyd.Framework;
using Content.Client.DoAfter;
using Content.Client.Hands.Systems;
using Content.Client.Items;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.ActionBlocker;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using Robust.Client.GameStates;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Client._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientOxydGunSystem : SharedOxydGunSystem
{
    [Dependency] private  IPlayerManager _playerManager = default!;
    [Dependency] private  SpriteSystem _spriteSystem = default!;


    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydHandheldGunComponent, SyncedEntityEventArgs<UsingMouseDownEvent>>(HandleHandheldGun);
        SubscribeLocalEvent<OxydHandheldGunComponent, ItemStatusCollectMessage>(onInventoryControlRequest);
        SubscribeLocalEvent<OxydMagazineComponent, ComponentInit>(onMagazineInitialized);
        //SubscribeLocalEvent<OxydChamberComponent, SyncedEntityEventArgs<UsingMouseDownEvent>>(OnTryInsertChamber);
        SubscribeLocalEvent<OxydGunComponent, GunAfterFireIndividualProjectileEvent>(afterFireIndividual);
        SubscribeLocalEvent<OxydHandheldGunComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<OxydHandheldGunComponent, DroppedEvent>(onDrop);
        SubscribeNetworkEvent<SetGunChargeEvent>(onChargeSet);
        SubscribeNetworkEvent<GunCompareFired>(onCompare);
        _netManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>();
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>();
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>();
        _netManager.RegisterNetMessage<FiremodeMouseStatus>();
    }

    public void onDrop(Entity<OxydHandheldGunComponent> ent, ref DroppedEvent args)
    {
        if (!TryComp<OxydGunComponent>(ent, out var gcomp))
            return;
        var frd = gcomp.selectedFiremodePrototype;
        if (frd.Active)
        {
            Log.Debug($"Resetting thrown weapon");
            ResetFiremode(frd, (ent.Owner, gcomp), args.User);
        }
        else
        {
            //Log.Debug($"Thrown but not reset weapon");
        }
    }

    public void onChargeSet(SetGunChargeEvent ev)
    {
        var ent = GetEntity(ev.gun);
        if (TerminatingOrDeleted(ent))
            return;
        if(TryComp<OxydGunChargeupComponent>(ent, out var ccomp))
            ccomp.charge = ev.charge;
    }

    public void HandleUnjam(Entity<OxydHandheldGunComponent> ent, EntityUid user)
    {
        Log.Error($"Trying unjam");
        if (!TryComp<OxydGunComponent>(ent, out var gcomp))
            return;
        gcomp.jammed = false;


    }

    public void onCompare(GunCompareFired args)
    {
        var ent = GetEntity(args.target);
        if (TryComp<OxydGunComponent>(ent, out var gcomp))
        {
            if (gcomp.timesFired != args.firedCount)
            {
                Log.Fatal($"Mismatched fired count on {ent}!");
            }
        }
    }

    private void OnGetInteractionVerbs(Entity<OxydHandheldGunComponent> ent,ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;
        if (!TryComp<OxydGunComponent>(ent, out var gcomp))
            return;
        if (!gcomp.jammed)
            return;

        var user = args.User;

        args.Verbs.Add(new()
        {
            Act = () => HandleUnjam(ent, user),
            Message = "Unjam!",
            Text = "Unjam your weapon!",
        });
    }

    public void onInventoryControlRequest(Entity<OxydHandheldGunComponent> ent, ref ItemStatusCollectMessage args)
    {
        if (!TryComp<OxydGunComponent>(ent, out var gunComp))
            return;
        var adding = new BoxContainer()
        {
            HorizontalExpand = true,
            HorizontalAlignment = Control.HAlignment.Left
        };
        var firemodeSwitchButton = new TextureButton()
        {
            MinSize = new Vector2(32, 32),
            MaxSize = new Vector2(32, 32)
        };
        firemodeSwitchButton.TextureNormal = _spriteSystem.Frame0(gunComp.selectedFiremodePrototype.icon);
        firemodeSwitchButton.OnPressed += eventargs => HandleFiremodeSwitch(eventargs, ent);
        adding.AddChild(firemodeSwitchButton);
        if (gunComp.hasSafety)
        {
            var gunSafetyButton = new TextureButton()
            {
                TextureNormal = _spriteSystem.Frame0(getSafetySprite(gunComp.safety)),
                MinSize = new Vector2(32, 32),
                MaxSize = new Vector2(32, 32)
            };
            gunSafetyButton.OnPressed += eventargs => HandleSafetySwitch(eventargs, ent);
            adding.AddChild(gunSafetyButton);
        }

        args.Controls.Add(adding);
    }

    public void HandleFiremodeSwitch(BaseButton.ButtonEventArgs args, Entity<OxydHandheldGunComponent> gun)
    {
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        var playerEnt = _playerManager.LocalSession!.AttachedEntity;
        if (playerEnt is null)
            return;
        if(!TryDoFiremodeSwitch((gun.Owner, gcomp), playerEnt.Value))
            return;
        var b = (TextureButton)args.Button;
        b.TextureNormal = _spriteSystem.Frame0(gcomp.selectedFiremodePrototype.icon);
        RaiseNetworkEvent(new FiremodeChangedEvent()
        {
            gun = GetNetEntity(gun.Owner),
            index = gcomp.selectedFiremodeIndex,
            switcher = GetNetEntity(playerEnt.Value)
        });
    }

    public void HandleSafetySwitch(BaseButton.ButtonEventArgs args, Entity<OxydHandheldGunComponent> gun)
    {
        if (!TryComp<OxydGunComponent>(gun, out var gcomp))
            return;
        var playerEnt = _playerManager.LocalSession!.AttachedEntity;
        if (playerEnt is null)
            return;
        if (!TryDoSafetySwitch((gun.Owner, gcomp), playerEnt.Value))
            return;
        var b = (TextureButton)args.Button;
        b.TextureNormal = _spriteSystem.Frame0(getSafetySprite(gcomp.safety));
        RaiseNetworkEvent(new GunSafetyChangedEvent()
        {
            gun = GetNetEntity(gun.Owner),
            switcher = GetNetEntity(playerEnt.Value),
            newState = gcomp.safety
        });
    }

    public void onMagazineInitialized(Entity<OxydMagazineComponent> ent, ref ComponentInit args)
    {
        _containerSystem.EnsureContainer<Robust.Shared.Containers.Container>(ent.Owner, oxydContents);
    }
    public void HandleHandheldGun(Entity<OxydHandheldGunComponent> obj, ref SyncedEntityEventArgs<UsingMouseDownEvent> args)
    {
        args.Register(0, (ev) =>
        {
            if (!_gameTiming.IsFirstTimePredicted)
                return false;
            if (ev.self.activeHeld != obj.Owner)
                return false;
            if (!TryComp<OxydGunComponent>(obj, out var gun))
            {
                Log.Error($"Tried to fire handheld gun without gun component {MetaData(obj).EntityName}");
                return false;
            }

            DoInterpret((obj.Owner, gun), ev.self.user);
            return true;
        });
    }

    public void DoInterpret(Entity<OxydGunComponent> gun, EntityUid shooter)
    {
        if (gun.Comp.jammed)
        {
            ResetFiremode(gun.Comp.selectedFiremodePrototype, gun, shooter);
            _audio.PlayEntity(getJammedSound(true), Filter.Local(), gun.Owner, true);
            return;
        }

        var firemode = gun.Comp.selectedFiremodePrototype;
        if (firemode.nextFire > _gameTiming.CurTime)
            return;


        gun.Comp.simulateAsTick = _gameTiming.CurTick;
        if (!firemode.Active)
        {
            //Log.Debug($"Sending new interpretation start message!");
            _netManager.ClientSendMessage(new ClientSideInterpretingFiremode()
            {
                gun = GetNetEntity(gun),
                clientsideStartingStep = firemode.currentStep,
                clientTick = _gameTiming.CurTick,
            });
        }
        else
        {
            firemode.ticksBehind += (int)(_gameTiming.CurTick.Value - firemode.lastInterpreted.Value ) - 1;
        }

        if (TryExecuteFiremodeCycle(firemode, gun, shooter) && !firemode.Active)
        {
            //Log.Error($"Sending end interpret at {_gameTiming.RealTime}");
            _netManager.ClientSendMessage(new ClientSideDoneInterpretingFiremode()
            {
                gun = GetNetEntity(gun),
                stoppedAt = firemode.currentStep,
                clientTick = _gameTiming.CurTick,
            });
            firemode.ticksBehind = 0;
        }
    }

    public override HashSet<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return null;
        if (!preFireChecks(gun))
            return null;
        return base.TryFireGunAt(gun, shooter, targetCoordinates, firingCoordinates);

    }

    public override void Update(float frameTime)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        base.Update(frameTime);
        var query = EntityQuery<OxydActiveFiremodeUpdatingComponent>();
        foreach (var active in query)
        {
            if(active.shooter is not null)
                DoInterpret(active.gun, active.shooter.Value);
        }
        foreach (var ent in checkActive)
        {
            if (ent.gun.Comp.keepUpdating)
            {
                if (HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun))
                    continue;
                var c = EnsureComp<OxydActiveFiremodeUpdatingComponent>(ent.gun);
                c.gun = ent.gun;
                c.FiremodePrototype = ent.firemode;
                c.shooter = ent.shooter;
            }
            else
            {
                if (!HasComp<OxydActiveFiremodeUpdatingComponent>(ent.gun))
                    continue;
                RemComp<OxydActiveFiremodeUpdatingComponent>(ent.gun);
            }
        }
        checkActive.Clear();
        visualUpdate();


    }
}
