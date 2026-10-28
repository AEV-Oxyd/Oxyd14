using System.Linq;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.Medical;

/// <summary>MIRC — Moebius Internal Reagent Catalogue (Eris chem_catalog.dm):
/// a PDA program listing Moebius medicine reagents and their synthesis recipes.</summary>
[RegisterComponent]
public sealed partial class OxydMircCartridgeComponent : Component;

public sealed partial class OxydMircCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydMircCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    private void OnUiReady(EntityUid uid, OxydMircCartridgeComponent comp, CartridgeUiReadyEvent args)
    {
        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, new OxydMircUiState { Entries = BuildEntries() });
    }

    private List<OxydMircEntry> BuildEntries()
    {
        var byProduct = new Dictionary<string, List<ReactionPrototype>>();
        var usedIn = new Dictionary<string, List<ReactionPrototype>>();
        foreach (var reaction in _prototypes.EnumeratePrototypes<ReactionPrototype>())
        {
            foreach (var product in reaction.Products.Keys)
            {
                if (!byProduct.TryGetValue(product.Id, out var list))
                    byProduct[product.Id] = list = new List<ReactionPrototype>();
                list.Add(reaction);
            }
            foreach (var reactant in reaction.Reactants.Keys)
            {
                if (!usedIn.TryGetValue(reactant.Id, out var list))
                    usedIn[reactant.Id] = list = new List<ReactionPrototype>();
                list.Add(reaction);
            }
        }

        var entries = new List<OxydMircEntry>();
        foreach (var reagent in _prototypes.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagent.Abstract || !reagent.ID.StartsWith("OxydMed"))
                continue;

            var entry = new OxydMircEntry
            {
                Id = reagent.ID,
                Name = reagent.LocalizedName,
                Description = reagent.LocalizedDescription,
                Type = reagent.Group,
                Phase = PhaseAtStp(reagent),
                ColorHex = reagent.SubstanceColor.ToHex(),
                Taste = reagent.Flavor is { } flavor
                    ? Loc.GetString(_prototypes.Index(flavor).FlavorDescription)
                    : string.Empty,
            };

            // Eris spec block: metabolism rate, NSA contribution, addiction data —
            // read off the Bloodstream metabolism stage like Eris reads the reagent datum.
            if (reagent.Metabolisms?.Metabolisms.TryGetValue("Bloodstream", out var bloodEntry) == true)
            {
                entry.Metabolism = bloodEntry.MetabolismRate.Float();
                foreach (var effect in bloodEntry.Effects)
                {
                    if (effect is Nsa nsa)
                        entry.Nsa = nsa.Value;
                    else if (effect is Addictive addictive)
                    {
                        entry.AddictionThreshold = addictive.Threshold.Float();
                        entry.AddictionChance = addictive.AddictionChance;
                    }
                }
            }

            if (byProduct.TryGetValue(reagent.ID, out var reactions))
            {
                foreach (var reaction in reactions.OrderBy(r => r.ID))
                {
                    var reactants = reaction.Reactants
                        .Select(kv => $"{_prototypes.Index(kv.Key).LocalizedName} ({kv.Value.Amount})");
                    var products = reaction.Products
                        .Select(kv => $"{_prototypes.Index(kv.Key).LocalizedName} ({kv.Value})");
                    entry.Recipes.Add($"{string.Join(" + ", reactants)} = {string.Join(", ", products)}");
                }
            }

            // Eris "Takes part in reactions": links to the catalog entry of each product
            // of every reaction this reagent feeds.
            if (usedIn.TryGetValue(reagent.ID, out var usedInReactions))
            {
                foreach (var reaction in usedInReactions.OrderBy(r => r.ID))
                {
                    foreach (var product in reaction.Products.Keys)
                    {
                        var productName = _prototypes.Index(product).LocalizedName;
                        entry.UsedIn.Add(new OxydMircLink
                        {
                            Label = productName,
                            EntryId = product.Id.StartsWith("OxydMed") ? product.Id : null,
                        });
                    }
                }
            }

            entries.Add(entry);
        }
        return entries.OrderBy(e => e.Name).ToList();
    }

    /// <summary>Eris catalog "Phase" — derived from the reagent's melting/boiling points
    /// relative to room temperature (~20C).</summary>
    private static string PhaseAtStp(ReagentPrototype reagent)
    {
        const float stp = 293.15f;
        if (reagent.MeltingPoint is { } mp && mp > stp)
            return "Solid";
        if (reagent.BoilingPoint is { } bp && bp < stp)
            return "Gas";
        return "Liquid";
    }
}
