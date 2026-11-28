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
}
