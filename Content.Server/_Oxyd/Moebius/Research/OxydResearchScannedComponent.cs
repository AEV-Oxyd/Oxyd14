namespace Content.Server._Oxyd.Moebius.Research;

/// <summary>
/// Marks an entity the science tool has already scanned, so the same object or specimen
/// can't be rescanned for repeat research points.
/// </summary>
[RegisterComponent]
public sealed partial class OxydResearchScannedComponent : Component;
