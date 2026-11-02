using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared.CartridgeLoader;

public sealed partial class CartridgeLoaderSystem
{
    public Entity<T>? TryGetProgram<T>(Entity<CartridgeLoaderComponent?> ent) where T : IComponent
    {
        if (!Resolve(ent, ref ent.Comp))
            return null;

        foreach (var prog in GetAllPrograms(ent))
        {
            if (!TryComp<T>(prog, out var program))
                continue;

            return (prog, program);
        }

        return null;
    }

    public bool HasProgram<T>(Entity<CartridgeLoaderComponent?> ent) where T : IComponent
    {
        return TryGetProgram<T>(ent).HasValue;
    }

    public IEnumerable<EntityUid> GetAllPrograms(EntityUid uid)
    {
        if (_itemSlotsSystem.GetItemOrNull(uid, CartridgeLoaderComponent.CartridgeSlotId) is { } cartridge)
            return GetDiskPrograms(uid).Append(cartridge);

        return GetDiskPrograms(uid);
    }

    public IEnumerable<EntityUid> GetDiskPrograms(EntityUid uid)
    {
        return GetPreinstalledPrograms(uid).Concat(GetRemovablePrograms(uid));
    }

    public IReadOnlyList<EntityUid> GetRemovablePrograms(EntityUid uid)
    {
        if (_container.TryGetContainer(uid, CartridgeLoaderComponent.RemovableContainerId, out var container))
            return container.ContainedEntities.Where(e => !EntityManager.IsQueuedForDeletion(e)).ToList();

        return [];
    }

    public IReadOnlyList<EntityUid> GetPreinstalledPrograms(EntityUid uid)
    {
        if (_container.TryGetContainer(uid, CartridgeLoaderComponent.UnremovableContainerId, out var container))
            return container.ContainedEntities.Where(e => !EntityManager.IsQueuedForDeletion(e)).ToList();

        return [];
    }

    public int UsedDiskSpace(EntityUid uid)
    {
        return GetRemovablePrograms(uid).Count + GetPreinstalledPrograms(uid).Count;
    }

    private bool HasProgram(EntityUid uid, EntityUid program)
    {
        return GetAllPrograms(uid).Contains(program);
    }

    public void ActivateProgram(Entity<CartridgeLoaderComponent> ent, EntityUid program)
    {
        if (!HasProgram(ent, program))
            return;

        var prevActive = ent.Comp.ActiveProgram;
        if (prevActive == program)
            return;

        ent.Comp.ActiveProgram = program;
        Dirty(ent);

        if (prevActive is { } active)
        {
            var deactEvt = new CartridgeDeactivatedEvent(ent);
            RaiseLocalEvent(active, ref deactEvt);
            var loaderDeactEvt = new CartridgeLoaderProgramDeactivatedEvent(active);
            RaiseLocalEvent(ent, ref loaderDeactEvt);
        }

        var evt = new CartridgeActivatedEvent(ent);
        RaiseLocalEvent(program, ref evt);

        var loaderEvt = new CartridgeLoaderProgramActivatedEvent(program);
        RaiseLocalEvent(ent, ref loaderEvt);

        UpdateUiState(ent.AsNullable());
    }

    public void DeactivateProgram(Entity<CartridgeLoaderComponent> ent, EntityUid program)
    {
        if (!HasProgram(ent, program) || ent.Comp.ActiveProgram != program)
            return;

        var evt = new CartridgeDeactivatedEvent(ent);
        RaiseLocalEvent(program, ref evt);
        ent.Comp.ActiveProgram = null;
        Dirty(ent);

        var loaderEvt = new CartridgeLoaderProgramDeactivatedEvent(program);
        RaiseLocalEvent(ent, ref loaderEvt);

        UpdateUiState(ent.AsNullable());
    }

    /// <summary>
    /// Installs a program by its prototype.
    /// </summary>
    /// <param name="ent">The cartridge loader.</param>
    /// <param name="prototype">The prototype.</param>
    /// <param name="deinstallable">Whether the program can be deinstalled or not.</param>
    /// <returns>Whether installing the cartridge was successful.</returns>
    public bool InstallProgram(Entity<CartridgeLoaderComponent> ent, EntProtoId prototype, bool deinstallable = true)
    {
        if (UsedDiskSpace(ent) >= ent.Comp.DiskSpace)
            return false;

        var ev = new ProgramInstallationAttempt(ent, prototype);
        RaiseLocalEvent(ref ev);

        if (ev.Cancelled)
            return false;

        return PredictedTrySpawnInContainer(prototype,
            ent,
            deinstallable ? CartridgeLoaderComponent.RemovableContainerId : CartridgeLoaderComponent.UnremovableContainerId,
            out _);
    }

    /// <summary>
    /// Uninstalls a program using its uid.
    /// </summary>
    /// <param name="ent">The cartridge loader uid.</param>
    /// <param name="program">The uid of the program to be uninstalled.</param>
    /// <returns>Whether uninstalling the program was successful.</returns>
    public bool UninstallProgram(Entity<CartridgeLoaderComponent> ent, EntityUid program)
    {
        if (!GetDiskPrograms(ent).Contains(program))
            return false;

        if (ent.Comp.ActiveProgram == program)
            DeactivateProgram(ent, program);

        var ev = new CartridgeLoaderProgramUninstalledEvent(program);
        RaiseLocalEvent(ent, ref ev);

        QueueDel(program);

        return true;
    }

    /// <summary>
    /// Installs a cartridge by spawning an invisible version of the cartridges prototype into the cartridge loaders program container.
    /// </summary>
    /// <param name="ent">The cartridge loader.</param>
    /// <param name="cartridge">The uid of the cartridge to be installed.</param>
    /// <returns>Whether installing the cartridge was successful.</returns>
    public bool InstallCartridge(Entity<CartridgeLoaderComponent> ent, EntityUid cartridge)
    {
        if (!HasComp<CartridgeComponent>(cartridge))
            return false;

        if (MetaData(cartridge).EntityPrototype is not { } cartridgeProto)
            return false;

        foreach (var program in GetDiskPrograms(ent))
        {
            if (MetaData(program).EntityPrototype is { } programProto && programProto == cartridgeProto)
                return false;
        }

        return InstallProgram(ent, cartridgeProto);
    }

    private void UpdateCartridgeInstallationStatus(Entity<CartridgeComponent> ent, InstallationStatus installationStatus)
    {
        ent.Comp.InstallationStatus = installationStatus;
        Dirty(ent);
    }
}

[ByRefEvent]
public readonly record struct CartridgeLoaderProgramActivatedEvent(EntityUid Program);

[ByRefEvent]
public readonly record struct CartridgeLoaderProgramDeactivatedEvent(EntityUid Program);

[ByRefEvent]
public readonly record struct CartridgeLoaderProgramUninstalledEvent(EntityUid Program);
