using Content.Shared.GameTicking;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Gives mapped jobs their cruciform. Eris did this in
/// install_default_modules_by_job(); here the mapping is rules data so jobs stay upstream.
/// </summary>
public sealed partial class NeoTheologyJobSystem : EntitySystem
{
    [Dependency] private readonly CruciformSystem _cruciform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId is not { } jobId)
            return;

        var rules = _cruciform.GetRules();
        if (rules is null || !rules.JobProfiles.TryGetValue(jobId, out var profileId))
            return;

        _cruciform.GrantCruciform(args.Mob, profileId);
    }
}
