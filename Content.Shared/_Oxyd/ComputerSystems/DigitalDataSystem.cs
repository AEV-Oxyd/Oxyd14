namespace Content.Shared._Oxyd;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class DigitalDataSystem : EntitySystem
{
    [SubscribeLocalEvent]
    public void AfterState(EntityUid id, DigitalDataHolderComponent comp, AfterAutoHandleStateEvent ev)
    {
        comp.files = comp.networkedFiles;
    }
}