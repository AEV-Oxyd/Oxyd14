namespace Content.Shared;

public sealed partial class IgnorableComponentState : IComponentState
{
    /// <summary>
    ///  The application of this state should be ignored if the controlled
    /// entity clientside is this! SPCR 2026
    /// </summary>
    public NetEntity ignore = NetEntity.Invalid;
    
    public IgnorableComponentState(NetEntity ignore) { this.ignore = ignore;}
}