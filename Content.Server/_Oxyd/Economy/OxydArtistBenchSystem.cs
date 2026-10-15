using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.Cargo.Components;
using Content.Shared._Oxyd.Economy;
using Content.Shared.Cargo.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._Oxyd.Economy;

/// <summary>Eris artist_bench — spend Insight, spawn Guild-tagged priced art.</summary>
public sealed partial class OxydArtistBenchSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SanitySystem _sanity = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydArtistBenchComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<OxydArtistBenchComponent, OxydArtistBenchCreateMessage>(OnCreate);
    }

    private void OnOpened(Entity<OxydArtistBenchComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent, args.Actor);
    }

    private void OnCreate(Entity<OxydArtistBenchComponent> ent, ref OxydArtistBenchCreateMessage args)
    {
        if (!TryCreateArtwork(ent, args.Actor, args.InsightUsed, out var err, out var price, out var used))
        {
            PushState(ent, args.Actor, error: err);
            return;
        }

        _popup.PopupEntity($"Created art worth {price}cr.", ent, args.Actor);
        PushState(ent, args.Actor, status: $"Spawned {price}cr artwork (−{used:0} insight)");
    }

    /// <summary>Spend Insight and spawn Guild-tagged artwork. Returns false on failure.</summary>
    public bool TryCreateArtwork(
        Entity<OxydArtistBenchComponent> bench,
        EntityUid artist,
        float requestedInsight,
        out string? error,
        out int price,
        out float used)
    {
        error = null;
        price = 0;
        used = 0f;

        if (!TryComp<SanityComponent>(artist, out var sanity))
        {
            error = "No insight";
            return false;
        }

        used = Math.Clamp(requestedInsight, bench.Comp.MinInsight, Math.Min(bench.Comp.MaxInsightPerWork, sanity.Insight));
        if (sanity.Insight < bench.Comp.MinInsight || used < bench.Comp.MinInsight)
        {
            error = $"Need at least {bench.Comp.MinInsight:0} insight";
            return false;
        }

        _sanity.GiveInsight((artist, sanity), -used);

        var proto = used >= 70f && _random.Prob(0.45f)
            ? bench.Comp.OddityProto
            : bench.Comp.StatueProto;

        var art = Spawn(proto, Transform(bench).Coordinates);
        price = bench.Comp.BasePrice + (int)MathF.Floor(used * bench.Comp.PricePerInsight);

        var staticPrice = EnsureComp<StaticPriceComponent>(art);
        staticPrice.Price = price;

        var overrideSell = EnsureComp<OverrideSellComponent>(art);
        overrideSell.OverrideAccount = OxydEconomyConstants.EmployerGuild;

        var artwork = EnsureComp<OxydArtworkComponent>(art);
        artwork.InsightInvested = used;
        artwork.ArtistName = MetaData(artist).EntityName;
        Dirty(art, artwork);

        _meta.SetEntityName(art, $"{artwork.ArtistName}'s artwork");
        _meta.SetEntityDescription(art, $"Insight {used:0} · {price}cr · Guild consignment");
        return true;
    }

    private void PushState(Entity<OxydArtistBenchComponent> ent, EntityUid user, string? error = null, string? status = null)
    {
        var insight = 0f;
        if (TryComp<SanityComponent>(user, out var sanity))
            insight = sanity.Insight;

        var state = new OxydArtistBenchUiState
        {
            Insight = insight,
            MinInsight = ent.Comp.MinInsight,
            MaxInsight = Math.Min(ent.Comp.MaxInsightPerWork, Math.Max(insight, ent.Comp.MinInsight)),
            CanCreate = insight >= ent.Comp.MinInsight,
            Error = error,
            Status = status,
        };
        _ui.SetUiState(ent.Owner, OxydArtistBenchUiKey.Key, state);
    }
}
