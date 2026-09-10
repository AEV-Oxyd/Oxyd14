using System.Diagnostics.CodeAnalysis;
using Content.Server.Materials;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Cloning;
using Robust.Shared.Containers;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// P2.12: the reader/cloner glue the Resurrection litany drives. The reader's item slot holds the
/// cruciform whose soul (P2.11) is read back out, and the cloner pays its biomass up front before
/// upstream <see cref="CloningPodSystem"/> grows the body.
/// </summary>
public sealed class CruciformReaderSystem : EntitySystem
{
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CruciformReaderComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<CruciformReaderComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnInserted(EntityUid uid, CruciformReaderComponent reader, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != reader.SlotId)
            return;

        reader.ReaderImplant = args.Entity;
        Dirty(uid, reader);
    }

    private void OnRemoved(EntityUid uid, CruciformReaderComponent reader, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != reader.SlotId)
            return;

        reader.ReaderImplant = null;
        Dirty(uid, reader);
    }

    /// <summary>
    /// Reads the soul out of the cruciform sitting in the reader's slot.
    /// </summary>
    public bool TryReadSoul(EntityUid uid,
        [NotNullWhen(true)] out CruciformSoulComponent? soul,
        CruciformReaderComponent? reader = null)
    {
        soul = null;

        if (!Resolve(uid, ref reader) || reader.ReaderImplant is not { } implant)
            return false;

        return TryComp<CruciformSoulComponent>(implant, out soul) && soul.HasSnapshot;
    }

    /// <summary>
    /// Pays <paramref name="amount"/> of the pod's biomass. Refuses without spending a unit when the
    /// container cannot cover it, so a resurrection never starts on an empty pod.
    /// </summary>
    public bool TrySpendBiomass(EntityUid cloner, int amount, CloningPodComponent? pod = null)
    {
        if (!Resolve(cloner, ref pod) || amount <= 0)
            return false;

        if (_materialStorage.GetMaterialAmount(cloner, pod.RequiredMaterial) < amount)
            return false;

        return _materialStorage.TryChangeMaterialAmount(cloner, pod.RequiredMaterial, -amount);
    }
}
