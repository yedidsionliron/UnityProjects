using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a configurable number of charging bays.
/// Robots that have no pending task are directed here.
///
/// Placement: the StationGridBuilder places this object adjacent to the staging area
/// (right of col 24). One child GameObject per bay, named "Bay_0", "Bay_1", etc.
/// </summary>
public class ChargingArea : MonoBehaviour
{
    [Tooltip("Must match AMRStationConfig.chargingBays. Set by StationGridBuilder.")]
    public int bayCount = 4;

    [Tooltip("World-space positions of individual bays. Populated by StationGridBuilder.")]
    public List<Transform> bayTransforms = new List<Transform>();

    // ── Runtime state ──────────────────────────────────────────────────────
    private AMRController[] _occupants; // null = bay is free

    void Awake()
    {
        _occupants = new AMRController[Mathf.Max(bayCount, bayTransforms.Count)];
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Request a charging bay for <paramref name="robot"/>.
    /// If a bay is available, starts a coroutine to navigate the robot there
    /// and calls <see cref="AMRController.NotifyChargingComplete"/> on arrival.
    /// Returns true if a bay was assigned, false if all bays are full.
    /// </summary>
    public bool RequestBay(AMRController robot)
    {
        for (int i = 0; i < _occupants.Length; i++)
        {
            if (_occupants[i] != null) continue;
            if (i >= bayTransforms.Count) continue;

            _occupants[i] = robot;
            StartCoroutine(NavigateRobotToBay(robot, i));
            return true;
        }
        return false;
    }

    /// <summary>Release the bay held by <paramref name="robot"/>.</summary>
    public void ReleaseBay(AMRController robot)
    {
        for (int i = 0; i < _occupants.Length; i++)
            if (_occupants[i] == robot) { _occupants[i] = null; return; }
    }

    public bool HasFreeBay()
    {
        for (int i = 0; i < _occupants.Length; i++)
            if (_occupants[i] == null && i < bayTransforms.Count) return true;
        return false;
    }

    // ── Private ────────────────────────────────────────────────────────────

    private System.Collections.IEnumerator NavigateRobotToBay(AMRController robot, int bayIndex)
    {
        Transform bay = bayTransforms[bayIndex];
        Vector3   goal = bay.position;
        float     speed = robot.config != null ? robot.config.emptySpeed : 1.5f;

        // Simple lerp to bay position (no grid pathfinding — bays are outside the grid)
        while (Vector3.Distance(robot.transform.position, goal) > 0.02f)
        {
            robot.transform.position = Vector3.MoveTowards(
                robot.transform.position, goal, speed * Time.deltaTime);
            yield return null;
        }

        robot.transform.position = goal;
        robot.NotifyChargingComplete();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.5f);
        foreach (var t in bayTransforms)
            if (t != null) Gizmos.DrawCube(t.position, Vector3.one * 0.4f);
    }
#endif
}
