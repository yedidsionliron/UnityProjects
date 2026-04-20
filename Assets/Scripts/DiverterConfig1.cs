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
    [Tooltip("Minimum air gap between the belt frame outer edge and the nearest gaylord face (metres).")]
    public float gaylordGap     = 0.1f;

    [Header("Overflow")]
    [Tooltip("Place an extra gaylord at the end of the belt to catch packages that pass all divert points.")]
    public bool addOverflowGaylord = true;

    /// <summary>All sort points created by the last Build(), ordered [L0, R0, L1, R1, ...].</summary>
    [HideInInspector] public SortPoint[] sortPoints;

    /// <summary>Intended physical belt length in metres.</summary>
    public float BeltLength => numDivertPoints * gaylordWidth;

#if UNITY_EDITOR
    /// <summary>
    /// Creates SortPoints and places gaylords. Called by DiverterConfigEditor after it has
    /// sized the belt via PCSConfig and measured it via Diverter.MeasureBelt().
    ///
    /// <paramref name="beltHalfX"/>: physical half-width of the belt mesh in world X,
    ///   measured by DiverterConfigEditor from actual renderer bounds.
    /// <paramref name="effectiveSlotPitch"/>: actual slot spacing in Z — at least the
    ///   gaylord prefab's measured Z footprint so gaylords never overlap each other.
    ///
    /// Returns the Diverter component used (created or existing).
    /// </summary>
    public Diverter BuildSortPoints(Diverter diverter, float beltHalfX, float effectiveSlotPitch, float gaylordZScale, float groundWorldY)
    {
        ClearSortPoints();
        sortPoints = new SortPoint[numDivertPoints * 2];

        float totalLength = numDivertPoints * effectiveSlotPitch;
        float beltStart   = diverter.beltCenterLocalZ - totalLength / 2f;
        float xOffset     = beltHalfX + gaylordGap + gaylordDepth;

        for (int i = 0; i < numDivertPoints; i++)
        {
            float slotCentreZ = beltStart + (i + 0.5f) * effectiveSlotPitch;

            sortPoints[i * 2]     = CreateSortPoint(i * 2,     DivertSide.Left,  slotCentreZ, -xOffset, diverter, gaylordZScale, groundWorldY);
            sortPoints[i * 2 + 1] = CreateSortPoint(i * 2 + 1, DivertSide.Right, slotCentreZ,  xOffset, diverter, gaylordZScale, groundWorldY);
        }

        if (addOverflowGaylord)
            PlaceOverflowGaylord(diverter, effectiveSlotPitch, gaylordZScale, groundWorldY);

        addressInit.Assign(sortPoints);
        EditorUtility.SetDirty(this);
        return diverter;
    }

    private void ClearSortPoints()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child.GetComponent<SortPoint>() != null)
                DestroyImmediate(child);
        }
    }

    private SortPoint CreateSortPoint(int id, DivertSide side, float localZ, float localX, Diverter diverter, float gaylordZScale, float groundWorldY)
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

        PlaceGaylord(sp, gaylordZScale);
        return sp;
    }

    private void PlaceGaylord(SortPoint sp, float zScale)
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
        // Preserve prefab X/Y scale; stretch only Z to fill the slot.
        Vector3 ps = gaylordPrefab.transform.localScale;
        g.transform.localScale = new Vector3(ps.x, ps.y, ps.z * zScale);

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

    private void PlaceOverflowGaylord(Diverter diverter, float slotPitch, float zScale, float groundWorldY)
    {
        // Destroy any previous overflow gaylord so Build() is idempotent.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == "OverflowGaylord")
                DestroyImmediate(child.gameObject);
        }

        // Position: centred on the belt in X, one slot-pitch past the last divert point.
        float beltEnd = diverter.beltCenterLocalZ + diverter.beltLength / 2f + slotPitch * 0.5f;
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

        Vector3 ps = gaylordPrefab.transform.localScale;
        g.transform.localScale = new Vector3(ps.x, ps.y, ps.z * zScale);
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
