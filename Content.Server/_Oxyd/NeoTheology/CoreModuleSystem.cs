using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Installs and removes <see cref="CoreModulePrototype"/>s on a cruciform and raises
/// the lifecycle events. All derived state is recomputed by <see cref="CruciformSystem"/>
/// so there is exactly one writer for sets, capacity and regeneration.
/// </summary>
public sealed partial class CoreModuleSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public bool TryInstall(EntityUid cruciform, CruciformComponent comp, ProtoId<CoreModulePrototype> module)
    {
        if (!_proto.HasIndex(module))
            return false;
        if (!comp.InstalledModules.Add(module))
            return false;

        _cruciform.RecomputeProfile(cruciform, comp);
        var installed = new CoreModuleInstalledEvent(module);
        RaiseLocalEvent(cruciform, ref installed);
        return true;
    }

    public bool TryRemove(EntityUid cruciform, CruciformComponent comp, ProtoId<CoreModulePrototype> module)
    {
        if (!comp.InstalledModules.Remove(module))
            return false;

        _cruciform.RecomputeProfile(cruciform, comp);
        var uninstalled = new CoreModuleUninstalledEvent(module);
        RaiseLocalEvent(cruciform, ref uninstalled);
        return true;
    }

    public bool HasModule(CruciformComponent comp, ProtoId<CoreModulePrototype> module)
        => comp.InstalledModules.Contains(module);
}
