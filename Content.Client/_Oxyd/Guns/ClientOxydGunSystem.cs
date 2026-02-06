using System.Numerics;
using Content.Client._Oxyd.Framework;
using Content.Client.Items;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Client.GameStates;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;


namespace Content.Client._Oxyd.OxydGunSystem;


/// <summary>
/// This handles...
/// </summary>
public sealed partial class ClientOxydGunSystem : SharedOxydGunSystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydHandheldGunComponent, UsingMouseDownEvent>(HandleHandheldGun);
        SubscribeLocalEvent<OxydHandheldGunComponent, ItemStatusCollectMessage>(onInventoryControlRequest);
        SubscribeLocalEvent<OxydMagazineComponent, ComponentInit>(onMagazineInitialized);
        SubscribeLocalEvent<OxydGunComponent, GunAfterFireIndividualProjectileEvent>(muzzleEffect);
        _netManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>();
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>();
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>();
    }

    public void onInventoryControlRequest(Entity<OxydHandheldGunComponent> ent, ref ItemStatusCollectMessage args)
    {
        if (!TryComp<OxydGunComponent>(ent, out var gunComp))
            return;
        var firemodeSwitchButton = new TextureButton()
        {
            MinSize = new Vector2(32, 32),
            MaxSize = new Vector2(32, 32)
        };
        firemodeSwitchButton.TextureNormal = _spriteSystem.Frame0(gunComp.selectedFiremodePrototype.Icon);
        firemodeSwitchButton.OnPressed += eventargs => HandleFiremodeSwitch(eventargs, ent);
        var gunSafetyButton = new TextureButton()
        {
            TexturePath = $"/Textures/Oxyd/erisported/gunactions16.rsi/safety{(gunComp.safety ? '1' : '0')}.png",
            MinSize = new Vector2(32, 32),
            MaxSize = new Vector2(32, 32)
        };
        gunSafetyButton.OnPressed += eventargs => HandleSafetySwitch(eventargs, ent);

        var adding = new BoxContainer()
        {
            HorizontalExpand = true,
            HorizontalAlignment = Control.HAlignment.Left,
            Children =
            {
                firemodeSwitchButton,
                gunSafetyButton,
            }
        };
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
        b.TextureNormal = _spriteSystem.Frame0(gcomp.selectedFiremodePrototype.Icon);
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
        b.TexturePath = $"/Textures/Oxyd/erisported/gunactions16.rsi/safety{(gcomp.safety ? '1' : '0')}.png";
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
    public void HandleHandheldGun(Entity<OxydHandheldGunComponent> obj, ref UsingMouseDownEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;
        if (!args.holding.Contains(obj.Owner))
            return;
        if (!TryComp<OxydGunComponent>(obj, out var gun))
        {
            Log.Error($"Tried to fire handheld gun without gun component {MetaData(obj).EntityName}");
            return;
        }
        DoInterpret((obj.Owner, gun), args.user);
    }

    public void DoInterpret(Entity<OxydGunComponent> gun, EntityUid shooter)
    {
        if (gun.Comp.selectedFiremodePrototype.nextFire > _gameTiming.CurTime)
            return;

        gun.Comp.simulateAsTick = _gameTiming.CurTick;
        if (!gun.Comp.selectedFiremodePrototype.Active)
        {
            Log.Debug($"Sending new interpretation start message!");
            _netManager.ClientSendMessage(new ClientSideInterpretingFiremode()
            {
                gun = GetNetEntity(gun),
                shooter = GetNetEntity(shooter),
                clientsideStartingStep = gun.Comp.selectedFiremodePrototype.currentStep,
                clientTick = _gameTiming.CurTick,
            });
        }

        if (TryExecuteFiremodeCycle(gun.Comp.selectedFiremodePrototype, gun, shooter) && !gun.Comp.selectedFiremodePrototype.Active)
        {
            _netManager.ClientSendMessage(new ClientSideDoneInterpretingFiremode()
            {
                gun = GetNetEntity(gun),
                stoppedAt = gun.Comp.selectedFiremodePrototype.currentStep,
                clientTick = _gameTiming.CurTick,
            });
        }
    }

    public override List<Entity<OxydProjectileComponent>>? TryFireGunAt(Entity<OxydGunComponent> gun, EntityUid shooter,
        MapCoordinates targetCoordinates, MapCoordinates firingCoordinates)
    {
        if (!_gameTiming.IsFirstTimePredicted)
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

    }
}
