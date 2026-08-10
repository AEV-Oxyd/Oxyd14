namespace Content.Shared._Oxyd;

/// <summary>
/// This handles...
/// </summary>
public sealed class DigitalDataSystem : EntitySystem
{
    [SubscribeLocalEvent]
    public void AfterState(EntityUid id, DigitalDataHolderComponent comp, AfterAutoHandleStateEvent ev)
    {
        comp.files = comp.networkedFiles;
    }
}