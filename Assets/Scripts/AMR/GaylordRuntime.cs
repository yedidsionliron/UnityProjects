using UnityEngine;

/// <summary>
/// Links a scene Gaylord instance to its database record.
/// </summary>
public class GaylordRuntime : MonoBehaviour
{
    [Tooltip("Unique Gaylord identifier.")]
    public string gaylordId;

    [Tooltip("Current grid label, e.g. 147 or St12.")]
    public string gridLabel;

    [Tooltip("Current 0-based grid location.")]
    public Vector2Int gridCell;
}
