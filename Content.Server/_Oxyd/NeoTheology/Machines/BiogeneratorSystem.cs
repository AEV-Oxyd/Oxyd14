using System;
using Content.Server.Power.Components;
using Content.Shared._Oxyd.NeoTheology.Components;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Feeds the power network from a working biogenerator.
/// </summary>
/// <remarks>
/// Eris' console/port/generator/chamber/core part graph is flattened into one machine. No litany
/// addresses a part — they all locate the console and act on the multistructure. Split into parts
/// only if construction gameplay needs it.
/// </remarks>
public sealed partial class BiogeneratorSystem : EntitySystem
{
    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BiogeneratorComponent, PowerSupplierComponent>();

        while (query.MoveNext(out _, out var gen, out var supplier))
        {
            if (!gen.Working)
                continue;

            supplier.Enabled = true;
            supplier.MaxSupply = gen.OutputWatts * (1f - Math.Clamp(gen.Dirtiness, 0f, 1f));
        }
    }
}
