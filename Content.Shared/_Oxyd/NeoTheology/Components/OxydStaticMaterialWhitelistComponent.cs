using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// Merges the material union of fixed lathe recipes into a machine's material
/// whitelist every time <see cref="Content.Shared.Materials.GetMaterialWhitelistEvent"/>
/// rebuilds it. Needed because lathes rebuild
/// <see cref="MaterialStorageComponent.MaterialWhiteList"/> from that event
/// (MapInit, tech changes) — a YAML <c>materialWhiteList</c> alone is
/// overwritten by the recipe-driven union upstream. Listing recipes (not raw
/// materials) keeps the whitelist in sync when a recipe's costs change.
/// </summary>
[RegisterComponent]
public sealed partial class OxydStaticMaterialWhitelistComponent : Component
{
    [DataField]
    public List<ProtoId<LatheRecipePrototype>> Recipes = new();
}
