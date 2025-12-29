using System.Numerics;
using Content.Client._Oxyd.Framework;
using Content.Client.Items;
using Content.Shared._Oxyd.OxydGunSystem;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
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

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydHandheldGunComponent, UsingMouseDownEvent>(HandleHandheldGun);
        SubscribeLocalEvent<OxydHandheldGunComponent, ItemStatusCollectMessage>(onInventoryControlRequest);
        SubscribeLocalEvent<OxydMagazineComponent, ComponentInit>(onMagazineInitialized);
        _netManager.RegisterNetMessage<ClientSideDoneInterpretingFiremode>();
        _netManager.RegisterNetMessage<ClientSideInterpretingFiremode>();
        _netManager.RegisterNetMessage<FiremodeClientsideFiredEvent>();
    }

    public void onInventoryControlRequest(Entity<OxydHandheldGunComponent> ent, ref ItemStatusCollectMessage args)
    {
        var adding = new BoxContainer()
        {
            HorizontalExpand = true,
            HorizontalAlignment = Control.HAlignment.Left,
            Children =
            {
                new TextureButton()
                {
                    TexturePath = "/Textures/Interface/NavMap/beveled_circle.png",
                    MinSize = new Vector2(32, 32),
                    MaxSize = new Vector2(32, 32)
                },
                new TextureButton()
                {
                    TexturePath = "/Textures/Interface/NavMap/beveled_arrow_south.png",
                    MinSize = new Vector2(32, 32),
                    MaxSize = new Vector2(32, 32)
                }
            }
        };
        args.Controls.Add(adding);
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
        base.Update(frameTime);
        if (!_gameTiming.IsFirstTimePredicted)
            return;
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

    }
}
