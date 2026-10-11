using System.Linq;
using Content.Shared._Oxyd.NeoTheology.Components;
using Content.Shared.Materials;

namespace Content.Server._Oxyd.NeoTheology.Machines;

/// <summary>
/// Merges <see cref="OxydStaticMaterialWhitelistComponent.Materials"/> into the
/// machine's material whitelist whenever it is rebuilt.
/// </summary>
public sealed partial class OxydStaticMaterialWhitelistSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<OxydStaticMaterialWhitelistComponent, GetMaterialWhitelistEvent>(OnGetWhitelist);
    }

    private void OnGetWhitelist(EntityUid uid, OxydStaticMaterialWhitelistComponent comp, ref GetMaterialWhitelistEvent args)
    {
        if (args.Storage != uid)
            return;
        args.Whitelist = args.Whitelist.Union(comp.Materials).ToList();
    }
}
