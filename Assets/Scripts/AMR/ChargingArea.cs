using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a configurable number of charging bays.
/// Robots that have no pending task are directed here.
///
/// Placement: the StationGridBuilder places this object adjacent to the staging area.
/// One child GameObject per bay, named "Bay_0", "Bay_1", etc.
/// </summary>
public class ChargingArea : MonoBehaviour
{
    [Tooltip("Must match AMRStationConfig.chargingBays. Set by StationGridBuilder.")]
    public int bayCount = 4;

    [Tooltip("World-space positions of individual bays. Populated by StationGridBuilder.")]
    public List<Transform> bayTransforms = new List<Transform>();

    private AMRController[] _occupants; // null = bay is free
    private GridMap _gridMap;

    void Awake()
    {
        _gridMap = GetComponentInParent<GridMap>();
        _occupants = new AMRController[Mathf.Max(bayCount, bayTransforms.Count)];
    }

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

    private System.Collections.IEnumerator NavigateRobotToBay(AMRController robot, int bayIndex)
    {
        Transform bay = bayTransforms[bayIndex];
        Vector3 goal = bay.position;
        float speed = robot.config != null ? robot.config.emptySpeed : 1.5f;
        float arrivalThreshold = GetArrivalThreshold();

        // Bays live outside the logical grid, so this final approach is world-space only.
        while (Vector3.Distance(robot.transform.position, goal) > arrivalThreshold)
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
        float gizmoSize = GetBayGizmoSize();
        foreach (var t in bayTransforms)
            if (t != null) Gizmos.DrawCube(t.position, Vector3.one * gizmoSize);
    }
#endif

    private float GetArrivalThreshold()
    {
        if (_gridMap == null) return 0.02f;
        return Mathf.Max(0.02f, _gridMap.MinCellDimension * 0.05f);
    }

    private float GetBayGizmoSize()
    {
        if (_gridMap == null) return 0.4f;
        return Mathf.Max(0.4f, _gridMap.MinCellDimension * 0.35f);
    }
}
