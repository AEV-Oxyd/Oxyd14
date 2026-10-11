using Content.Shared._Oxyd.Medical;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Medical;

/// <summary>
/// Ports Eris machinery/bioprinter.dm: meat is processed into stored biomass,
/// a syringe/injector carrying blood registers a donor sample, and organs are
/// printed one at a time for a fixed biomass cost. The prosthetics variant is
/// not ported (Eris robotic organs use the wound system's MODIFICATION_SILICON).
/// </summary>
public sealed partial class OxydBioprinterSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    /// <summary>Eris BIOMASS_TYPES: 50 units of biomass per meat item.</summary>
    private const int BiomassPerMeat = 50;

    /// <summary>Brief working animation window while a print finishes.</summary>
    private static readonly TimeSpan PrintTime = TimeSpan.FromSeconds(3);

    /// <summary>Eris products table (organ name -> organ prototype, biomass cost).
    /// SS14 has a single kidneys proto covering Eris' left/right pair.</summary>
    private static readonly (string Id, string Proto, int Cost)[] Products =
    {
        ("heart", "OrganHumanHeart", 50),
        ("lungs", "OrganHumanLungs", 40),
        ("kidney", "OrganHumanKidneys", 20),
        ("eyes", "OrganHumanEyes", 30),
        ("liver", "OrganHumanLiver", 50),
        ("stomach", "OrganHumanStomach", 40),
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydBioprinterComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OxydBioprinterComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<OxydBioprinterComponent, OxydBioprinterPrintMessage>(OnPrint);
    }

    /// <summary>Eris attackby: syringe of blood registers donor data; meat becomes biomass.</summary>
    private void OnInteractUsing(EntityUid uid, OxydBioprinterComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Blood sample (Eris: syringe containing organic blood -> loaded_dna).
        if (_solutions.TryGetSolution(args.Used, "injector", out var injEnt, out var injSol) &&
            injSol.GetTotalPrototypeQuantity("Blood") > 0)
        {
            args.Handled = true;
            comp.HasBloodSample = true;
            Dirty(uid, comp);
            _popup.PopupEntity(Loc.GetString("oxyd-bioprinter-sample-loaded"), uid, args.User);
            PushState(uid, comp);
            return;
        }

        // Biomass (Eris BIOMASS_TYPES: any meat -> stored_matter).
        if (_tag.HasTag(args.Used, "Meat"))
        {
            if (comp.StoredMatter >= comp.MaxMatter)
            {
                _popup.PopupEntity(Loc.GetString("oxyd-bioprinter-full", ("max", comp.MaxMatter)), uid, args.User);
                args.Handled = true;
                return;
            }

            args.Handled = true;
            comp.StoredMatter = Math.Min(comp.MaxMatter, comp.StoredMatter + BiomassPerMeat);
            Dirty(uid, comp);
            _popup.PopupEntity(
                Loc.GetString("oxyd-bioprinter-biomass", ("matter", comp.StoredMatter)),
                uid, args.User);
            Del(args.Used);
            PushState(uid, comp);
        }
    }

    private void OnUiOpen(EntityUid uid, OxydBioprinterComponent comp, AfterActivatableUIOpenEvent args)
    {
        PushState(uid, comp);
    }

    private void OnPrint(EntityUid uid, OxydBioprinterComponent comp, OxydBioprinterPrintMessage args)
    {
        if (comp.Working)
            return;

        var product = Array.Find(Products, p => p.Id == args.Product);
        if (product.Proto == null)
            return;

        if (comp.StoredMatter < product.Cost)
        {
            // At the actor, not the machine: a world popup on the printer sits
            // under the open window where nobody sees it.
            _popup.PopupEntity(Loc.GetString("oxyd-bioprinter-no-matter"),
                args.Actor, args.Actor, PopupType.Small);
            return;
        }

        comp.StoredMatter -= product.Cost;
        comp.Working = true;
        comp.NextFinish = _timing.CurTime + PrintTime;
        Dirty(uid, comp);
        _appearance.SetData(uid, OxydMachineVisuals.Working, true);

        var printed = Spawn(product.Proto, Transform(uid).Coordinates);
        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.Actor):user} printed {ToPrettyString(printed)} from {ToPrettyString(uid)} for {product.Cost} biomass");
        _popup.PopupEntity(Loc.GetString("oxyd-bioprinter-printed"), uid, PopupType.Small);
        PushState(uid, comp);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<OxydBioprinterComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Working || now < comp.NextFinish)
                continue;
            comp.Working = false;
            Dirty(uid, comp);
            _appearance.SetData(uid, OxydMachineVisuals.Working, false);
            PushState(uid, comp);
        }
    }

    private void PushState(EntityUid uid, OxydBioprinterComponent comp)
    {
        var state = new OxydBioprinterState
        {
            StoredMatter = comp.StoredMatter,
            MaxMatter = comp.MaxMatter,
            HasBloodSample = comp.HasBloodSample,
            Working = comp.Working,
        };
        foreach (var (id, _, cost) in Products)
        {
            state.Products.Add(new OxydBioprinterProduct
            {
                Id = id,
                Name = Loc.GetString($"oxyd-bioprinter-organ-{id}"),
                Cost = cost,
                Affordable = comp.StoredMatter >= cost && !comp.Working,
            });
        }
        _ui.SetUiState(uid, OxydBioprinterUiKey.Key, state);
    }
}
