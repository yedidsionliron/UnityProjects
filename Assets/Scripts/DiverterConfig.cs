using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Data and sort-point construction for a divert station.
///
/// Targets a conveyor by finding its PCSConfig component. During Build(),
/// a Diverter component is added to (or retrieved from) that conveyor GameObject.
///
/// PCSConfig interaction (belt sizing) is handled by DiverterConfigEditor,
/// which can see both assemblies. This script only creates SortPoints and
/// places gaylords — no dependency on the PCS assembly.
///
/// User workflow: assign targetConveyorGO (auto-found on Reset), fill dimensions, press Build.
/// </summary>
public class DiverterConfig : MonoBehaviour
{
    [Header("References")]
    public AddressInit addressInit;
    public GameObject  gaylordPrefab;

    [Header("Sort Points")]
    [Tooltip("Number of physical divert positions along the belt.")]
    public int numDivertPoints = 10;

    [Header("Gaylord Dimensions")]
    [Tooltip("Slot pitch along the belt in metres. Belt length = numDivertPoints × gaylordWidth.")]
    public float gaylordWidth   = 1.2f;
    [Tooltip("Gap from the physical belt edge to the gaylord centre in metres.")]
    public float gaylordDepth   = 0.8f;
    [Tooltip("Extra empty space added to each side of a Gaylord along the belt.")]
    public float gaylordBufferPerSide = 0.1f;
    [Tooltip("Legacy side clearance term in metres. The current build places Gaylords flush to the belt edge, so this is ignored.")]
    public float gaylordGap     = 0.1f;

    [Header("Overflow")]
    [Tooltip("Place an extra gaylord at the end of the belt to catch packages that pass all divert points.")]
    public bool addOverflowGaylord = true;

    /// <summary>All sort points created by the last Build(), ordered [L0, R0, L1, R1, ...].</summary>
    [HideInInspector] public SortPoint[] sortPoints;

    /// <summary>Intended physical belt length in metres.</summary>
    public float BeltLength => numDivertPoints * (gaylordWidth + 2f * gaylordBufferPerSide);

#if UNITY_EDITOR
    /// <summary>
    /// Creates SortPoints and places gaylords. Called by DiverterConfigEditor after it has
    /// sized the belt via PCSConfig and measured it via Diverter.MeasureBelt().
    ///
    /// <paramref name="beltHalfX"/>: physical half-width of the belt mesh in world X,
    ///   measured by DiverterConfigEditor from actual belt geometry.
    /// <paramref name="gaylordHalfX"/>: half the measured Gaylord footprint in world X.
    /// <paramref name="effectiveSlotPitch"/>: actual slot spacing in Z — at least the
    ///   gaylord prefab's measured Z footprint so gaylords never overlap each other.
    ///
    /// Returns the Diverter component used (created or existing).
    /// </summary>
    public Diverter BuildSortPoints(Diverter diverter, float beltHalfX, float gaylordHalfX, float effectiveSlotPitch, float groundWorldY)
    {
        ClearSortPoints();
        sortPoints = new SortPoint[numDivertPoints * 2];

        float totalLength = numDivertPoints * effectiveSlotPitch;
        float beltStart   = diverter.beltCenterLocalZ - totalLength / 2f;
        float xOffset     = beltHalfX + gaylordHalfX;

        for (int i = 0; i < numDivertPoints; i++)
        {
            float slotCentreZ = beltStart + (i + 0.5f) * effectiveSlotPitch;

            sortPoints[i * 2]     = CreateSortPoint(i * 2,     DivertSide.Left,  slotCentreZ, -xOffset, diverter, groundWorldY);
            sortPoints[i * 2 + 1] = CreateSortPoint(i * 2 + 1, DivertSide.Right, slotCentreZ,  xOffset, diverter, groundWorldY);
        }

        if (addOverflowGaylord)
            PlaceOverflowGaylord(diverter, effectiveSlotPitch, groundWorldY);

        addressInit.Assign(sortPoints);
        EditorUtility.SetDirty(this);
        return diverter;
    }

    private void ClearSortPoints()
    {
        var existingSortPoints = GetComponentsInChildren<SortPoint>(includeInactive: true);
        for (int i = existingSortPoints.Length - 1; i >= 0; i--)
        {
            var sortPoint = existingSortPoints[i];
            if (sortPoint == null || sortPoint.gameObject == gameObject)
                continue;

            DestroyImmediate(sortPoint.gameObject);
        }
    }

    private SortPoint CreateSortPoint(int id, DivertSide side, float localZ, float localX, Diverter diverter, float groundWorldY)
    {
        GameObject go = new GameObject($"SortPoint_{id}_{side}");
        Undo.RegisterCreatedObjectUndo(go, "Create SortPoint");
        go.transform.SetParent(transform, worldPositionStays: false);
        // Use the diverter's transform for X/Z, but pin world Y to the support bottom
        // so gaylords always stand on the ground regardless of belt elevation.
        Vector3 worldPos = diverter.transform.TransformPoint(new Vector3(localX, 0f, localZ));
        worldPos.y = groundWorldY;
        go.transform.position = worldPos;
        go.transform.rotation = diverter.transform.rotation;

        SortPoint sp = go.AddComponent<SortPoint>();
        sp.id   = id;
        sp.side = side;

        PlaceGaylord(sp);
        return sp;
    }

    private void PlaceGaylord(SortPoint sp)
    {
        GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(gaylordPrefab, sp.transform);
        if (g == null)
        {
            Debug.LogError($"DiverterConfig: PrefabUtility.InstantiatePrefab failed for '{gaylordPrefab.name}'. " +
                           "Make sure it is a saved prefab asset, not a scene object.", this);
            return;
        }

        Undo.RegisterCreatedObjectUndo(g, "Create Gaylord");
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;
        g.transform.localScale = gaylordPrefab.transform.localScale;

        // Measure bounds to align both Z-center and Y-bottom with the SortPoint.
        // SortPoint Y = support ground level, so lift the gaylord until its mesh bottom
        // sits at that same world Y (pivot may not be at the mesh bottom).
        var renderers = g.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float lift         = sp.transform.position.y - b.min.y;
            float zCenterLocal = sp.transform.InverseTransformPoint(b.center).z;
            g.transform.localPosition = new Vector3(0f, lift, -zCenterLocal);
        }

        // GaylordContainer creates colliders at runtime only (via OnEnable when isPlaying).
        // Do not call RebuildColliders here — it would create __GaylordColliders objects
        // in Edit mode without Undo registration, causing them to appear at scene root.
    }

    private void PlaceOverflowGaylord(Diverter diverter, float slotPitch, float groundWorldY)
    {
        // Destroy any previous overflow gaylord so Build() is idempotent.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == "OverflowGaylord")
                DestroyImmediate(child.gameObject);
        }

        // Position: centred on the belt in X, immediately past the physical belt end.
        // Use the measured belt length here rather than the logical row length, because
        // PCS tile rounding can make the conveyor physically longer than the Gaylord row.
        float physicalHalfLength = diverter.measuredBeltLength > 0f
            ? diverter.measuredBeltLength * 0.5f
            : diverter.beltLength * 0.5f;
        float beltEnd = diverter.beltCenterLocalZ + physicalHalfLength + slotPitch * 0.5f;
        Vector3 worldPos = diverter.transform.TransformPoint(new Vector3(0f, 0f, beltEnd));
        worldPos.y = groundWorldY;

        var go = new GameObject("OverflowGaylord");
        Undo.RegisterCreatedObjectUndo(go, "Create Overflow Gaylord");
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.position = worldPos;
        go.transform.rotation = diverter.transform.rotation;

        var g = (GameObject)PrefabUtility.InstantiatePrefab(gaylordPrefab, go.transform);
        if (g == null) return;
        Undo.RegisterCreatedObjectUndo(g, "Create Overflow Gaylord Mesh");

        g.transform.localScale = gaylordPrefab.transform.localScale;
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;

        // Align mesh bottom to ground, centre in Z.
        var renderers = g.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float lift        = go.transform.position.y - b.min.y;
            float zCenterLocal = go.transform.InverseTransformPoint(b.center).z;
            g.transform.localPosition = new Vector3(0f, lift, -zCenterLocal);
        }
    }
#endif
}
