using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Oxyd.Framework;

public sealed class OxydHelpers : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;



    public override void Initialize()
    {
        base.Initialize();
    }
}
