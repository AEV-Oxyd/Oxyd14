using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.CartridgeLoader;
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
        var proto = which.ToLowerInvariant() switch
        {
            "club" => ClubPda,
            "artist" => ArtistPda,
            _ => (EntProtoId)which,
        };

        var pda = Spawn(proto, Transform(uid).Coordinates);
        ctx.WriteLine($"spawned {EntityManager.ToPrettyString(pda)} — equip manually or via inventory");
    }
}
