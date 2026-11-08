using Robust.Shared.Serialization;

namespace Content.Shared.PDA
{
    [Serializable, NetSerializable]
    public enum PdaVisuals
    {
        PdaType,
        IdCardInserted
    }

    [Serializable, NetSerializable]
    public enum PdaUiKey
    {
        Key
    }

    /// <summary>
    /// Universal bound UI key for all computer, PDA, APC, Atmos, Lathe, and digital device instances.
    /// Shares the same underlying value as <see cref="PdaUiKey"/> so both YAML names resolve compatibly.
    /// </summary>
    [Serializable, NetSerializable]
    public enum ComputerUiKey
    {
        Key = PdaUiKey.Key
    }

    [Serializable, NetSerializable]
    public enum ComputerUiTemplate : byte
    {
        Pda = 0,
        Desktop = 1,
        Embedded = 2
    }

    [Serializable, NetSerializable]
    public enum ComputerBootState : byte
    {
        Off = 0,
        NoPower = 1,
        WireCut = 2,
        NoHardDrive = 3,
        NoRam = 4,
        NoBootProgram = 5,
        Running = 6
    }
}
