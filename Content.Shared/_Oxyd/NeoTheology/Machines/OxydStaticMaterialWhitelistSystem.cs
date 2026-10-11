using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Machines;

/// <summary>
/// Merges the material union of <see cref="OxydStaticMaterialWhitelistComponent.Recipes"/>
/// into the machine's material whitelist whenever it is rebuilt.
/// </summary>
public sealed partial class OxydStaticMaterialWhitelistSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OxydStaticMaterialWhitelistComponent, GetMaterialWhitelistEvent>(OnGetWhitelist);
    }

    private void OnGetWhitelist(EntityUid uid, OxydStaticMaterialWhitelistComponent comp, ref GetMaterialWhitelistEvent args)
    {
        if (args.Storage != uid)
            return;
        foreach (var id in comp.Recipes)
        {
            if (!_proto.Resolve(id, out var recipe))
            {
                Log.Error($"OxydStaticMaterialWhitelist on {ToPrettyString(uid)} references missing recipe '{id}'");
                continue;
            }
            args.Whitelist = args.Whitelist.Union(recipe.Materials.Keys).ToList();
        }
    }
}
