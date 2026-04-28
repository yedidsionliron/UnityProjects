using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>State machine states for an AMR robot.</summary>
public enum AMRState
{
    Idle,
    NavigateToPickup,
    LiftGaylord,
    NavigateToDestination,
    LowerGaylord,
    Charging,
}

/// <summary>
/// Controls a single AMR robot.
///
/// Usage:
///   Call AssignTask() to give the robot a task.
///   The robot executes autonomously: navigates → lifts → navigates → lowers → Idle.
///   Subscribe to OnTaskCompleted to be notified when the robot is free.
///
/// Carry mechanics:
///   The gaylord's world position is driven to the robot's carry point each frame.
///   The gaylord's rotation is never changed — it stays world-aligned.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class AMRController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("References")]
    public GridMap          gridMap;
    public ReservationTable reservationTable;
    public AMRStationConfig config;

    [Header("Carry")]
    [Tooltip("Optional local-space adjustment applied after the carry position is auto-derived from the picked up gaylord.")]
    public Vector3 carryOffset = Vector3.zero;

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action<AMRController, AMRTask> OnTaskCompleted;

    // ── State (read-only from outside) ─────────────────────────────────────
    public AMRState      State         { get; private set; } = AMRState.Idle;
    public AMRTask       CurrentTask   { get; private set; }
    public Vector2Int    CurrentCell   { get; private set; }
    public GameObject    CarriedGaylord { get; private set; }

    // ── Private ────────────────────────────────────────────────────────────
    private List<Vector2Int> _path;
    private int              _pathIndex;
    private bool             _isMoving;
    private float            _lastReplanTime = -999f;
    private Vector3          _activeCarryOffset;
    private bool             _hasActiveCarryOffset;

    private Rigidbody _rb;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true; // AMR moves via transform, not physics forces
        CurrentCell = gridMap != null ? gridMap.WorldToCell(transform.position) : Vector2Int.zero;
    }

    void Update()
    {
        if (CarriedGaylord != null && State == AMRState.NavigateToDestination)
            DriveGaylordPosition();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Assign a new task. Robot must be Idle.</summary>
    public void AssignTask(AMRTask task)
    {
        if (State != AMRState.Idle)
        {
            Debug.LogWarning($"[AMR {name}] AssignTask called while not Idle (state={State}). Ignored.", this);
            return;
        }
        CurrentTask = task;
        _hasActiveCarryOffset = false;

        if (task.taskType == TaskType.Charge)
        {
            State = AMRState.Charging;
            // ChargingArea will call NotifyChargingComplete when a bay is ready
            return;
        }

        State = AMRState.NavigateToPickup;
        StartCoroutine(ExecuteTask());
    }

    /// <summary>Called by ChargingArea when a bay is assigned and the robot has arrived.</summary>
    public void NotifyChargingComplete()
    {
        State = AMRState.Idle;
        OnTaskCompleted?.Invoke(this, CurrentTask);
    }

    // ── Task execution coroutine ───────────────────────────────────────────

    private IEnumerator ExecuteTask()
    {
        // 1. Navigate (empty) to origin
        yield return NavigateTo(CurrentTask.originLabel, loaded: false);
        if (State == AMRState.Idle) yield break; // aborted

        // 2. Lift gaylord
        State = AMRState.LiftGaylord;
        yield return LiftGaylord();

        // 3. Navigate (loaded) to destination
        State = AMRState.NavigateToDestination;
        yield return NavigateTo(CurrentTask.destLabel, loaded: true);
        if (State == AMRState.Idle) yield break;

        // 4. Lower gaylord
        State = AMRState.LowerGaylord;
        yield return LowerGaylord();

        // 5. Done
        State = AMRState.Idle;
        var completedTask = CurrentTask;
        reservationTable?.ReleaseAll(this);
        OnTaskCompleted?.Invoke(this, completedTask);
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private IEnumerator NavigateTo(string label, bool loaded)
    {
        if (!gridMap.gridData.TryResolveLabel(label, out Vector2Int goal))
        {
            Debug.LogError($"[AMR {name}] Cannot resolve label '{label}'.", this);
            State = AMRState.Idle;
            yield break;
        }

        int replanAttempts = 0;
        const int maxReplans = 10;

        while (CurrentCell != goal)
        {
            // Plan path
            _path = Pathfinder.Find(CurrentCell, goal, gridMap, loaded);
            if (_path == null || _path.Count == 0)
            {
                Debug.LogWarning($"[AMR {name}] No path from {CurrentCell} to {goal} (loaded={loaded}). Waiting...", this);
                yield return new WaitForSeconds(config.replanCooldown);
                replanAttempts++;
                if (replanAttempts >= maxReplans) { State = AMRState.Idle; yield break; }
                continue;
            }

            _pathIndex = 0;
            replanAttempts = 0;

            // Walk the path one cell at a time
            while (_pathIndex < _path.Count)
            {
                Vector2Int nextCell = _path[_pathIndex];
                float      speed    = loaded ? config.loadedSpeed : config.emptySpeed;
                float      eta      = Vector3.Distance(transform.position, gridMap.CellToWorld(nextCell)) / speed;
                float      now      = Time.time;

                // Attempt to reserve the next cell
                bool reserved = reservationTable != null
                    ? reservationTable.TryReserve(nextCell, this, now, now + eta + 0.5f)
                    : true;

                if (!reserved)
                {
                    // Cell is taken — check deadlock
                    if (reservationTable != null && reservationTable.ShouldBackUp(this))
                    {
                        yield return BackUpOneCell(loaded);
                        break; // replan from new position
                    }
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                reservationTable?.ClearBlocked(this);

                // Move to next cell
                yield return MoveToCell(nextCell, speed);

                // Release the cell we just left
                reservationTable?.Release(CurrentCell, this);
                gridMap.Release(CurrentCell, this);

                CurrentCell = nextCell;
                gridMap.TryReserve(nextCell, this); // mark as physically occupied

                _pathIndex++;
            }
        }
    }

    private IEnumerator MoveToCell(Vector2Int targetCell, float speed)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos   = gridMap.CellToWorld(targetCell);
        endPos.y = startPos.y; // keep current height

        // Face the direction of movement
        Vector3 dir = (endPos - startPos);
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float distance = Vector3.Distance(startPos, endPos);
        float duration = distance / Mathf.Max(0.01f, speed);
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, endPos, elapsed / duration);
            yield return null;
        }

        transform.position = endPos;
    }

    private IEnumerator BackUpOneCell(bool loaded)
    {
        // Find any valid adjacent cell we can retreat to
        Vector2Int[] dirs = { new Vector2Int(-1,0), new Vector2Int(1,0),
                              new Vector2Int(0,-1), new Vector2Int(0,1) };
        var current = gridMap.gridData.GetCell(CurrentCell);

        foreach (var d in dirs)
        {
            Vector2Int candidate = CurrentCell + d;
            if (!gridMap.IsTraversable(candidate, loaded)) continue;
            if (current != null && !current.AllowsExit(d)) continue;
            float speed = loaded ? config.loadedSpeed : config.emptySpeed;
            yield return MoveToCell(candidate, speed);
            gridMap.Release(CurrentCell, this);
            CurrentCell = candidate;
            yield break;
        }
    }

    // ── Lift / Lower ───────────────────────────────────────────────────────

    private IEnumerator LiftGaylord()
    {
        // Find the gaylord at the current cell
        var cellState = gridMap.GetState(CurrentCell);
        if (cellState == null || cellState.gaylord == null)
        {
            Debug.LogWarning($"[AMR {name}] No gaylord at {CurrentCell} to lift.", this);
            yield break;
        }

        CarriedGaylord = cellState.gaylord;
        gridMap.SetOccupied(CurrentCell, null); // cell is now free (gaylord in the air)

        // Animate: raise gaylord from rest position to carry height
        Vector3 startPos = CarriedGaylord.transform.position;
        Vector3 endPos   = startPos + Vector3.up * config.liftHeight;
        yield return AnimateGaylordVertical(startPos, endPos, config.liftDuration);
        CacheCarryOffset();
    }

    private IEnumerator LowerGaylord()
    {
        if (CarriedGaylord == null) yield break;

        // Animate: lower to rest position at destination cell
        Vector3 startPos = CarriedGaylord.transform.position;
        Vector3 endPos   = startPos - Vector3.up * config.liftHeight;
        yield return AnimateGaylordVertical(startPos, endPos, config.liftDuration);

        // Place on destination cell
        gridMap.SetOccupied(CurrentCell, CarriedGaylord);
        CarriedGaylord = null;
        _hasActiveCarryOffset = false;
    }

    private IEnumerator AnimateGaylordVertical(Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (CarriedGaylord != null)
                CarriedGaylord.transform.position = Vector3.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        if (CarriedGaylord != null)
            CarriedGaylord.transform.position = to;
    }

    // ── Gaylord carry (Update) ─────────────────────────────────────────────

    private void DriveGaylordPosition()
    {
        // Drive position only — rotation is intentionally left unchanged
        Vector3 localCarryOffset = _hasActiveCarryOffset ? _activeCarryOffset : carryOffset;
        Vector3 worldCarryPoint = transform.TransformPoint(localCarryOffset);
        CarriedGaylord.transform.position = worldCarryPoint;
    }

    private void CacheCarryOffset()
    {
        if (CarriedGaylord == null)
        {
            _hasActiveCarryOffset = false;
            return;
        }

        // Capture the actual post-lift relationship so carry alignment follows
        // the current Gaylord scale/pivot instead of relying on a magic constant.
        _activeCarryOffset = transform.InverseTransformPoint(CarriedGaylord.transform.position) + carryOffset;
        _hasActiveCarryOffset = true;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (_path == null || gridMap == null) return;
        Gizmos.color = Color.cyan;
        for (int i = _pathIndex; i < _path.Count - 1; i++)
            Gizmos.DrawLine(
                gridMap.CellToWorld(_path[i])   + Vector3.up * 0.1f,
                gridMap.CellToWorld(_path[i+1]) + Vector3.up * 0.1f);
    }
}
