using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Technomancer.Components;

/// <summary>
/// Oxyd pulsar engine console — CEV-Eris code/modules/pulsar_engine/pulsar_consoles.dm.
/// Holds the virtual satellite's state; the 5-z PULSAR map minigame from Eris is
/// emulated as a small logical grid (Oxyd has no overmap — see blueprint §4).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OxydPulsarComponent : Component
{
    /// Satellite position on the virtual PULSAR grid (Eris PULSAR_SIZE).
    [DataField, AutoNetworkedField]
    public Vector2i SatellitePos = new(7, 7);

    /// Beam origins — regenerated each orbit cycle like pulsar_map.dm's ship effect.
    [DataField, AutoNetworkedField]
    public List<Vector2i> Beams = new();

    /// Remaining fuel units (Eris fuel tank scan, max 700 like pulsar_fuel_tank).
    [DataField, AutoNetworkedField]
    public int Fuel;

    /// Eris thrust_cost = round((1/specific_mass)*2) — kept as a flat per-move cost.
    [DataField]
    public int FuelPerMove = 4;

    /// Percent of produced power rerouted to shields (0-100).
    [DataField, AutoNetworkedField]
    public int ShieldPower = 45;

    /// Power needed to sustain shields, percent of production (Eris constant-ish).
    [DataField]
    public float ShieldCost = 0.398f;

    /// Seconds until the orbit decays (Eris ORBIT_DECAY_TIMER = 15min).
    [DataField, AutoNetworkedField]
    public float OrbitDecaySeconds = 900f;

    /// Maximum bridge output, 25 MW like PULSAR_100_POWER.
    [DataField]
    public float MaxPowerWatts = 25000000f;

    /// Grid size of the virtual PULSAR map.
    [DataField]
    public int GridSize = 15;
}

/// Distance-based produced fraction, same linear formula as the Eris console.
public static class OxydPulsarMath
{
    public static float ProducedFraction(Vector2i sat, IReadOnlyList<Vector2i> beams, int gridSize)
    {
        if (beams.Count == 0) return 0.49f;
        var best = float.MaxValue;
        foreach (var b in beams)
            best = Math.Min(best, (sat - b).Length);
        var maxDist = gridSize * 1.4142f;
        return Math.Clamp(1f - best / maxDist, 0f, 1f);
    }
}

[Serializable, NetSerializable]
public enum OxydPulsarUiKey : byte
{
    Key
}

/// BUI state mirroring pulsar.tmpl rows.
[Serializable, NetSerializable]
public sealed class OxydPulsarState : BoundUserInterfaceState
{
    public float ProducedFraction;
    public int Fuel;
    public int FuelPerMove;
    public int ShieldPower;
    public float ShieldNeededPct;
    public float EffectiveFraction;
    public float OrbitDecaySeconds;
    public Vector2i SatellitePos;
    public List<Vector2i> Beams = new();
    public int GridSize;
}

[Serializable, NetSerializable]
public sealed class OxydPulsarMoveMessage(Vector2i dir) : BoundUserInterfaceMessage
{
    public Vector2i Dir = dir;
}

[Serializable, NetSerializable]
public sealed class OxydPulsarShieldMessage(int pct) : BoundUserInterfaceMessage
{
    public int Pct = pct;
}
