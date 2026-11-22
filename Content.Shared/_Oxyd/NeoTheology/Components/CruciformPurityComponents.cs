namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>Source cruciform_resist: this implant is exempt from automatic purity and Rejection.</summary>
[RegisterComponent]
public sealed partial class CruciformResistantComponent : Component;

/// <summary>A forcibly expelled implant cannot be implanted again (source permanent malfunction).</summary>
[RegisterComponent]
public sealed partial class RejectedImplantComponent : Component;
