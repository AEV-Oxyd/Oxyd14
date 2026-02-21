using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Oxyd.Framework;

public sealed class OxydHelpers : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;

    public int ticksFuture = CVars.maxFutureTicksAccepted.DefaultValue;

    public int ticksPast = CVars.maxPastTicksAccepted.DefaultValue;


    public override void Initialize()
    {
        _config.OnValueChanged(CVars.maxFutureTicksAccepted,  i => ticksFuture = i, true);
        _config.OnValueChanged(CVars.maxFutureTicksAccepted,  i => ticksFuture = i, true);
        base.Initialize();
    }
}
