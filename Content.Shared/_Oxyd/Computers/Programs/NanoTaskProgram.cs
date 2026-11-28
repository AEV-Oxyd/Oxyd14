using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Oxyd.Computers.Programs;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause, AutoGenerateComponentState]
public sealed partial class NanoTaskProgramComponent : Component
{
    public const string DefaultTasksFilePath = "/tasks/tasks.tsk";

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextPrintAllowedAfter = TimeSpan.Zero;

    [DataField]
    public TimeSpan PrintDelay = TimeSpan.FromSeconds(5);
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NanoTaskPrintedComponent : Component
{
    [DataField, AutoNetworkedField]
    public NanoTaskItem? Task;
}

[Serializable, NetSerializable]
public enum NanoTaskPriority : byte
{
    High,
    Medium,
    Low
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class NanoTaskItem : IRobustCloneable<NanoTaskItem>
{
    public static int MaximumStringLength = 30;

    public readonly string Description;
    public readonly string TaskIsFor;
    public readonly bool IsTaskDone;
    public readonly NanoTaskPriority Priority;

    public NanoTaskItem(string description, string taskIsFor, bool isTaskDone, NanoTaskPriority priority)
    {
        Description = description;
        TaskIsFor = taskIsFor;
        IsTaskDone = isTaskDone;
        Priority = priority;
    }

    public bool Validate() => Description.Length <= MaximumStringLength && TaskIsFor.Length <= MaximumStringLength;

    public NanoTaskItem Clone() => new(Description, TaskIsFor, IsTaskDone, Priority);
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class NanoTaskItemAndId
{
    public readonly int Id;
    public readonly NanoTaskItem Data;

    public NanoTaskItemAndId(int id, NanoTaskItem data)
    {
        Id = id;
        Data = data;
    }
}

[Serializable, NetSerializable]
public sealed class NanoTaskUiState : BoundUserInterfaceState
{
    public bool HasHardDrive { get; }
    public List<NanoTaskItemAndId> Tasks { get; }
    public bool HasPrinterModule { get; }
    public string StatusMessage { get; }

    public NanoTaskUiState(
        bool hasHardDrive,
        List<NanoTaskItemAndId> tasks,
        bool hasPrinterModule = false,
        string statusMessage = "")
    {
        HasHardDrive = hasHardDrive;
        Tasks = tasks;
        HasPrinterModule = hasPrinterModule;
        StatusMessage = statusMessage;
    }
}

public interface INanoTaskUiMessagePayload
{
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class NanoTaskAddTask : INanoTaskUiMessagePayload
{
    public readonly NanoTaskItem Item;

    public NanoTaskAddTask(NanoTaskItem item)
    {
        Item = item;
    }
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class NanoTaskUpdateTask : INanoTaskUiMessagePayload
{
    public readonly NanoTaskItemAndId Item;

    public NanoTaskUpdateTask(NanoTaskItemAndId item)
    {
        Item = item;
    }
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class NanoTaskDeleteTask : INanoTaskUiMessagePayload
{
    public readonly int Id;

    public NanoTaskDeleteTask(int id)
    {
        Id = id;
    }
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class NanoTaskPrintTask : INanoTaskUiMessagePayload
{
    public readonly NanoTaskItem Item;

    public NanoTaskPrintTask(NanoTaskItem item)
    {
        Item = item;
    }
}

[Serializable, NetSerializable]
public sealed class NanoTaskActionMessage : BoundUserInterfaceMessage
{
    public readonly INanoTaskUiMessagePayload Payload;

    public NanoTaskActionMessage(INanoTaskUiMessagePayload payload)
    {
        Payload = payload;
    }
}
