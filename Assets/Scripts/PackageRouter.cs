using UnityEngine;

/// <summary>
/// Assigns a routing address to every box spawned by BoxSpawner, then spawns Gaylord
/// containers on both sides of each Diverter.
///
/// Address scheme: addresses are 1-indexed and wrap via modulo in DivertZone, so every
/// package is routed regardless of totalLanes. Range is [1, totalLanes].
///
/// Gaylord placement derives all geometry from the gaylordPrefab itself — no magic
/// scale constants in code. If the prefab pivot is not at the visual base-center, a one-time
/// correction is computed from the mesh bounds and stored in _yGroundCorrection / _zPivotNorm.
/// Right-click this component → "Log Gaylord Pivot Values" to see what child localPosition
/// to bake into the prefab to eliminate those corrections permanently.
///
/// Attach to the same GameObject as BoxSpawner.
/// </summary>
[RequireComponent(typeof(BoxSpawner))]
public class PackageRouter : MonoBehaviour
{
    [Tooltip("All diverters in order along the belt. Offsets are calculated automatically.")]
    public Diverter[] diverters;

    [Header("Gaylords")]
    [Tooltip("Gaylord prefab. Its root localScale sets the base size (X and Y are preserved; " +
             "Z is stretched per slot). For zero pivot corrections, the mesh child's pivot " +
             "should sit at the visual base-center. Run the context menu to see the required offsets.")]
    public GameObject gaylordPrefab;

    [Tooltip("Gap between belt edge and nearest face of the Gaylord (metres).")]
    public float gaylordGap = 0.1f;

    [Tooltip("World-space Y of the Gaylord's visual base (0 = ground).")]
    public float gaylordYOffset = 0f;

    // ------------------------------------------------------------------ //
    //  Runtime state
    // ------------------------------------------------------------------ //

    private BoxSpawner _spawner;
    private int        _totalLanes;

    // Derived from gaylordPrefab mesh once in SpawnGaylords.
    // Both become 0 when the prefab pivot is correctly placed at the visual base-center.
    private float _yGroundCorrection; // metres to raise pivot so visual base sits at gaylordYOffset
    private float _zPivotNorm;        // normalised Z offset of mesh centre from pivot (0 = centred)

    // ------------------------------------------------------------------ //

    private void Awake()
    {
        _spawner = GetComponent<BoxSpawner>();
    }

    private void Start()
    {
        if (diverters == null || diverters.Length == 0)
        {
            Debug.LogWarning("PackageRouter: diverters array is empty — nothing to set up.", this);
            _spawner.OnBoxSpawned += OnBoxSpawned;
            return;
        }

        // Assign each diverter its address offset and propagate totalLanes.
        int offset = 0;
        foreach (Diverter d in diverters)
        {
            if (d == null) { Debug.LogWarning("PackageRouter: null entry in diverters array — skipping.", this); continue; }
            d.addressOffset = offset;
            offset += d.numDivertPoints * 2;
        }
        _totalLanes = offset;

        foreach (Diverter d in diverters)
        {
            if (d == null) continue;
            d.totalLanes = _totalLanes;
        }

        if (_totalLanes == 0)
            Debug.LogWarning("PackageRouter: all diverters have 0 divert points.", this);

        for (int i = 0; i < diverters.Length; i++)
        {
            if (diverters[i] == null) continue;
            Debug.Log($"PackageRouter: Diverter[{i}] '{diverters[i].name}' " +
                      $"offset={diverters[i].addressOffset} lanes={diverters[i].numDivertPoints * 2} " +
                      $"totalLanes={diverters[i].totalLanes}", diverters[i]);
        }

        _spawner.OnBoxSpawned += OnBoxSpawned;

        SpawnGaylords();
    }

    // ------------------------------------------------------------------ //
    //  Gaylord placement
    // ------------------------------------------------------------------ //

    private void SpawnGaylords()
    {
        if (gaylordPrefab == null)
        {
            Debug.LogWarning("PackageRouter: gaylordPrefab not assigned — Gaylords will not spawn.", this);
            return;
        }

        // Derive all sizing from the prefab itself — no hardcoded scale constants.
        MeshFilter mf = gaylordPrefab.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("PackageRouter: gaylordPrefab has no MeshFilter — cannot compute Gaylord footprint.", this);
            return;
        }

        Mesh m = mf.sharedMesh;

        // The prefab's root scale sets the visual size. Typically (200, 100, 200) baked in
        // as a prefab override. Z will be overridden per-slot; X and Y are preserved.
        Vector3 prefabScale = gaylordPrefab.transform.localScale;

        float naturalLength = m.bounds.size.z * prefabScale.z;
        float naturalDepth  = m.bounds.size.x * prefabScale.x;

        // --- Pivot corrections (become 0 once the prefab pivot is at the visual base-center) ---
        // Y: how far the pivot sits above the visual base (bounds.min.y < 0 means pivot is above base)
        _yGroundCorrection = -m.bounds.min.y * prefabScale.y;

        // Z: normalised offset of mesh centre from pivot (0 = pivot is at mesh centre)
        _zPivotNorm = m.bounds.size.z > 0f ? m.bounds.center.z / m.bounds.size.z : 0f;

        if (Mathf.Abs(_yGroundCorrection) > 0.001f || Mathf.Abs(_zPivotNorm) > 0.001f)
            Debug.Log($"PackageRouter: Gaylord pivot corrections active — " +
                      $"yGroundCorrection={_yGroundCorrection:F3} m  zPivotNorm={_zPivotNorm:F3}. " +
                      "Right-click this component → 'Log Gaylord Pivot Values' to see how to eliminate these.", this);

        int total = 0;

        foreach (Diverter d in diverters)
        {
            if (d == null) continue;

            int n = d.numDivertPoints;
            if (n <= 0)
            {
                Debug.LogWarning($"PackageRouter: Diverter '{d.name}' has numDivertPoints=0, skipping.", this);
                continue;
            }

            float sliceZ  = d.beltLength / n;
            float zScale  = naturalLength > 0f ? sliceZ / naturalLength : 1f;
            float xOffset = d.triggerWidth / 2f + gaylordGap + naturalDepth / 2f;

            float beltStartZ = d.beltCenterLocalZ - d.beltLength / 2f;

            for (int i = 0; i < n; i++)
            {
                // Place pivot so the visual centre of the mesh lands at the slot centre.
                // _zPivotNorm * sliceZ corrects for the mesh centre not being at the pivot.
                float localZ = beltStartZ + (i + 0.5f) * sliceZ - _zPivotNorm * sliceZ;

                PlaceGaylord(d, i, new Vector3(-xOffset, 0f, localZ), zScale, prefabScale, "L");
                PlaceGaylord(d, i, new Vector3( xOffset, 0f, localZ), zScale, prefabScale, "R");
                total += 2;
            }
        }

        Debug.Log($"PackageRouter: spawned {total} Gaylords.", this);
    }

    private void PlaceGaylord(Diverter d, int index, Vector3 localPos, float zScale, Vector3 prefabScale, string side)
    {
        Vector3 worldPos = d.transform.TransformPoint(localPos);
        // Y is always absolute: pivot raised by _yGroundCorrection so the visual base sits at gaylordYOffset.
        // (Belt is flat so TransformPoint's Y contribution is discarded intentionally.)
        worldPos.y = gaylordYOffset + _yGroundCorrection;

        GameObject g = Instantiate(gaylordPrefab);
        g.name = $"Gaylord_{d.name}_{index}_{side}";
        g.transform.SetParent(d.transform, worldPositionStays: true);
        g.transform.position = worldPos;
        g.transform.rotation = d.transform.rotation;
        // Preserve prefab X/Y scale; stretch only Z to fill the diverter slot.
        g.transform.localScale = new Vector3(prefabScale.x, prefabScale.y, prefabScale.z * zScale);

        // RebuildColliders must run AFTER scale is set — GaylordContainer.OnEnable fires during
        // Instantiate (before scale is applied), so the colliders would be sized for the default
        // prefab scale. Rebuilding here ensures they match the final stretched Z scale.
        var gc = g.GetComponent<GaylordContainer>();
        if (gc != null) gc.RebuildColliders();
    }

    // ------------------------------------------------------------------ //

    private void OnBoxSpawned(GameObject box)
    {
        Package pkg = box.GetComponent<Package>();
        if (pkg == null) pkg = box.AddComponent<Package>();
        // Addresses are 1-indexed: DivertZone maps via (address - 1) % totalLanes.
        // Range [1, totalLanes] is the exact valid set; all produce unique lane assignments.
        pkg.address = _totalLanes > 0 ? Random.Range(1, _totalLanes + 1) : 1;
    }

    // ------------------------------------------------------------------ //
    //  Editor utility
    // ------------------------------------------------------------------ //

#if UNITY_EDITOR
    /// <summary>
    /// Logs the localPosition a mesh child would need inside the Gaylord prefab to
    /// place its pivot at the visual base-centre, eliminating _yGroundCorrection and _zPivotNorm.
    ///
    /// How to apply:
    ///   1. Open Assets/LastMileAssets/Gaylord.prefab in Prefab Mode.
    ///   2. Create an empty child GameObject named "GaylordMesh".
    ///   3. Move MeshFilter, MeshRenderer to that child.
    ///   4. Set the child's localPosition to the Y and Z values logged below.
    ///   5. Set the child's localScale to the prefab's current root scale (e.g. 200,100,200).
    ///   6. Set the root's localScale to (1,1,1).
    ///   After this, _yGroundCorrection and _zPivotNorm will both be 0.
    /// </summary>
    [ContextMenu("Log Gaylord Pivot Values")]
    private void LogGaylordPivotValues()
    {
        if (gaylordPrefab == null) { Debug.LogError("Assign gaylordPrefab first.", this); return; }

        MeshFilter mf = gaylordPrefab.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("Prefab has no MeshFilter.", this); return; }

        Mesh m = mf.sharedMesh;
        Vector3 ps = gaylordPrefab.transform.localScale;

        float yFix = -m.bounds.min.y   * ps.y;  // raise child so visual base = 0
        float zFix = -m.bounds.center.z * ps.z;  // centre child in Z so pivot = mesh centre

        Debug.Log(
            $"[GaylordPivotFix] To eliminate pivot corrections, set GaylordMesh child:\n" +
            $"  localPosition = (0, {yFix:F4}, {zFix:F4})\n" +
            $"  localScale    = ({ps.x}, {ps.y}, {ps.z})\n" +
            $"  Root scale    → (1, 1, 1)\n" +
            $"Current corrections: yGroundCorrection={yFix:F4}  zPivotNorm={_zPivotNorm:F4}", this);
    }
#endif
}
