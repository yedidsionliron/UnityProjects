using UnityEngine;

public enum TaskType
{
    PickupFromChute,      // go to sort chute, lift gaylord, carry to storage
    DeliverToStorage,     // carry gaylord to assigned storage slot
    DeliverEmptyToChute,  // pick up empty gaylord from buffer/storage, deliver to chute
    DeliverToQueue,       // carry gaylord from storage to feeder queue entry (Q10)
    DeliverToStaging,     // carry gaylord from storage to front staging column (St1-30)
    ReplenishStaging,     // move gaylord from backup staging (St31-90) to front (St1-30)
    ReturnEmpty,          // collect empty gaylord from staging, return to storage
    Charge,               // navigate to charging area and wait
}

/// <summary>
/// Describes a single unit of work assigned to an AMR.
/// Locations are identified by their label string (e.g. "S5", "147", "St12", "E3").
/// The movement engine resolves labels to grid coordinates via LocationRegistry.
/// </summary>
[System.Serializable]
public struct AMRTask
{
    public TaskType taskType;

    /// <summary>
    /// Label of the cell where the robot picks up the gaylord (e.g. "S5", "E3", "St45").
    /// Empty string for Charge tasks.
    /// </summary>
    public string originLabel;

    /// <summary>
    /// Label of the cell where the robot deposits the gaylord (e.g. "147", "St12", "Q10").
    /// Empty string for Charge tasks.
    /// </summary>
    public string destLabel;

    /// <summary>
    /// Optional reference to the specific gaylord GameObject involved.
    /// May be null when the task is created before the gaylord is known.
    /// </summary>
    public GameObject gaylordRef;

    public static AMRTask Charge() =>
        new AMRTask { taskType = TaskType.Charge, originLabel = "", destLabel = "" };
}

/// <summary>
/// Snapshot of a robot's current status, returned by the dispatcher query interface.
/// </summary>
[System.Serializable]
public struct RobotStatus
{
    public Vector2Int cell;         // current grid cell (row-1, col-1)
    public AMRState   state;
    public AMRTask    currentTask;
    public GameObject carriedGaylord;
}
