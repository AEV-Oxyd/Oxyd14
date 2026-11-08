using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.NeoTheology.Events;

[ByRefEvent]
public readonly record struct NeoTheologyRevelationEvent(EntityUid User, EntityUid Target);

[ByRefEvent]
public record struct NeoTheologyCrusadeEvent(bool Handled = false);

[Serializable, NetSerializable]
public sealed partial class SwordOfTruthFlashEvent : SimpleDoAfterEvent;
