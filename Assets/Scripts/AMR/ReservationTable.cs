using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Time-windowed cell reservation table.
///
/// Before a robot begins moving to its next cell it calls Reserve() for that cell.
/// If another robot has already reserved the cell for an overlapping time window,
/// Reserve() returns false and the robot must replan.
///
/// Deadlock detection: if a robot has been blocked (unable to reserve its next cell)
/// for longer than <see cref="deadlockTimeout"/> seconds, it backs up one cell and replans.
/// </summary>
public class ReservationTable : MonoBehaviour
{
    [Tooltip("Seconds before a blocking reservation is considered a deadlock candidate.")]
    public float deadlockTimeout = 3f;

    [Tooltip("Maximum backup-and-replan attempts before giving up and waiting.")]
    public int maxBackupAttempts = 3;

    // ── Data ───────────────────────────────────────────────────────────────
    private readonly Dictionary<Vector2Int, List<Reservation>> _table =
        new Dictionary<Vector2Int, List<Reservation>>();

    // Per-robot blocked tracking for deadlock detection
    private readonly Dictionary<AMRController, BlockedInfo> _blocked =
        new Dictionary<AMRController, BlockedInfo>();

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to reserve <paramref name="cell"/> for <paramref name="robot"/>
    /// from <paramref name="timeFrom"/> to <paramref name="timeTo"/>.
    /// Returns true on success; false if the cell is already reserved in that window.
    /// </summary>
    public bool TryReserve(Vector2Int cell, AMRController robot, float timeFrom, float timeTo)
    {
        if (!_table.TryGetValue(cell, out var list))
        {
            list = new List<Reservation>();
            _table[cell] = list;
        }

        // Prune expired reservations
        float now = Time.time;
        list.RemoveAll(r => r.timeTo < now);

        // Check overlap (excluding this robot's own existing reservations)
        foreach (var r in list)
        {
            if (r.robot == robot) continue;
            if (r.timeFrom < timeTo && r.timeTo > timeFrom)
                return false; // conflict
        }

        // Remove any previous reservation by this robot on this cell, then add new one
        list.RemoveAll(r => r.robot == robot);
        list.Add(new Reservation { robot = robot, timeFrom = timeFrom, timeTo = timeTo });
        return true;
    }

    /// <summary>Release all reservations held by <paramref name="robot"/> on <paramref name="cell"/>.</summary>
    public void Release(Vector2Int cell, AMRController robot)
    {
        if (!_table.TryGetValue(cell, out var list)) return;
        list.RemoveAll(r => r.robot == robot);
    }

    /// <summary>Release every reservation held by a robot (called on task completion or abort).</summary>
    public void ReleaseAll(AMRController robot)
    {
        foreach (var list in _table.Values)
            list.RemoveAll(r => r.robot == robot);
        _blocked.Remove(robot);
    }

    // ── Deadlock detection helpers ─────────────────────────────────────────

    /// <summary>
    /// Call each frame when a robot is blocked (cannot reserve its next cell).
    /// Returns true when the robot has been blocked long enough to trigger a backup.
    /// </summary>
    public bool ShouldBackUp(AMRController robot)
    {
        if (!_blocked.TryGetValue(robot, out var info))
        {
            _blocked[robot] = new BlockedInfo { blockedSince = Time.time, attempts = 0 };
            return false;
        }

        if (info.attempts >= maxBackupAttempts) return false; // already exhausted attempts
        if (Time.time - info.blockedSince < deadlockTimeout) return false;

        info.attempts++;
        info.blockedSince = Time.time; // reset timer for next potential backup
        return true;
    }

    /// <summary>Call when the robot successfully reserves its next cell (no longer blocked).</summary>
    public void ClearBlocked(AMRController robot) => _blocked.Remove(robot);

    // ── Inner types ────────────────────────────────────────────────────────

    private class Reservation
    {
        public AMRController robot;
        public float timeFrom;
        public float timeTo;
    }

    private class BlockedInfo
    {
        public float blockedSince;
        public int   attempts;
    }
}
