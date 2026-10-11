using Content.Shared._Oxyd.NeoTheology.UI;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Server-only: the actor's in-flight litany cast. Per-entity cast state lives on the
/// actor entity, not on <see cref="LitanySystem"/> — the component is added when a cast
/// begins and removed when it commits, expires, or is cancelled.
/// </summary>
[RegisterComponent]
public sealed partial class LitanyPendingCastComponent : Component
{
    public PendingLitanyCast Cast = default!;
}

/// <summary>Server-only: per-actor litany request rate-limit state.</summary>
[RegisterComponent]
public sealed partial class LitanyRateLimitComponent : Component
{
    public TimeSpan WindowStart;
    public int RequestsInWindow;
    public TimeSpan LastBegin;
}

/// <summary>
/// Server-only singleton holding round-global litany state — cast cooldowns and the
/// test-only availability overrides. The data lives on a dedicated nullspace entity so
/// it stays component state; the entity is spawned lazily and deleted on round cleanup.
/// </summary>
[RegisterComponent]
internal sealed partial class LitanyGlobalStateComponent : Component
{
    public Dictionary<string, TimeSpan> Cooldowns = new(StringComparer.Ordinal);
    public Dictionary<string, bool> AvailabilityOverrides = new(StringComparer.Ordinal);
}

/// <summary>
/// Test-only marker: integration fixtures without a connected session put this on a
/// body so player-only cast paths treat it as an actor.
/// </summary>
[RegisterComponent]
internal sealed partial class LitanyTestingActorComponent : Component;

/// <summary>Test-only: captures the last actor-targeted snapshot for this viewer.</summary>
[RegisterComponent]
internal sealed partial class LitanyTestingSnapshotComponent : Component
{
    public LitanyViewerSnapshotMessage? Last;
}
