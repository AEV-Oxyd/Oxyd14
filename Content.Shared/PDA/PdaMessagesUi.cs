using Robust.Shared.Serialization;

namespace Content.Shared.PDA;

[Serializable, NetSerializable]
public sealed class PdaToggleFlashlightMessage : BoundUserInterfaceMessage
{
    public PdaToggleFlashlightMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaShowRingtoneMessage : BoundUserInterfaceMessage
{
    public PdaShowRingtoneMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaShowUplinkMessage : BoundUserInterfaceMessage
{
    public PdaShowUplinkMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaLockUplinkMessage : BoundUserInterfaceMessage
{
    public PdaLockUplinkMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaShowMusicMessage : BoundUserInterfaceMessage
{
    public PdaShowMusicMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaRequestUpdateInterfaceMessage : BoundUserInterfaceMessage
{
    public PdaRequestUpdateInterfaceMessage() { }
}

[Serializable, NetSerializable]
public sealed class PdaCloseProgramTabMessage : BoundUserInterfaceMessage
{
    public NetEntity Program { get; }

    public PdaCloseProgramTabMessage(NetEntity program)
    {
        Program = program;
    }
}

[Serializable, NetSerializable]
public sealed class PdaSwitchViewMessage : BoundUserInterfaceMessage
{
    public int View { get; }

    public PdaSwitchViewMessage(int view)
    {
        View = view;
    }
}
