using System.Linq;
using Content.Server._Oxyd.Pda;
using Content.Server.Administration;
using Content.Shared._Oxyd.Pda;
using Content.Shared.Administration;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.PDA;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;

namespace Content.Server._Oxyd.Economy;

[ToolshedCommand(Name = "pda"), AdminCommand(AdminFlags.Debug)]
public sealed class PdaDebugCommand : ToolshedCommand
{
    private static readonly EntProtoId ClubPda = "OxydPdaClub";
    private static readonly EntProtoId ArtistPda = "OxydPdaArtist";

    [CommandImplementation("programs")]
    public void ProgramsSelf(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        Programs(ctx, uid);
    }

    [CommandImplementation("programs")]
    public void Programs(IInvocationContext ctx, [PipedArgument] EntityUid mob)
    {
        if (!HasComp<MobStateComponent>(mob))
        {
            ctx.WriteLine($"{mob}: not a mob, skipped");
            return;
        }

        if (!GetSys<InventorySystem>().TryGetSlotEntity(mob, "id", out var idUid) ||
            !TryComp<PdaComponent>(idUid.Value, out _) ||
            !HasComp<CartridgeLoaderComponent>(idUid.Value))
        {
            ctx.WriteLine($"{EntityManager.ToPrettyString(mob)}: no PDA");
            return;
        }

        var programs = GetSys<CartridgeLoaderSystem>().GetAllPrograms(idUid.Value).ToList();
        var names = programs.Select(p => MetaData(p).EntityName).ToList();
        ctx.WriteLine($"{EntityManager.ToPrettyString(mob)} pda={EntityManager.ToPrettyString(idUid.Value)} programs=[{string.Join(", ", names)}]");
    }

    [CommandImplementation("programs")]
    public void ProgramsMany(IInvocationContext ctx, [PipedArgument] IEnumerable<EntityUid> mobs)
    {
        foreach (var mob in mobs.ToList())
            Programs(ctx, mob);
    }

    [CommandImplementation("spritecheck")]
    public void SpriteCheck(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        if (!GetSys<InventorySystem>().TryGetSlotEntity(uid, "id", out var idUid))
        {
            ctx.WriteLine("no PDA");
            return;
        }

        var meta = EntityManager.GetComponent<MetaDataComponent>(idUid.Value);
        ctx.WriteLine($"pda={EntityManager.ToPrettyString(idUid.Value)} proto={meta.EntityPrototype?.ID}");
    }

    [CommandImplementation("give")]
    public void Give(IInvocationContext ctx, string which)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        EntProtoId proto = which.ToLowerInvariant() switch
        {
            "club" => ClubPda,
            "artist" => ArtistPda,
            "cpu" => (EntProtoId)"OxydHwCpuAdv",
            "hdd" => (EntProtoId)"OxydHwHddAdvanced",
            "net" => (EntProtoId)"OxydHwNetAdvanced",
            "tesla" => (EntProtoId)"OxydHwTeslaLink",
            "printer" => (EntProtoId)"OxydHwPrinter",
            "signaller" or "signal" => (EntProtoId)"OxydHwSignaller",
            "medscan" or "medical" => (EntProtoId)"OxydHwScannerMedical",
            "atmossan" or "atmos" => (EntProtoId)"OxydHwScannerAtmos",
            "pricescan" or "price" => (EntProtoId)"OxydHwScannerPrice",
            "reagentscan" or "reagent" => (EntProtoId)"OxydHwScannerReagent",
            _ => (EntProtoId)which,
        };

        var pda = Spawn(proto, Transform(uid).Coordinates);
        ctx.WriteLine($"spawned {EntityManager.ToPrettyString(pda)} — equip manually or via inventory");
    }

    [CommandImplementation("hw")]
    public void HardwareSelf(IInvocationContext ctx)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        Hardware(ctx, uid);
    }

    [CommandImplementation("hw")]
    public void Hardware(IInvocationContext ctx, [PipedArgument] EntityUid mob)
    {
        if (!GetSys<InventorySystem>().TryGetSlotEntity(mob, "id", out var idUid) ||
            !TryComp<OxydModularPdaComponent>(idUid.Value, out var mod))
        {
            ctx.WriteLine($"{EntityManager.ToPrettyString(mob)}: no modular PDA");
            return;
        }

        GetSys<OxydModularPdaSystem>().Recalculate((idUid.Value, mod));
        ctx.WriteLine(GetSys<OxydModularPdaSystem>().DescribeHardware((idUid.Value, mod)));
    }

    [CommandImplementation("install")]
    public void InstallHw(IInvocationContext ctx, string slot, string proto)
    {
        var uid = ExecutingEntity(ctx) ?? throw new InvalidOperationException("no executing entity");
        if (!GetSys<InventorySystem>().TryGetSlotEntity(uid, "id", out var idUid) ||
            !HasComp<OxydModularPdaComponent>(idUid.Value))
        {
            ctx.WriteLine("no modular PDA equipped");
            return;
        }

        var slotId = slot.ToLowerInvariant() switch
        {
            "cpu" => OxydModularPdaComponent.CpuSlotId,
            "hdd" => OxydModularPdaComponent.HddSlotId,
            "net" => OxydModularPdaComponent.NetSlotId,
            "per" or "peripheral" or "tesla" or "printer" or "signaller" or "scanner" or "medical" or "atmos" or "price" or "reagent"
                => OxydModularPdaComponent.PeripheralSlotId,
            _ => slot,
        };

        var slots = GetSys<ItemSlotsSystem>();
        if (slots.GetItemOrNull(idUid.Value, slotId) is { } existing)
        {
            slots.TryEject(idUid.Value, slotId, uid, out _);
            ctx.WriteLine($"ejected previous {EntityManager.ToPrettyString(existing)}");
        }

        var part = Spawn(proto, Transform(uid).Coordinates);
        if (!slots.TryInsert(idUid.Value, slotId, part, uid))
        {
            ctx.WriteLine($"install failed for {proto} into {slotId}");
            Del(part);
            return;
        }

        var mod = Comp<OxydModularPdaComponent>(idUid.Value);
        GetSys<OxydModularPdaSystem>().Recalculate((idUid.Value, mod));
        ctx.WriteLine($"installed {proto} → disk={mod.EffectiveDiskSpace} net={mod.EffectiveNetworkRange}");
    }
}
