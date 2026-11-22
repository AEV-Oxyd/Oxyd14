using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.UI;

/// <summary>
/// UI surface for the flattened NT machine consoles (Eris bioreactor.tmpl / nt_biogen.tmpl):
/// read-only status panes the server re-pushes once a second while open.
/// </summary>
[Serializable, NetSerializable]
public enum BioreactorConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum BiogeneratorConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class BioreactorConsoleState : BoundUserInterfaceState
{
    public readonly bool Operational;
    public readonly bool ChamberClosed;
    public readonly bool ChamberSolution;
    public readonly bool ChamberBreached;
    public readonly int ChamberContents;

    public BioreactorConsoleState(bool operational, bool chamberClosed, bool chamberSolution,
        bool chamberBreached, int chamberContents)
    {
        Operational = operational;
        ChamberClosed = chamberClosed;
        ChamberSolution = chamberSolution;
        ChamberBreached = chamberBreached;
        ChamberContents = chamberContents;
    }
}

[Serializable, NetSerializable]
public sealed class BiogeneratorConsoleState : BoundUserInterfaceState
{
    public readonly bool Operational;
    public readonly bool GeneratorOn;
    public readonly float FuelAmount;
    public readonly float TargetPower;
    public readonly float MaxTargetPower;
    public readonly float Dirtiness;

    public BiogeneratorConsoleState(bool operational, bool generatorOn, float fuelAmount,
        float targetPower, float maxTargetPower, float dirtiness)
    {
        Operational = operational;
        GeneratorOn = generatorOn;
        FuelAmount = fuelAmount;
        TargetPower = targetPower;
        MaxTargetPower = maxTargetPower;
        Dirtiness = dirtiness;
    }
}
