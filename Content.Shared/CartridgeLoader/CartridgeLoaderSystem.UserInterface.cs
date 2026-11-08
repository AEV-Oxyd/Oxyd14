using System.Linq;
using Content.Shared._Oxyd.Computers;

namespace Content.Shared.CartridgeLoader;

public sealed partial class CartridgeLoaderSystem
{
    private void OnLoaderUiMessage(Entity<CartridgeLoaderComponent> ent, ref CartridgeLoaderUiMessage message)
    {
        var cartridge = GetEntity(message.CartridgeUid);

        switch (message.Action)
        {
            case CartridgeUiMessageAction.Activate:
                ActivateProgram(ent, cartridge);
                break;
            case CartridgeUiMessageAction.Deactivate:
                DeactivateProgram(ent, cartridge);
                break;
            case CartridgeUiMessageAction.Install:
                InstallCartridge(ent, cartridge);
                break;
            case CartridgeUiMessageAction.Uninstall:
                // Do not allow uninstalling installed programs via UI message from app hub
                break;
            case CartridgeUiMessageAction.UIReady:
                var targetProg = Exists(cartridge) ? cartridge : ent.Comp.ActiveProgram;
                if (targetProg is { } target && Exists(target))
                {
                    var evt = new CartridgeUiReadyEvent(ent);
                    RaiseLocalEvent(target, ref evt);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException($"Unrecognized UI action passed from cartridge loader ui {message.Action}.");
        }
    }

    /// <summary>
    /// Relays ui messages meant for cartridges to the currently active cartridge.
    /// In multi-window/OS setups, if the message is targeted at a specific program or the OS shell,
    /// it routes directly to the intended program or system boot program.
    /// </summary>
    private void OnUiMessage(Entity<CartridgeLoaderComponent> ent, ref CartridgeUiMessage args)
    {
        var cartridgeEvent = args.MessageEvent;
        cartridgeEvent.User = args.Actor;
        cartridgeEvent.LoaderUid = GetNetEntity(ent);
        cartridgeEvent.Actor = args.Actor;

        // Route OS UI events directly to the system boot program (the OS cartridge)
        if (cartridgeEvent is OsUiMessageEvent)
        {
            if (TryComp<OxydComputerComponent>(ent.Owner, out var oxydComp) &&
                oxydComp.BootProgram is { } bootProg &&
                Exists(bootProg) &&
                HasComp<OsCartridgeComponent>(bootProg))
            {
                RaiseLocalEvent(bootProg, cartridgeEvent);
                return;
            }

            var osProgram = GetAllPrograms(ent.Owner).FirstOrDefault(p => HasComp<OsCartridgeComponent>(p));
            if (osProgram.IsValid() && Exists(osProgram))
            {
                RaiseLocalEvent(osProgram, cartridgeEvent);
                return;
            }
        }

        if (ent.Comp.ActiveProgram is { } foreground)
            RaiseLocalEvent(foreground, cartridgeEvent);
    }

    /// <summary>
    /// Updates the programs ui state.
    /// </summary>
    /// <param name="loaderUid">The cartridge loaders entity uid.</param>
    /// <param name="state">The programs ui state. Programs should use their own ui state class inheriting from <see cref="BoundUserInterfaceState"/>.</param>
    /// <param name="loader">The cartridge loader component.</param>
    /// <remarks>
    /// This method is called "UpdateCartridgeUiState" but cartridges and a programs are the same. A cartridge is just a program as a visible item.
    /// </remarks>
    /// <seealso cref="Cartridges.NotekeeperCartridgeSystem.UpdateUiState"/>
    public void UpdateCartridgeUiState(EntityUid loaderUid, BoundUserInterfaceState state, CartridgeLoaderComponent? loader = null)
    {
        if (!Resolve(loaderUid, ref loader))
            return;

        // Store the program's UI state directly on the Program entity's ProgramStateComponent
        // so that each program entity owns its own networked state for PVS and BUI load reduction.
        if (loader.ActiveProgram is { } activeProg && Exists(activeProg))
        {
            var progState = EnsureComp<ProgramStateComponent>(activeProg);
            progState.State = state;
            progState.LoaderEntity = GetNetEntity(loaderUid);
            Dirty(activeProg, progState);
        }

        if (TryComp<Content.Shared.PDA.PdaComponent>(loaderUid, out var pda))
        {
            if (state is Content.Shared._Oxyd.Computers.OsUiState osState)
            {
                pda.OsUiState = osState;
                if (loader.ActiveProgram == null || (TryComp<OxydComputerComponent>(loaderUid, out var comp) && loader.ActiveProgram == comp.BootProgram))
                    pda.ActiveCartridgeState = osState;
            }
            else
            {
                pda.ActiveCartridgeState = state;
            }

            _pdaSystem.UpdatePdaUi(loaderUid, pda);
            return;
        }

        if (_userInterface.HasUi(loaderUid, loader.UiKey))
            _userInterface.SetUiState(loaderUid, loader.UiKey, state);
        if (_userInterface.HasUi(loaderUid, Content.Shared.PDA.ComputerUiKey.Key))
            _userInterface.SetUiState(loaderUid, Content.Shared.PDA.ComputerUiKey.Key, state);
        if (_userInterface.HasUi(loaderUid, Content.Shared.PDA.PdaUiKey.Key))
            _userInterface.SetUiState(loaderUid, Content.Shared.PDA.PdaUiKey.Key, state);
    }


    /// <summary>
    /// Updates the cartridge loaders ui state.
    /// </summary>
    /// <remarks>
    /// Because the cartridge loader integrates with the ui of the entity using it, the entities ui state needs to inherit from <see cref="CartridgeLoaderUiState"/>
    /// and use this method to update its state so the cartridge loaders state can be added to it.
    /// </remarks>
    /// <seealso cref="PDA.PdaSystem.UpdatePdaUserInterface"/>
    public void UpdateUiState(Entity<CartridgeLoaderComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (HasComp<Content.Shared.PDA.PdaComponent>(ent.Owner))
            return;

        var programs = GetNetEntityList(GetAllPrograms(ent).ToList());
        var state = new CartridgeLoaderUiState(programs, GetNetEntity(ent.Comp.ActiveProgram));
        if (_userInterface.HasUi(ent.Owner, ent.Comp.UiKey))
            _userInterface.SetUiState(ent.Owner, ent.Comp.UiKey, state);
        if (_userInterface.HasUi(ent.Owner, Content.Shared.PDA.ComputerUiKey.Key))
            _userInterface.SetUiState(ent.Owner, Content.Shared.PDA.ComputerUiKey.Key, state);
        if (_userInterface.HasUi(ent.Owner, Content.Shared.PDA.PdaUiKey.Key))
            _userInterface.SetUiState(ent.Owner, Content.Shared.PDA.PdaUiKey.Key, state);
    }
}
