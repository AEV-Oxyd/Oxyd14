using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Server.Cargo.Components;
using Content.Shared._Oxyd.Economy;
using Content.Shared._Oxyd.Skills;
using Content.Shared.Cargo.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Oxyd.Economy;

/// <summary>Eris artist_bench — materials, insight, oddity inspiration, artist perk.</summary>
public sealed partial class OxydArtistBenchSystem : EntitySystem
{
    private static readonly ProtoId<SkillPrototype> SkillRob = "Rob";
    private static readonly ProtoId<SkillPrototype> SkillVig = "Vig";
    private static readonly ProtoId<SkillPrototype> SkillTgh = "Tgh";
    private static readonly ProtoId<SkillPrototype> SkillCog = "Cog";
    private static readonly ProtoId<SkillPrototype> SkillMec = "Mec";
    private static readonly ProtoId<SkillPrototype> SkillBio = "Bio";

    private static readonly ProtoId<SkillPrototype>[] AllSkills =
    [
        SkillRob, SkillVig, SkillTgh, SkillCog, SkillMec, SkillBio,
    ];

    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SanitySystem _sanity = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materials = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedSkillSystem _skills = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OxydArtistBenchComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<OxydArtistBenchComponent, OxydArtistBenchCreateMessage>(OnCreate);
        SubscribeLocalEvent<OxydArtistBenchComponent, MaterialAmountChangedEvent>(OnMaterialsChanged);
    }

    private void OnOpened(Entity<OxydArtistBenchComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent, args.Actor);
    }

    private void OnMaterialsChanged(Entity<OxydArtistBenchComponent> ent, ref MaterialAmountChangedEvent args)
    {
        foreach (var actor in _ui.GetActors(ent.Owner, OxydArtistBenchUiKey.Key))
            PushState(ent, actor);
    }

    private void OnCreate(Entity<OxydArtistBenchComponent> ent, ref OxydArtistBenchCreateMessage args)
    {
        if (!TryCreateArtwork(ent, args.Actor, args.InsightUsed, out var err, out var price, out var used, out var kind))
        {
            PushState(ent, args.Actor, error: err);
            return;
        }

        _popup.PopupEntity($"Created {kind} worth {price}cr.", ent, args.Actor);
        PushState(ent, args.Actor, status: $"Spawned {kind} {price}cr (−{used:0} insight)");
    }

    /// <summary>Spend materials + Insight and spawn Guild-tagged artwork. Returns false on failure.</summary>
    public bool TryCreateArtwork(
        Entity<OxydArtistBenchComponent> bench,
        EntityUid artist,
        float requestedInsight,
        out string? error,
        out int price,
        out float used,
        out string kind)
    {
        error = null;
        price = 0;
        used = 0f;
        kind = string.Empty;

        if (!TryComp<SanityComponent>(artist, out var sanity))
        {
            error = "No insight";
            return false;
        }

        if (!HasEnoughMaterials(bench, out error))
            return false;

        var isArtist = HasComp<OxydArtistPerkComponent>(artist);

        // Eris: artists with insight > min choose spend; everyone else dumps all insight.
        if (isArtist && sanity.Insight > bench.Comp.MinInsight)
        {
            used = Math.Clamp(
                requestedInsight,
                bench.Comp.MinInsight,
                Math.Min(bench.Comp.MaxInsightPerWork, sanity.Insight));
        }
        else
        {
            used = sanity.Insight;
        }

        if (used < bench.Comp.MinInsight)
        {
            error = $"Need at least {bench.Comp.MinInsight:0} insight";
            return false;
        }

        if (!TryConsumeMaterials(bench, out error))
            return false;

        _sanity.GiveInsight((artist, sanity), -used);

        kind = ChooseArtKind(bench, artist, used, isArtist);
        var proto = KindToProto(bench.Comp, kind);
        var art = Spawn(proto, Transform(bench).Coordinates);
        price = bench.Comp.BasePrice + (int)MathF.Floor(used * bench.Comp.PricePerInsight);

        var staticPrice = EnsureComp<StaticPriceComponent>(art);
        staticPrice.Price = price;

        var overrideSell = EnsureComp<OverrideSellComponent>(art);
        overrideSell.OverrideAccount = OxydEconomyConstants.EmployerGuild;

        var artwork = EnsureComp<OxydArtworkComponent>(art);
        artwork.InsightInvested = used;
        artwork.ArtistName = MetaData(artist).EntityName;
        artwork.ArtKind = kind;
        Dirty(art, artwork);

        if (kind == "oddity")
            PopulateArtworkOddity(art, used);

        _meta.SetEntityName(art, $"{artwork.ArtistName}'s {kind}");
        _meta.SetEntityDescription(art, $"Insight {used:0} · {price}cr · Guild consignment");

        if (!isArtist)
            SacrificeSkills(artist);

        return true;
    }

    /// <summary>Legacy overload used by older call sites / tests.</summary>
    public bool TryCreateArtwork(
        Entity<OxydArtistBenchComponent> bench,
        EntityUid artist,
        float requestedInsight,
        out string? error,
        out int price,
        out float used)
    {
        return TryCreateArtwork(bench, artist, requestedInsight, out error, out price, out used, out _);
    }

    private bool HasEnoughMaterials(Entity<OxydArtistBenchComponent> bench, out string? error)
    {
        error = null;
        foreach (var mat in bench.Comp.SuitableMaterials)
        {
            var amount = _materials.GetMaterialAmount(bench.Owner, mat);
            if (amount < bench.Comp.MinMaterial)
            {
                error = $"Need {bench.Comp.MinMaterial} {mat} (have {amount})";
                return false;
            }
        }

        return true;
    }

    private bool TryConsumeMaterials(Entity<OxydArtistBenchComponent> bench, out string? error)
    {
        error = null;
        // Eris randomize_materialas + consume: burn min_mat of each suitable material.
        foreach (var mat in bench.Comp.SuitableMaterials)
        {
            if (!_materials.TryChangeMaterialAmount(bench.Owner, mat, -bench.Comp.MinMaterial))
            {
                error = $"Failed to consume {mat}";
                return false;
            }
        }

        return true;
    }

    private string ChooseArtKind(Entity<OxydArtistBenchComponent> bench, EntityUid artist, float insUsed, bool isArtist)
    {
        var stats = new Dictionary<ProtoId<SkillPrototype>, int>();
        if (isArtist && TryGetInspirationStats(bench, out var insp))
            stats = insp;

        int Stat(ProtoId<SkillPrototype> id) => stats.GetValueOrDefault(id, 0);

        var weightStatue = 20f;
        var weightOddity = 1f + Math.Max(Stat(SkillCog), Stat(SkillBio)) * 2f;
        var weightToolmod = 2f + Stat(SkillMec) * 2f;
        var weightGunPart = 1f + Stat(SkillCog) + Stat(SkillMec);
        var weightArmorPart = 2f + Stat(SkillTgh) + Stat(SkillBio);
        // Eris also weights revolver/weapon; fold Vigilance/Robustness into gun/armor flavors.
        weightGunPart += 1f + Stat(SkillVig) * 2f;
        weightArmorPart += 1f + Math.Max(Stat(SkillRob), Stat(SkillTgh)) * 2f;

        if (insUsed >= 85f)
        {
            weightGunPart += 14f;
            weightArmorPart += 9f;
        }
        else if (insUsed >= 70f)
        {
            weightGunPart += 12f;
            weightOddity += 13f;
            weightArmorPart += 8f;
        }

        if (insUsed >= 55f)
        {
            weightToolmod += 12f;
            weightArmorPart += 4f;
        }
        else
        {
            weightStatue += 12f;
        }

        var weights = new Dictionary<string, float>
        {
            ["statue"] = Math.Max(weightStatue, 1f),
            ["oddity"] = Math.Max(weightOddity, 1f),
            ["toolmod"] = Math.Max(weightToolmod, 1f),
            ["gunPart"] = Math.Max(weightGunPart, 1f),
            ["armorPart"] = Math.Max(weightArmorPart, 1f),
        };

        return _random.Pick(weights);
    }

    private static EntProtoId KindToProto(OxydArtistBenchComponent comp, string kind) => kind switch
    {
        "oddity" => comp.OddityProto,
        "toolmod" => comp.ToolmodProto,
        "gunPart" => comp.GunPartProto,
        "armorPart" => comp.ArmorPartProto,
        _ => comp.StatueProto,
    };

    private bool TryGetInspirationStats(
        Entity<OxydArtistBenchComponent> bench,
        out Dictionary<ProtoId<SkillPrototype>, int> stats)
    {
        stats = new();
        var item = _slots.GetItemOrNull(bench.Owner, bench.Comp.OdditySlotId);
        if (item is not { } oddity || !TryComp<OddityComponent>(oddity, out var od))
            return false;

        foreach (var (skill, amount) in od.giving)
            stats[skill] = amount;
        return stats.Count > 0;
    }

    private void PopulateArtworkOddity(EntityUid art, float insUsed)
    {
        var oddity = EnsureComp<OddityComponent>(art);
        oddity.giving = new Dictionary<ProtoId<SkillPrototype>, int>();
        foreach (var skill in AllSkills)
            oddity.giving[skill] = _random.Next(0, 2);

        var statsAmt = 3;
        if (insUsed >= 85f)
            statsAmt += 3;
        if (insUsed >= 70f)
            statsAmt += 3;
        if (insUsed >= 55f)
            statsAmt += 3;

        for (var i = 0; i < statsAmt; i++)
        {
            var skill = _random.Pick(AllSkills);
            oddity.giving[skill] = Math.Min(12, oddity.giving.GetValueOrDefault(skill) + _random.Next(1, 3));
        }
    }

    private void SacrificeSkills(EntityUid artist)
    {
        if (!TryComp<MobSkillComponent>(artist, out var skills))
            return;

        var pool = _random.Next(4, 11);
        var change = new Dictionary<ProtoId<SkillPrototype>, int>();
        for (var i = 0; i < pool; i++)
        {
            var skill = _random.Pick(AllSkills);
            change[skill] = change.GetValueOrDefault(skill) - 1;
        }

        _skills.ModifySkills((artist, skills), change);
        _popup.PopupEntity("To create this work of art you have sacrificed a part of yourself.", artist, artist);
    }

    private void PushState(
        Entity<OxydArtistBenchComponent> ent,
        EntityUid user,
        string? error = null,
        string? status = null)
    {
        var insight = 0f;
        if (TryComp<SanityComponent>(user, out var sanity))
            insight = sanity.Insight;

        var isArtist = HasComp<OxydArtistPerkComponent>(user);
        string? oddityName = null;
        var oddityStats = new Dictionary<string, int>();
        var oddityUid = _slots.GetItemOrNull(ent.Owner, ent.Comp.OdditySlotId);
        if (oddityUid is { } odUid)
        {
            oddityName = MetaData(odUid).EntityName;
            if (TryComp<OddityComponent>(odUid, out var od))
            {
                foreach (var (skill, amount) in od.giving)
                    oddityStats[skill.Id] = amount;
            }
        }

        var materials = new Dictionary<string, int>();
        var enoughMats = true;
        foreach (var mat in ent.Comp.SuitableMaterials)
        {
            var amt = _materials.GetMaterialAmount(ent.Owner, mat);
            materials[mat.Id] = amt;
            if (amt < ent.Comp.MinMaterial)
                enoughMats = false;
        }

        var canCreate = insight >= ent.Comp.MinInsight && enoughMats;

        var state = new OxydArtistBenchUiState
        {
            Insight = insight,
            MinInsight = ent.Comp.MinInsight,
            MaxInsight = Math.Min(ent.Comp.MaxInsightPerWork, Math.Max(insight, ent.Comp.MinInsight)),
            IsArtist = isArtist,
            CanCreate = canCreate,
            OddityName = oddityName,
            OddityStats = oddityStats,
            Materials = materials,
            MinMaterial = ent.Comp.MinMaterial,
            Error = error,
            Status = status,
        };
        _ui.SetUiState(ent.Owner, OxydArtistBenchUiKey.Key, state);
    }
}
