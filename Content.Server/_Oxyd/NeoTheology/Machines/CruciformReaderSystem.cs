using System.Diagnostics.CodeAnalysis;
using Content.Server.Cloning;
using Content.Server.Materials;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Cloning;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
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
    [Dependency] private readonly CloningPodSystem _cloningPod = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CruciformReaderComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<CruciformReaderComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<CruciformClonerComponent, LitanyResurrectionEvent>(OnLitanyResurrection);
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

    /// <summary>
    /// Resurrection bridge (Eris <c>rituals/machinery.dm:13-42</c>): the litany hands the cloner
    /// a reader holding the stored soul, this reads it back out and lets upstream
    /// <see cref="CloningPodSystem"/> grow the body. The dead wearer and the pod's biomatter are
    /// upstream's own gates — TryCloning refuses a live mind and charges its cloningCost — so the
    /// litany does not pre-charge and <see cref="TrySpendBiomass"/> stays unused on this path.
    /// The stored mind moves as soon as the pod accepts the job (Eris <c>transfer_soul</c>
    /// semantics) instead of waiting on upstream's accept dialog, which becomes a no-op.
    /// </summary>
    private void OnLitanyResurrection(Entity<CruciformClonerComponent> ent, ref LitanyResurrectionEvent args)
    {
        if (!TryReadSoul(args.Reader, out var soul) ||
            soul.MindId is not { } mindId ||
            !TryComp<MindComponent>(mindId, out var mind) ||
            mind.OwnedEntity is not { } body ||
            !_mobState.IsDead(body) ||
            !TryComp<CloningPodComponent>(ent.Owner, out var pod))
        {
            return;
        }

        if (!_cloningPod.TryCloning(ent.Owner, body, (mindId, mind), pod))
            return;

        _cloningPod.TransferMindToClone(mindId, mind);
        args.Handled = true;
    }
}
