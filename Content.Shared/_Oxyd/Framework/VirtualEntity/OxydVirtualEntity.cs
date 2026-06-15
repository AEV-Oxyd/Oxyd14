using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd;


[Serializable, NetSerializable]
public partial class UpdateVirtualPoolEvent
{
    public List<NetEntity> entities = new();
    public List<NetEntity> converted = new();
    public List<NetEntity> invalid = new();
}
[Serializable, NetSerializable]
public partial class VirtualEntConvertedEvent
{
    public NetEntity entity;
    public string prototype = string.Empty;
    public string localIdentifier = string.Empty;
}
