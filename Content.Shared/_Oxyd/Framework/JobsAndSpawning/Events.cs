using Content.Shared.Roles;

namespace Content.Server._Oxyd.Framework.JobsAndSpawning;

public sealed class JobAfterSpawnEvent
{
    public EntityUid spawned;
    public required JobPrototype job;
}
