using Content.Shared.Tools;

namespace Content.Shared._Oxyd.Tools;

public class OxydToolGetModifiersEvent
{
    public EntityUid user;
    public EntityUid? target;
    public TimeSpan delay;
    public required IEnumerable<string> qualities;
}
