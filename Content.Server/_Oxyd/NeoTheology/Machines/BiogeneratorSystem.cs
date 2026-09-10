using System;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Materials;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Feeds the power network from a working biogenerator, burning biomatter out of the machine's
/// <see cref="MaterialStorageComponent"/> as it runs.
/// </summary>
/// <remarks>
/// Eris' console/port/generator/chamber/core part graph is flattened into one machine. No litany
/// addresses a part — they all locate the console and act on the multistructure. Split into parts
/// only if construction gameplay needs it.
/// </remarks>
public sealed partial class BiogeneratorSystem : EntitySystem
{
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BiogeneratorComponent, PowerSupplierComponent, MaterialStorageComponent>();

        while (query.MoveNext(out var uid, out var gen, out var supplier, out var storage))
        {
            // Inert while switched off, and while the store is dry it supplies nothing.
            if (!gen.Working || _materialStorage.GetMaterialAmount(uid, "Biomatter", storage) <= 0)
            {
                supplier.Enabled = false;
                supplier.MaxSupply = 0f;
                continue;
            }

            // Burn whole biomatter units only; carry the fraction to the next tick.
            gen.BiomatterAccumulator += gen.BiomatterPerSecond * frameTime;
            var owed = (int) gen.BiomatterAccumulator;
            if (owed > 0 && _materialStorage.TryChangeMaterialAmount(uid, "Biomatter", -owed, storage))
                gen.BiomatterAccumulator -= owed;

            supplier.Enabled = true;
            supplier.MaxSupply = gen.OutputWatts * (1f - Math.Clamp(gen.Dirtiness, 0f, 1f));
        }
    }
}
