using Content.Client._Oxyd.Framework;
using Content.Shared._Oxyd.Predictors;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Oxyd.Predictors;

/// <summary>
/// This handles...
/// </summary>
public sealed class ClientBasicPhysicsPredictorSystem : BasicPhysicsPredictorSystem
{
    [Dependency] private readonly IPlayerManager _lplayer = default!;
    public override void Initialize()
    {
        base.Initialize();
    }

    public override void onAttach(Entity<UseBasicPredictionComponent> ent, ref PlayerAttachedEvent args)
    {
        base.onAttach(ent, ref args);
        if (_lplayer?.LocalSession == args.Player)
            return;
        EnsureComp<BasicPredictorOffsetSetterComponent>(ent);
    }
}
