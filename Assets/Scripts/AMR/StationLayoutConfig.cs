using UnityEngine;

/// <summary>
/// Shared defaults for station layout and grid-building tools.
/// </summary>
[CreateAssetMenu(menuName = "AMR/Station Layout Config", fileName = "StationLayoutConfig")]
public class StationLayoutConfig : ScriptableObject
{
    [Header("Grid Defaults")]
    [Tooltip("Extra clearance added to each side of the Gaylord footprint when computing station cell size.")]
    public float cellBuffer = 0.2f;
}
