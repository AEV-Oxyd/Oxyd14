using System;
using Content.Server.Power.Generator;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared._Oxyd.NeoTheology.Events;
using Content.Shared.Power.Generator;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// The biogenerator litany surface. Fuel burn, fuel-empty shutdown and supplier management all
/// live in <see cref="GeneratorSystem"/> via the stock <see cref="FuelGeneratorComponent"/> +
/// <see cref="SolidFuelGeneratorAdapterComponent"/> pair; this system only toggles it for the
/// litany and derates the target power by Eris' fouling (<see cref="BiogeneratorComponent.Dirtiness"/>).
/// </summary>
/// <remarks>
/// Eris' console/port/generator/chamber/core part graph is flattened into one machine. No litany
/// addresses a part — they all locate the console and act on the multistructure. Split into parts
/// only if construction gameplay needs it.
/// </remarks>
public sealed partial class BiogeneratorSystem : EntitySystem
{
    [Dependency] private GeneratorSystem _generator = default!;
    [Dependency] private NeoTheologyMachineSystem _machines = default!;

    /// <summary>
    /// Eris <c>power_biogen_awake</c>: switches the machine on or off. The generator carries a
    /// single on flag, so the ritual is a toggle rather than Eris'
    /// <c>activate</c>/<c>deactivate</c> pair.
    /// </summary>
    public bool TryToggle(EntityUid uid, BiogeneratorComponent? generator = null)
    {
        if (!Resolve(uid, ref generator))
            return false;

        if (TryComp<FuelGeneratorComponent>(uid, out var fuel))
            _generator.SetFuelGeneratorOn(uid, !fuel.On, fuel);

        return true;
    }

    /// <summary>
    /// PowerBiogenerator bridge (Eris <c>rituals/machinery.dm:151-168</c>): the litany finds the
    /// biogenerator near its screen and this flips it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnLitanyToggleBiogenerator(Entity<BiogeneratorComponent> ent, ref LitanyToggleBiogeneratorEvent args)
    {
        args.Handled = TryToggle(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        UpdateConsoles(frameTime);

        // Fouling derates the stock generator's target power; everything else (burn rate,
        // supplier state, fuel exhaustion) is GeneratorSystem's own tick.
        var query = EntityQueryEnumerator<BiogeneratorComponent, FuelGeneratorComponent>();
        while (query.MoveNext(out var uid, out var bio, out var fuel))
        {
            var target = fuel.MaxTargetPower * (1f - Math.Clamp(bio.Dirtiness, 0f, 1f));
            if (Math.Abs(fuel.TargetPower - target) > 1f)
                _generator.SetFuelGeneratorTargetPower(uid, target, fuel);
        }
    }
}
