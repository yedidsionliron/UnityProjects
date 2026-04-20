using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interface that the future dispatcher implementation will consume.
/// AMRController and GridMap expose these queries; the dispatcher calls AssignTask.
/// </summary>
public interface IDispatcher
{
    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>Assign a task to a specific robot. Robot must be Idle.</summary>
    void AssignTask(AMRController robot, AMRTask task);

    // ── Queries ───────────────────────────────────────────────────────────

    /// <summary>Returns position, state, and carried gaylord for any robot.</summary>
    RobotStatus QueryRobotStatus(AMRController robot);

    /// <summary>Returns the runtime state of a grid cell.</summary>
    CellState QueryCellOccupancy(int row, int col);

    /// <summary>Returns all robots currently in Idle state.</summary>
    List<AMRController> GetIdleRobots();

    /// <summary>Returns all occupied storage slots and the gaylord sitting in each.</summary>
    List<(Vector2Int cell, GameObject gaylord)> GetOccupiedStorageSlots();
}
