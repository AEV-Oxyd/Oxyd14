using System.Diagnostics.CodeAnalysis;
using Content.Server.Cloning;
using Content.Server.Cloning.Components;
using Content.Shared.Body;
using Content.Shared.Ghost.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Server.Player;
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
public sealed partial class CruciformReaderSystem : EntitySystem
{
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly CloningPodSystem _cloningPod = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NeoTheologyMachineSystem _machines = default!;
    [Dependency] private readonly SharedVisualBodySystem _visualBody = default!;
    [Dependency] private readonly HumanoidProfileSystem _profile = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    [SubscribeLocalEvent]
    private void OnInserted(EntityUid uid, CruciformReaderComponent reader, EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != reader.SlotId)
            return;

        reader.ReaderImplant = args.Entity;
        Dirty(uid, reader);
    }

    [SubscribeLocalEvent]
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

        if (!Resolve(uid, ref reader) || !_machines.IsOperational(uid) || reader.ReaderImplant is not { } implant)
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

    /// <summary>Check the saved soul and the pod without changing either machine.</summary>
    public bool CanResurrect(EntityUid cloner, EntityUid reader)
    {
        if (!_machines.IsOperational(cloner) ||
            !TryReadSoul(reader, out var soul) || soul.Profile == null || soul.BiomassCost <= 0 ||
            soul.MindId is not { } mindId || !TryComp<MindComponent>(mindId, out var mind) ||
            mind.UserId is not { } user || !_players.TryGetSessionById(user, out _) ||
            !TryComp<CloningPodComponent>(cloner, out var pod) ||
            HasComp<ActiveCloningPodComponent>(cloner) || pod.BodyContainer.ContainedEntity != null ||
            !ProtoMan.HasIndex<SpeciesPrototype>(soul.Profile.Species) ||
            _materialStorage.GetMaterialAmount(cloner, pod.RequiredMaterial) < soul.BiomassCost)
            return false;

        if (mind.OwnedEntity is { } body && !TerminatingOrDeleted(body) &&
            !HasComp<GhostComponent>(body) && !_mobState.IsDead(body))
            return false;

        return !_cloningPod.ClonesWaitingForMind.TryGetValue(mind, out var clone) ||
               TerminatingOrDeleted(clone) || _mobState.IsDead(clone);
    }

    [SubscribeLocalEvent]
    private void OnLitanyResurrection(Entity<CruciformClonerComponent> ent, ref LitanyResurrectionEvent args)
    {
        if (!CanResurrect(ent.Owner, args.Reader))
            return;

        args.Handled = true;
        if (args.ValidateOnly)
            return;

        TryReadSoul(args.Reader, out var soul);
        var profile = soul!.Profile!;
        var mindId = soul.MindId!.Value;
        var mind = Comp<MindComponent>(mindId);
        var pod = Comp<CloningPodComponent>(ent.Owner);
        var body = Spawn(ProtoMan.Index(profile.Species).Prototype, Transform(ent.Owner).Coordinates);
        _visualBody.ApplyProfileTo(body, profile);
        _profile.ApplyProfileTo(body, profile);
        _metadata.SetEntityName(body, soul.Name);

        if (!_containers.Insert(body, pod.BodyContainer))
        {
            QueueDel(body);
            args.Handled = false;
            return;
        }

        TrySpendBiomass(ent.Owner, soul.BiomassCost, pod);
        var beingCloned = AddComp<BeingClonedComponent>(body);
        beingCloned.Mind = mind;
        beingCloned.Parent = ent.Owner;
        pod.UsedBiomass = soul.BiomassCost;
        pod.CloningProgress = 0;
        _cloningPod.ClonesWaitingForMind[mind] = body;
        AddComp<ActiveCloningPodComponent>(ent.Owner);
        _cloningPod.TransferMindToClone(mindId, mind);
        _cloningPod.UpdateStatus(ent.Owner, CloningPodStatus.Cloning, pod);
    }
}
