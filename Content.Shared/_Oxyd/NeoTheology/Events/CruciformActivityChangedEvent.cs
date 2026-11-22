namespace Content.Shared._Oxyd.NeoTheology.Events;

/// <summary>Content-side lifecycle notification, shared by observation and uplink revocation.</summary>
[ByRefEvent]
public readonly record struct CruciformActivityChangedEvent(EntityUid? Body, bool Active);
