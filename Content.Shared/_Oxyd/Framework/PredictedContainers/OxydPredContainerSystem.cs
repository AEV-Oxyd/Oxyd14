namespace Content.Shared;

/// <summary>
/// This handles...
/// </summary>
public sealed class OxydPredContainerSystem : EntitySystem
{
    public 

    public byte GenerateActionChecksum(EntityUid entity, OxydContainerAction action)
    {
        return (byte)(entity.GetHashCode() + action.GetHashCode());
    }
}