using UnityEngine;

/// <summary>
/// Persistent settings for the StationGridBuilder EditorWindow.
/// Create via Assets > Create > AMR > Station Builder Settings,
/// or let the EditorWindow auto-create it at the default path.
/// </summary>
[CreateAssetMenu(menuName = "AMR/Station Builder Settings", fileName = "StationBuilderSettings")]
public class StationBuilderSettings : ScriptableObject
{
    [Header("Gaylord Reference")]
    [Tooltip("Prefab used to measure the physical Gaylord footprint.")]
    public GameObject gaylordPrefab;

    [Header("Cell Size")]
    [Tooltip("Extra clearance added to each side of the Gaylord footprint (metres). " +
             "Total padding per axis = 2 × bufferPerSide. Default: 0.10 m (10 cm per side).")]
    public float bufferPerSide = 0.1f;

    [Tooltip("When true, ignores the measured Gaylord size and uses the manual values below.")]
    public bool overrideCellSize = false;

    [Tooltip("Manual cell width (X axis) in metres. Only used when overrideCellSize is true.")]
    public float manualCellWidth = 1.4f;

    [Tooltip("Manual cell depth (Z axis) in metres. Only used when overrideCellSize is true.")]
    public float manualCellDepth = 1.2f;
}
