using UnityEngine;

/// <summary>
/// Owns the global address space used by both routing and spawning.
///
/// Edit time: DiverterConfig.Build() calls Assign() which bakes address ranges
/// into each SortPoint's serialized fields — ranges survive play-mode entry.
///
/// Runtime: Awake() re-exposes TotalAddressSpace as a static so BoxSpawner
/// can stamp each package without needing a direct reference.
/// </summary>
public class AddressInit : MonoBehaviour
{
    [Tooltip("Total number of addresses. Packages receive a random address in [1, TotalAddressSpace].")]
    public int totalAddressSpace = 1000;

    /// <summary>Read by BoxSpawner at runtime. Set in Awake() from the serialized field.</summary>
    public static int TotalAddressSpace { get; private set; }

    private void Awake() => TotalAddressSpace = totalAddressSpace;

    /// <summary>
    /// Distributes [1..totalAddressSpace] uniformly across all sort points.
    /// Ranges are contiguous and non-overlapping.
    /// Called by DiverterConfig.Build() at edit time.
    /// </summary>
    public void Assign(SortPoint[] sortPoints)
    {
        TotalAddressSpace = totalAddressSpace;

        if (sortPoints == null || sortPoints.Length == 0)
        {
            Debug.LogWarning("AddressInit: no sort points to assign.", this);
            return;
        }

        int n = sortPoints.Length;
        for (int i = 0; i < n; i++)
        {
            sortPoints[i].addressMin = Mathf.RoundToInt((float)totalAddressSpace * i / n) + 1;
            sortPoints[i].addressMax = Mathf.RoundToInt((float)totalAddressSpace * (i + 1) / n);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(sortPoints[i]);
#endif
        }

        Debug.Log($"AddressInit: assigned ranges across {n} sort points (space=1–{totalAddressSpace}).", this);
    }
}
