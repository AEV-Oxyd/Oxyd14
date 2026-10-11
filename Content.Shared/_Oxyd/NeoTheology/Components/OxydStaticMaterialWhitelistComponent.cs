using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Merges a fixed material list into a machine's material whitelist every time
/// <see cref="Content.Shared.Materials.GetMaterialWhitelistEvent"/> rebuilds it.
/// Needed because lathes rebuild <see cref="MaterialStorageComponent.MaterialWhiteList"/>
/// from that event (MapInit, tech changes) — a YAML <c>materialWhiteList</c> alone
/// is overwritten by the recipe-driven union upstream. Server-side only.
/// </summary>
[RegisterComponent]
public sealed partial class OxydStaticMaterialWhitelistComponent : Component
{
    [DataField]
    public List<ProtoId<MaterialPrototype>> Materials = new();
}
