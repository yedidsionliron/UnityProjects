using UnityEngine;

/// <summary>
/// Centralised configuration for the AMR station simulation.
/// Create via Assets > Create > AMR > Station Config.
/// </summary>
[CreateAssetMenu(menuName = "AMR/Station Config", fileName = "AMRStationConfig")]
public class AMRStationConfig : ScriptableObject
{
    [Header("Robot Movement")]
    [Tooltip("Metres per second when carrying a gaylord")]
    public float loadedSpeed  = 1.0f;

    [Tooltip("Metres per second when travelling empty")]
    public float emptySpeed   = 1.5f;

    [Header("Gaylord Carry")]
    [Tooltip("Height the gaylord is lifted above its resting position (metres)")]
    public float liftHeight   = 0.03f;

    [Tooltip("Duration of the lift/lower animation (seconds)")]
    public float liftDuration = 0.3f;

    [Header("Conflict Avoidance")]
    [Tooltip("Seconds a robot waits at a blocked cell before replanning")]
    public float reservationTimeout  = 2.0f;

    [Tooltip("Seconds two robots must be mutually blocked before one backs up")]
    public float deadlockTimeout     = 3.0f;

    [Tooltip("Maximum number of backup-and-replan attempts before escalating")]
    public int   maxBackupAttempts   = 3;

    [Tooltip("Minimum seconds between successive path replans for the same robot")]
    public float replanCooldown      = 0.5f;

    [Header("Facility")]
    [Tooltip("Number of standby sort chutes kept stocked with empty gaylords")]
    public int   standbyChutes       = 2;

    [Tooltip("Number of charging bays in the charging area")]
    public int   chargingBays        = 4;
}
