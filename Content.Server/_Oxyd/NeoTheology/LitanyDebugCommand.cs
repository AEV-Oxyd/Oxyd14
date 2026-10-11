using System.Linq;
using System.Numerics;
using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Server.Cloning.Components;
using Content.Server._Oxyd.NeoTheology.Machines;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared.Administration;
using Robust.Server.GameObjects;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Body.Components;
using Content.Server._Oxyd.SanityInsightAndResting;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.Cloning;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.UserInterface;
using Content.Shared.Materials;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Content.Shared.Standing;
using Content.Shared._Oxyd.NeoTheology;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Syntax;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Debug commands for exercising NeoTheology litany scenarios that the generic
/// admin verbs cannot reach: handing an oddity to a mob, buckling a mob to an
/// altar, granting litany sets, placing machines on the faced tile, and the
/// composite setups the playtest matrix needs. Dev/test only.
/// </summary>
[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class LitanyCommand : ToolshedCommand
{
    private static readonly EntProtoId OddityProto = "OxydNtOddity";
    private static readonly EntProtoId AltarProto = "OxydNtAltar";
    private static readonly EntProtoId FoodProto = "FoodSpacemansTrumpet";

    private CruciformSystem? _cruciform;
    private LitanySystem? _litany;
    private SharedHandsSystem? _hands;
    private SharedBuckleSystem? _buckle;
    private InventorySystem? _inventory;
    private CoreModuleSystem? _modules;
    private SharedSubdermalImplantSystem? _implants;
    private ItemSlotsSystem? _slots;
    private ChatSystem? _chat;
    private SharedContainerSystem? _containers;
    private DamageableSystem? _damageable;

    private SharedTransformSystem? _xform;
    private EntityLookupSystem? _lookup;

    private EntityUid? Self(IInvocationContext ctx)
        => ExecutingEntity(ctx);

    [CommandImplementation("giveoddity")]
    public EntityUid GiveOddity(IInvocationContext ctx)
    {
        _hands ??= GetSys<SharedHandsSystem>();
        var uid = Self(ctx) ?? throw new InvalidOperationException("no executing entity");
        var item = Spawn(OddityProto, Transform(uid).Coordinates);
        if (!_hands.TryPickupAnyHand(uid, item, checkActionBlocker: false))
            ctx.WriteLine($"litany: could not place {item} in a hand (all hands full?)");
        return item;
    }

    /// <summary>Force-unequip every clothing item on the target (Commitment needs a naked body).</summary>
    [CommandImplementation("undress")]
    public void UndressSelf(IInvocationContext ctx)
        => Undress(Self(ctx) ?? throw new InvalidOperationException("no executing entity"));

    [CommandImplementation("undress")]
    public void UndressPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        foreach (var mob in mobs)
            Undress(mob);
    }

    private void Undress(EntityUid mob)
    {
        _inventory ??= GetSys<InventorySystem>();
        if (!TryComp<InventoryComponent>(mob, out _))
            return;

        var slots = _inventory.GetSlotEnumerator(mob, SlotFlags.All);
        var names = new List<string>();
        while (slots.NextItem(out _, out var slot))
        {
            if (slot is { } def)
                names.Add(def.Name);
        }

        foreach (var name in names)
            _inventory.TryUnequip(mob, mob, name, force: true);
    }

    /// <summary>Buckle the mob to the nearest NeoTheology altar (spawns one under the mob if none in 5 m).</summary>
    [CommandImplementation("buckle")]
    public EntityUid? BuckleSelf(IInvocationContext ctx)
        => Buckle(Self(ctx) ?? throw new InvalidOperationException("no executing entity"));

    [CommandImplementation("buckle")]
    public void BucklePiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        foreach (var mob in mobs)
            Buckle(mob);
    }

    private EntityUid? Buckle(EntityUid mob)
    {
        _buckle ??= GetSys<SharedBuckleSystem>();
        _lookup ??= GetSys<EntityLookupSystem>();

        EntityUid altar = EntityUid.Invalid;
        var coords = Transform(mob).Coordinates;
        foreach (var seat in _lookup.GetEntitiesInRange<StrapComponent>(coords, 5f))
        {
            if (!HasComp<NeoTheologyAltarComponent>(seat))
                continue;
            // An occupied seat fails TryBuckle (StrapHasSpace) — keep scanning
            // for a free altar, and only spawn a fresh one when none took it.
            if (_buckle.TryBuckle(mob, mob, seat))
            {
                altar = seat;
                break;
            }
        }

        if (!altar.IsValid())
        {
            altar = Spawn(AltarProto, coords);
            if (!_buckle.TryBuckle(mob, mob, altar))
                return null;
        }

        // The altar's Strap is already position:Down, which puts the mob down on buckle.
        // (Strap.Enabled must stay true — TryGetProcedureAltar rejects a disabled strap.)
        return altar;
    }


    /// <summary>Dump each piped mob's NeoTheology state: cruciform, activity, profile,
    /// clearance, holiness pool, installed modules, unlocked/granted litany sets, and
    /// any pending cast or ceremony.</summary>
    [CommandImplementation("status")]
    public void StatusPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        _cruciform ??= GetSys<CruciformSystem>();
        foreach (var mob in mobs)
        {
            if (TryComp<PlantTrayComponent>(mob, out var tray))
            {
                ctx.WriteLine($"{mob}: tray weeds={tray.WeedLevel}/{tray.MaxWeedLevel}");
                continue;
            }
            if (TryComp<EyeOfTheProtectorComponent>(mob, out var eyeComp))
            {
                ctx.WriteLine($"{mob}: eye observation={eyeComp.Observation:F1} armament={eyeComp.ArmamentsPoints}/{eyeComp.MaxArmamentsPoints}");
                continue;
            }
            if (!HasComp<MobStateComponent>(mob))
            {
                ctx.WriteLine($"{mob}: not a mob, skipped");
                continue;
            }

            var vitals = $"{mob}: state={Comp<MobStateComponent>(mob).CurrentState}";
            _damageable ??= GetSys<DamageableSystem>();
            if (HasComp<DamageableComponent>(mob))
                vitals += $" dmg={(TryComp<DamageableComponent>(mob, out var dmgComp) ? _damageable!.GetPositiveDamage((mob, dmgComp)).GetTotal() : FixedPoint2.Zero):F1}";
            if (TryComp<SanityComponent>(mob, out var sanity))
                vitals += $" sanity={sanity.Sanity:F1}/{sanity.MaxSanity:F0} insight={sanity.Insight:F1}";
            ctx.WriteLine(vitals);

            if (!_cruciform.TryGetCruciformEntity(mob, out var implant, out var comp))
            {
                ctx.WriteLine($"{mob}: no cruciform");
            }
            else
            {
                var modules = comp.InstalledModules.Count == 0
                    ? "-"
                    : string.Join(", ", comp.InstalledModules);
                var sets = comp.UnlockedSets.Count == 0
                    ? "-"
                    : string.Join(", ", comp.UnlockedSets);
                var granted = comp.GrantedSets.Count == 0
                    ? "-"
                    : string.Join(", ", comp.GrantedSets);
                ctx.WriteLine(
                    $"{mob}: cruciform={implant} active={comp.Active} everActivated={comp.EverActivated} " +
                    $"profile={comp.Profile} clearance={comp.Clearance} " +
                    $"holiness={comp.Holiness:F1}/{comp.MaxHoliness:F1} regen={comp.RegenerationPerSecond:F3}/s " +
                    $"modules=[{modules}] sets=[{sets}] granted=[{granted}] " +
                    $"upgrades={comp.CoreUpgrades.Count} upgrade={(comp.Upgrade?.ToString() ?? "-")}");
            }

            if (TryComp<LitanyPendingCastComponent>(mob, out var pending))
            {
                ctx.WriteLine(
                    $"{mob}: pending request={pending.Cast.RequestId} litany={pending.Cast.LitanyId} " +
                    $"stage={pending.Cast.Stage} cleared={pending.Cast.Cleared} committed={pending.Cast.Committed} " +
                    $"awaitChoice={pending.Cast.AwaitingChoice} targets={pending.Cast.Targets.Count} " +
                    $"chant={pending.Cast.ChantEndsAt} choiceExpiry={pending.Cast.ChoiceExpiresAt}");
            }

            if (TryComp<ActiveCeremonyComponent>(mob, out var ceremony))
            {
                ctx.WriteLine(
                    $"{mob}: ceremony={ceremony.Ritual} first={ceremony.First} " +
                    $"phrasesLeft={ceremony.Phrases.Count} participants={ceremony.Participants.Count} range={ceremony.Range}");
            }

            if (TryComp<CruciformBearerComponent>(mob, out var bearerComp))
            {
                var now = IoCManager.Resolve<IGameTiming>().CurTime;
                var cds = string.Join(", ", bearerComp.PersonalCooldowns.Select(
                    kv => $"{kv.Key}={Math.Max(0, (kv.Value - now).TotalSeconds):F0}s"));
                ctx.WriteLine($"{mob}: cds=[{cds}]");
            }
        }
    }



    /// <summary>Unbuckle each piped entity from whatever it is strapped to.</summary>
    [CommandImplementation("unbuckle")]
    public void UnbucklePiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        _buckle ??= GetSys<SharedBuckleSystem>();
        foreach (var mob in mobs)
        {
            if (!TryComp<BuckleComponent>(mob, out var buckle) || buckle.BuckledTo is not { } seat)
                continue;
            _buckle.TryUnbuckle((mob, buckle), mob, popup: false);
            ctx.WriteLine($"{mob}: unbuckled from {seat}");
        }
    }




    private bool Grant(string set, EntityUid uid)
    {
        _cruciform ??= GetSys<CruciformSystem>();
        if (!_cruciform.TryGetCruciformEntity(uid, out var implant, out var comp))
            return false;

        var setId = new ProtoId<LitanySetPrototype>(set);
        comp.UnlockedSets.Add(setId);
        comp.GrantedSets.Add(setId);
        _cruciform.RecomputeProfile(implant, comp);
        EntityManager.Dirty(implant, comp);
        return true;
    }

    /// <summary>Set holiness directly on the entity's cruciform.</summary>
    [CommandImplementation("holiness")]
    public bool HolinessSelf(IInvocationContext ctx, double value)
        => Holiness(value, Self(ctx) ?? throw new InvalidOperationException("no executing entity"));

    [CommandImplementation("holiness")]
    public void HolinessPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, double value)
    {
        foreach (var mob in mobs)
            ctx.WriteLine($"{mob}: {Holiness(value, mob)}");
    }

    private bool Holiness(double value, EntityUid uid)
    {
        _cruciform ??= GetSys<CruciformSystem>();
        if (!_cruciform.TryGetCruciformEntity(uid, out var implant, out var comp))
            return false;

        comp.Holiness = value;
        EntityManager.Dirty(implant, comp);
        return true;
    }

    /// <summary>Add miracle points to the first Eye of the Protector (default +5).</summary>
    [CommandImplementation("miracle")]
    public int Miracle(IInvocationContext ctx, int amount = 5)
    {
        var query = EntityManager.EntityQueryEnumerator<EyeOfTheProtectorComponent>();
        while (query.MoveNext(out var uid, out var eye))
        {
            eye.MiraclePoints += amount;
            EntityManager.Dirty(uid, eye);
            return eye.MiraclePoints;
        }

        return -1;
    }



    /// <summary>Cast a litany by id as the executing entity (skips speech parsing; still runs the chant do-after and full target/choice pipeline). Optional name for named-selectTarget litanies.</summary>
    [CommandImplementation("cast")]
    public void Cast(IInvocationContext ctx, string litany, string? name = null)
        => DoCast(ctx, Self(ctx) ?? throw new InvalidOperationException("no executing entity"), litany, name);

    /// <summary>Piped variant: cast as each piped mob. Drive casts from the server console without client focus.</summary>
    [CommandImplementation("cast")]
    public void CastPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, string litany, string? name = null)
    {
        foreach (var mob in mobs)
            DoCast(ctx, mob, litany, name);
    }

    private void DoCast(IInvocationContext ctx, EntityUid uid, string litany, string? name)
    {
        _litany ??= GetSys<LitanySystem>();
        var res = _litany.TryBeginLitany(uid, new ProtoId<LitanyPrototype>(litany), LitanyCastOrigin.ManualSpeech, spokenName: name);
        ctx.WriteLine($"{uid} cast {litany}: success={res.Success} reason={res.Reason} request={res.RequestId}");
    }

    /// <summary>Set the mob's facing direction in degrees (0 = east, 90 = north) for deterministic FrontMachine casts.</summary>
    [CommandImplementation("face")]
    public void FaceSelf(IInvocationContext ctx, double degrees)
    {
        _xform ??= GetSys<SharedTransformSystem>();
        var uid = Self(ctx) ?? throw new InvalidOperationException("no executing entity");
        _xform.SetLocalRotation(uid, Angle.FromDegrees(degrees));
    }

    [CommandImplementation("face")]
    public void FacePiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, double degrees)
    {
        _xform ??= GetSys<SharedTransformSystem>();
        foreach (var mob in mobs)
            _xform.SetLocalRotation(mob, Angle.FromDegrees(degrees));
    }

    /// <summary>Clear personal litany cooldowns on the entity and all global cooldowns.</summary>
    [CommandImplementation("cooldowns")]
    public void CooldownsSelf(IInvocationContext ctx) => Cooldowns(ctx, Self(ctx));

    [CommandImplementation("cooldowns")]
    public void CooldownsPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        foreach (var mob in mobs)
            Cooldowns(ctx, mob);
    }

    private void Cooldowns(IInvocationContext ctx, EntityUid? uid)
    {
        _litany ??= GetSys<LitanySystem>();
        if (uid is { } self && TryComp<CruciformBearerComponent>(self, out var bearer))
        {
            bearer.PersonalCooldowns.Clear();
            EntityManager.Dirty(self, bearer);
        }

        _litany.TestingClearCooldowns();
    }

    /// <summary>Grant a cruciform with a NeoTheology profile (e.g. OxydNtPreacher) to the entity.</summary>
    [CommandImplementation("cruciform")]
    public bool CruciformSelf(IInvocationContext ctx, string profile)
        => Cruciform(profile, Self(ctx) ?? throw new InvalidOperationException("no executing entity"));

    [CommandImplementation("cruciform")]
    public void CruciformPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, string profile)
    {
        foreach (var mob in mobs)
            ctx.WriteLine($"{mob}: {Cruciform(profile, mob)}");
    }

    private bool Cruciform(string profile, EntityUid uid)
    {
        // Name-matched pipes hit organs/IDs/actions too; implants only belong in mobs.
        if (!HasComp<MobStateComponent>(uid))
            return false;
        _cruciform ??= GetSys<CruciformSystem>();
        return _cruciform.GrantCruciform(uid, new ProtoId<NeoTheologyProfilePrototype>(profile));
    }

    /// <summary>Force the entity's cruciform active (Epiphany equivalent).</summary>
    [CommandImplementation("activate")]
    public bool ActivateSelf(IInvocationContext ctx)
    {
        _cruciform ??= GetSys<CruciformSystem>();
        return _cruciform.Activate(Self(ctx) ?? throw new InvalidOperationException("no executing entity"));
    }

    [CommandImplementation("activate")]
    public void ActivatePiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        _cruciform ??= GetSys<CruciformSystem>();
        foreach (var mob in mobs)
            ctx.WriteLine($"{mob}: {_cruciform.Activate(mob)}");
    }

    /// <summary>Spawn an item into the entity's hands (generic version of giveoddity).</summary>
    [CommandImplementation("give")]
    public void GiveSelf(IInvocationContext ctx, string proto)
    {
        _hands ??= GetSys<SharedHandsSystem>();
        var uid = Self(ctx) ?? throw new InvalidOperationException("no executing entity");
        GiveTo(uid, proto, ctx);
    }

    [CommandImplementation("give")]
    public void GivePiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, string proto)
    {
        _hands ??= GetSys<SharedHandsSystem>();
        foreach (var mob in mobs)
            GiveTo(mob, proto, ctx);
    }

    private void GiveTo(EntityUid mob, string proto, IInvocationContext ctx)
    {
        _hands ??= GetSys<SharedHandsSystem>();
        var item = Spawn(proto, Transform(mob).Coordinates);
        if (!_hands.TryPickupAnyHand(mob, item, checkActionBlocker: false))
            ctx.WriteLine($"litany: could not place {proto} ({item}) in a hand on {mob}");
    }

    /// <summary>Spawn an item at the nearest NeoTheology altar to the mob (or on the mob if none in 5 m).</summary>
    [CommandImplementation("place")]
    public void PlaceSelf(IInvocationContext ctx, string proto)
        => Place(proto, Self(ctx) ?? throw new InvalidOperationException("no executing entity"), ctx);

    [CommandImplementation("place")]
    public void PlacePiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, string proto)
    {
        foreach (var mob in mobs)
            Place(proto, mob, ctx);
    }

    private void Place(string proto, EntityUid mob, IInvocationContext ctx)
    {
        _lookup ??= GetSys<EntityLookupSystem>();
        EntityUid where = mob;
        foreach (var altar in _lookup.GetEntitiesInRange<NeoTheologyAltarComponent>(Transform(mob).Coordinates, 5f))
        {
            where = altar;
            break;
        }

        ctx.WriteLine($"placed {proto} at {where}");
        Spawn(proto, Transform(where).Coordinates);
    }

    /// <summary>Install a core module (e.g. OxydNtModulePriestConvert, OxydNtModuleCloning) into the mob's cruciform.</summary>
    [CommandImplementation("module")]
    public bool ModuleSelf(IInvocationContext ctx, string module)
        => InstallModule(module, Self(ctx) ?? throw new InvalidOperationException("no executing entity"));

    [CommandImplementation("module")]
    public void ModulePiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, string module)
    {
        foreach (var mob in mobs)
            ctx.WriteLine($"{mob}: {InstallModule(module, mob)}");
    }

    private bool InstallModule(string module, EntityUid mob)
    {
        _modules ??= GetSys<CoreModuleSystem>();
        _cruciform ??= GetSys<CruciformSystem>();
        if (!_cruciform.TryGetCruciformEntity(mob, out var cruciform, out var comp))
            return false;

        return _modules.TryInstall(cruciform, comp, new ProtoId<CoreModulePrototype>(module));
    }

    /// <summary>Make the entity speak a line as local IC speech (drives ceremony phrases without chat-box typing).</summary>
    [CommandImplementation("say")]
    public void SaySelf(IInvocationContext ctx, string message)
        => Say(Self(ctx) ?? throw new InvalidOperationException("no executing entity"), message);

    [CommandImplementation("say")]
    public void SayPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, string message)
    {
        foreach (var mob in mobs)
            Say(mob, message);
    }

    private void Say(EntityUid mob, string message)
    {
        _chat ??= GetSys<ChatSystem>();
        _chat.TrySendInGameICMessage(
            mob, message, InGameICChatType.Speak, hideChat: false, hideLog: false,
            shell: null, player: null, nameOverride: null, checkRadioPrefix: false,
            ignoreActionBlocker: true);
    }


    private void CeremonyStep(IInvocationContext ctx, EntityUid mob)
    {
        _xform ??= GetSys<SharedTransformSystem>();
        if (TryComp<ActiveCeremonyComponent>(mob, out var starterCeremony))
        {
            if (starterCeremony.Phrases.Count < 2)
            {
                ctx.WriteLine($"{mob}: ceremony has fewer than 2 phrases left");
                return;
            }

            Say(mob, starterCeremony.Phrases[1]);
            ctx.WriteLine($"{mob} (leader): spoke next phrase");
            return;
        }

        var myXform = Transform(mob);
        var query = EntityManager.EntityQueryEnumerator<ActiveCeremonyComponent, TransformComponent>();
        while (query.MoveNext(out var starter, out var ceremony, out var starterXform))
        {
            if (myXform.MapID != starterXform.MapID ||
                (_xform!.GetWorldPosition(myXform) - _xform!.GetWorldPosition(starterXform)).Length() > ceremony.Range)
                continue;

            if (!ceremony.First && !ceremony.Participants.Contains(mob))
                continue;

            Say(mob, ceremony.Phrases[0]);
            ctx.WriteLine($"{mob} (follower): repeated the phrase");
            return;
        }

        ctx.WriteLine($"{mob}: no ceremony in range");
    }





    private void SoulReader(IInvocationContext ctx, EntityUid mob)
    {
        _cruciform ??= GetSys<CruciformSystem>();
        _implants ??= GetSys<SharedSubdermalImplantSystem>();
        _slots ??= GetSys<ItemSlotsSystem>();
        _lookup ??= GetSys<EntityLookupSystem>();

        // Prefer the mob's installed cruciform; fall back to a loose implant item within 3 m
        // (covers corpses whose cruciform was already ejected). Extraction must be a plain
        // container remove: ForceRemove deletes the implant, and the cruciform must survive.
        EntityUid implant = EntityUid.Invalid;
        if (_cruciform.TryGetCruciformEntity(mob, out var installed, out _))
        {
            _containers ??= GetSys<SharedContainerSystem>();
            if (!TryComp<ImplantedComponent>(mob, out var implanted) ||
                !_containers.Remove(installed, implanted.ImplantContainer))
            {
                ctx.WriteLine($"{mob}: cruciform {installed} could not be extracted");
                return;
            }
            implant = installed;
        }
        else
        {
            foreach (var item in _lookup.GetEntitiesInRange<CruciformComponent>(Transform(mob).Coordinates, 3f))
            {
                if (TryComp<SubdermalImplantComponent>(item, out var imp) && imp.ImplantedEntity != null)
                    continue;
                implant = item;
                break;
            }
        }

        if (!implant.IsValid())
        {
            ctx.WriteLine($"{mob}: no cruciform found (installed or loose)");
            return;
        }

        EntityUid reader = EntityUid.Invalid;
        foreach (var candidate in _lookup.GetEntitiesInRange<CruciformReaderComponent>(Transform(mob).Coordinates, 10f))
        {
            reader = candidate;
            break;
        }

        if (!reader.IsValid())
        {
            ctx.WriteLine($"{mob}: no cruciform reader within 10 m");
            return;
        }

        if (!_slots.TryGetSlot(reader, "cruciform", out var slot))
        {
            ctx.WriteLine($"{reader}: no 'cruciform' item slot");
            return;
        }

        if (slot.HasItem)
            _slots.TryEject(reader, slot, null, out _);

        if (!_slots.TryInsert(reader, slot, implant, null))
            ctx.WriteLine($"{mob}: cruciform {implant} found but reader insert failed");
        else
            ctx.WriteLine($"{mob}: cruciform {implant} moved into reader {reader}");
    }



    /// <summary>
    /// Composite scenario setups for the litany test matrix:
    /// blessing  — oddity into the active hand (DivineBlessing).
    /// commitment — altar under the mob, stripped, buckled (Commitment/Uproot targets).
    /// machines  — one of each litany machine on the tiles around the actor.
    /// offering  — oddity in hand + food produce on the front tile (HolyGuidance).
    /// full      — machines + miracle + cooldown reset.
    /// </summary>
    [CommandImplementation("setup")]
    public void SetupSelf(IInvocationContext ctx, string scenario) => Setup(ctx, scenario, null);

    [CommandImplementation("setup")]
    public void SetupPiped(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs, string scenario)
    {
        foreach (var mob in mobs)
            Setup(ctx, scenario, mob);
    }

    private void Setup(IInvocationContext ctx, string scenario, EntityUid? mob)
    {
        switch (scenario.ToLowerInvariant())
        {
            case "blessing":
            {
                var uid = mob ?? Self(ctx)
                    ?? throw new InvalidOperationException("pipe a mob or run as an entity");
                GiveTo(uid, OddityProto, ctx);
                break;
            }

            case "commitment":
            {
                var target = mob ?? Self(ctx)
                    ?? throw new InvalidOperationException("pipe a mob or run as an entity");
                Undress(target);
                var altar = Buckle(target);
                // Commitment needs a loose never-activated cruciform resting on the altar.
                if (altar is { } seat)
                    Spawn("OxydNtCruciform", Transform(seat).Coordinates);
                break;
            }

            case "machines":
            {
                var uid = mob ?? Self(ctx)
                    ?? throw new InvalidOperationException("pipe a mob or run as an entity");
                var protos = new[]
                {
                    "OxydNtEyeOfTheProtector", "OxydNtCruciformForge", "OxydNtAltar",
                    "OxydNtBioreactor", "OxydNtCruciformReader", "OxydNtCloner",
                    "OxydNtArmamentsPrinter", "OxydNtHolyDoor",
                };
                var xform = Transform(uid);
                var basePos = xform.Coordinates.Position;
                var ring = new[]
                {
                    new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
                    new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1),
                };
                _xform ??= GetSys<SharedTransformSystem>();
                for (var i = 0; i < protos.Length; i++)
                {
                    var tile = (basePos + ring[i]).Floored();
                    var spawned = Spawn(new EntProtoId(protos[i]), new EntityCoordinates(xform.ParentUid, tile));
                    MakeOperational(spawned);
                }

                break;
            }

            case "offering":
            {
                var uid = mob ?? Self(ctx)
                    ?? throw new InvalidOperationException("pipe a mob or run as an entity");
                GiveTo(uid, OddityProto, ctx);
                var xform = Transform(uid);
                var front = (xform.Coordinates.Position + xform.LocalRotation.ToVec()).Floored();
                var coords = new EntityCoordinates(xform.ParentUid, front);
                for (var i = 0; i < 40; i++)
                    Spawn(FoodProto, coords);
                break;
            }

            case "full":
                Setup(ctx, "machines", mob);
                Miracle(ctx, 5);
                Cooldowns(ctx, Self(ctx));
                break;

            default:
                ctx.WriteLine($"litany: unknown scenario '{scenario}' (blessing|commitment|machines|offering|full)");
                break;
        }
    }

    /// <summary>Anchor a spawned machine and drop its APC receiver so IsOperational passes in debug scenes.</summary>
    private void MakeOperational(EntityUid machine)
    {
        _xform ??= GetSys<SharedTransformSystem>();
        // AnchorEntity asserts on an already-anchored entity (double snap-grid cell insert).
        if (!Transform(machine).Anchored)
            _xform.AnchorEntity(machine);
        if (HasComp<ApcPowerReceiverComponent>(machine))
            RemComp<ApcPowerReceiverComponent>(machine);
    }











}

