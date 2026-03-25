using UnityEngine;

/// <summary>
/// Place on a conveyor belt GameObject (the root, not PaletArrow).
/// Creates X trigger zones along the belt. Each zone can eject a package
/// left or right based on its address, giving 2*X destination lanes.
///
/// Address → lane mapping:
///   address 0 → point 0, LEFT
///   address 1 → point 0, RIGHT
///   address 2 → point 1, LEFT
///   address 3 → point 1, RIGHT
///   ...
///   address 2i+0 → point i, LEFT
///   address 2i+1 → point i, RIGHT
///
/// Packages whose address >= numDivertPoints*2 pass through unaffected.
/// </summary>
public class Diverter : MonoBehaviour
{
    [Header("Sortation")]
    [Tooltip("Number of divert points (X). Gives 2*X destination lanes.")]
    public int numDivertPoints = 2;

    [HideInInspector] public int addressOffset = 0;
    [HideInInspector] public int totalLanes = 1;
    [HideInInspector] public float beltCenterLocalZ = 0f;

    [Header("Physics")]
    [Tooltip("Target lateral speed the divert surface drives the box toward (m/s).")]
    public float divertSpeed = 2f;

    [Tooltip("Friction coefficient between divert surface and box (μ). Same formula as ConveyorBelt: F = μ × mass × g.")]
    public float frictionCoefficient = 0.7f;

    [Header("Geometry")]
    [Tooltip("Length of the conveyor belt in local Z (used to space divert points evenly).")]
    public float beltLength = 6f;

    // triggerY is computed at runtime from the belt mesh — not a constant.
    private float triggerY;

    [Tooltip("Trigger zone width in X — should match belt width (~1.5 m).")]
    public float triggerWidth = 1.4f;

    [Tooltip("Trigger zone depth in Z — narrow enough that zones don't overlap.")]
    public float triggerDepth = 0.6f;

    [Tooltip("Trigger zone height in Y.")]
    public float triggerHeight = 1.2f;

    private void Start()
    {
        MeasureBeltLength();
        BuildZones();
    }

    private void MeasureBeltLength()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        // Accumulate world-space bounds of all renderers on this belt.
        Bounds wb = renderers[0].bounds;
        foreach (var r in renderers) wb.Encapsulate(r.bounds);

        // Project the world bounds half-extents onto this transform's local Z axis
        // to get the true belt length regardless of rotation or scale.
        Vector3 localZ = transform.forward;
        float halfLen = Mathf.Abs(Vector3.Dot(wb.extents,
            new Vector3(Mathf.Abs(localZ.x), Mathf.Abs(localZ.y), Mathf.Abs(localZ.z))));
        float measured = halfLen * 2f;

        // Record where the belt centre sits in this transform's local Z space.
        beltCenterLocalZ = transform.InverseTransformPoint(wb.center).z;

        // Derive triggerY from the belt's actual top surface in local space,
        // so trigger zones always sit above the belt regardless of scale or position.
        float worldSurfaceY = wb.max.y;
        float localSurfaceY = transform.InverseTransformPoint(new Vector3(wb.center.x, worldSurfaceY, wb.center.z)).y;
        triggerY = localSurfaceY + triggerHeight * 0.5f;

        Debug.Log($"Diverter '{name}': beltLength={measured:F3} (was {beltLength:F3})  beltCenterLocalZ={beltCenterLocalZ:F3}  triggerY={triggerY:F3}", this);
        beltLength = measured;
    }

    /// <summary>
    /// Z-distance a package travels along the belt while its centre of mass
    /// crosses from belt centre to belt edge.
    /// dz = vz * sqrt( (triggerWidth/2) / (frictionCoefficient * g) )
    /// </summary>
    public float ExitOffset()
    {
        var belt = GetComponentInChildren<ConveyorBelt>();
        float vz = belt != null ? belt.beltSpeed : 0f;
        float g  = Mathf.Abs(Physics.gravity.y);
        return vz * Mathf.Sqrt((triggerWidth / 2f) / (frictionCoefficient * g));
    }

    private void BuildZones()
    {
        if (numDivertPoints <= 0)
        {
            Debug.LogWarning("Diverter: numDivertPoints must be > 0.", this);
            return;
        }

        int   n          = numDivertPoints;
        float exitOffset = ExitOffset();
        float beltStart  = beltCenterLocalZ - beltLength / 2f;
        float firstPoint = beltLength / (2f * n) - exitOffset;
        float step       = beltLength / n;

        Debug.Log($"Diverter '{name}': exitOffset={exitOffset:F3}  firstPoint={firstPoint:F3}  step={step:F3}", this);

        for (int i = 0; i < n; i++)
        {
            float localZ = beltStart + firstPoint + i * step;

            GameObject zoneGO = new GameObject($"DivertZone_{i}");
            zoneGO.transform.SetParent(transform, worldPositionStays: false);
            zoneGO.transform.localPosition = new Vector3(0f, triggerY, localZ);
            zoneGO.transform.localRotation = Quaternion.identity;
            zoneGO.layer = gameObject.layer;

            BoxCollider bc = zoneGO.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(triggerWidth, triggerHeight, triggerDepth);

            // No Rigidbody on the zone — packages have dynamic Rigidbodies, so
            // OnTriggerStay fires fine on this static trigger. Adding a kinematic
            // Rigidbody here would create two overlapping kinematic bodies (belt
            // surface + this zone) which causes packages to phase through the belt.

            DivertZone dz = zoneGO.AddComponent<DivertZone>();
            dz.diverter = this;
            dz.pointIndex = i;
        }

        Debug.Log($"Diverter: built {numDivertPoints} divert points → {numDivertPoints * 2} lanes.", this);
    }

#if UNITY_EDITOR
    private float GizmoTriggerY()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return triggerY;
        var wb = renderers[0].bounds;
        foreach (var r in renderers) wb.Encapsulate(r.bounds);
        float localSurfaceY = transform.InverseTransformPoint(new Vector3(wb.center.x, wb.max.y, wb.center.z)).y;
        return localSurfaceY + triggerHeight * 0.5f;
    }

    // Draw trigger zone gizmos in the Scene view for easy placement.
    private void OnDrawGizmosSelected()
    {
        if (numDivertPoints <= 0) return;

        int   n          = numDivertPoints;
        float exitOffset = ExitOffset();
        float beltStart  = beltCenterLocalZ - beltLength / 2f;
        float firstPoint = beltLength / (2f * n) - exitOffset;
        float step       = beltLength / n;
        float gizmoY     = GizmoTriggerY();

        for (int i = 0; i < n; i++)
        {
            float localZ = beltStart + firstPoint + i * step;
            Vector3 centre = transform.TransformPoint(new Vector3(0f, gizmoY, localZ));
            Vector3 size = new Vector3(triggerWidth, triggerHeight, triggerDepth);
            // Alternate colours so adjacent zones are easy to distinguish.
            Gizmos.color = (i % 2 == 0)
                ? new Color(0.2f, 0.8f, 1f, 0.4f)
                : new Color(1f, 0.6f, 0.1f, 0.4f);
            Gizmos.DrawCube(centre, size);
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 1f);
            Gizmos.DrawWireCube(centre, size);

            // Label each zone with its address range.
            int addrA = i * 2;
            int addrB = i * 2 + 1;
            UnityEditor.Handles.Label(centre + Vector3.up * (triggerHeight * 0.5f + 0.1f),
                $"Point {i}\nAddr {addrA}=L  {addrB}=R");
        }
    }
#endif
}
