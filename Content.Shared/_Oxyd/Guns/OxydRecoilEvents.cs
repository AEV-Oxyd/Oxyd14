using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Oxyd.OxydGunSystem;

[Serializable, NetSerializable]
public sealed class RecoilChangedEvent : EntityEventArgs
{
    public required float oldRecoil;
    public required float currentRecoil;
    public required GameTick fromTick;
}

[Serializable, NetSerializable]
public sealed class RecoilGetModifiersEvent : CancellableEntityEventArgs
{
    public float add = 0f;
    public float multiply = 1f;
    public float? set;
}
