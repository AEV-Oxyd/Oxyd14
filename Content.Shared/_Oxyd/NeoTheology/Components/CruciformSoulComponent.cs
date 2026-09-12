using Content.Shared.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// The soul a cruciform keeps. <see cref="CoreModuleBehaviorSystem"/> writes it when the
/// cloning module is installed and again when it is removed; it lives on the cruciform, not
/// on the module, so pulling the module does not lose it — that is the resurrection path
/// (Eris <c>datum/core_module/cruciform/cloning</c>).
/// </summary>
/// <remarks>
/// This fork has no <c>HumanoidAppearanceComponent</c>, so the appearance half of the Eris
/// snapshot rides <see cref="Profile"/> (a <see cref="HumanoidCharacterProfile"/> carries
/// appearance, species, gender and age). Blood type and languages have no home here and are
/// deliberately not invented.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class CruciformSoulComponent : Component
{
    [DataField] public bool HasSnapshot;
    [DataField] public string? Ckey;
    [DataField] public EntityUid? MindId;
    [DataField] public string Name = string.Empty;
    [DataField] public HumanoidCharacterProfile? Profile;
    [DataField] public int BiomassCost = 100;
}
