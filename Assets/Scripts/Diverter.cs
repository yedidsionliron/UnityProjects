using PCS;
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

    [Range(0f, 1f)]
    [Tooltip("Normalised Z position within each Gaylord slot where packages land. " +
             "0 = slot start, 0.5 = centre, 1 = slot end.")]
    public float landingNormalized = 0.3f;

    [HideInInspector] public float beltCenterLocalZ = 0f;

    /// <summary>Belt speed (m/s). Set by DiverterConfigEditor at build time — ExitOffset() uses this
    /// to avoid a cross-assembly dependency on PCSConveyor.</summary>
    [HideInInspector] public float beltSpeed = 2f;

    /// <summary>Set by DiverterConfig.Build(). Linked into DivertZones at runtime by BuildZones().</summary>
    [HideInInspector] public SortPoint[] sortPoints;

    [Header("Physics")]
    [Tooltip("Target lateral speed the divert surface drives the box toward (m/s).")]
    public float divertSpeed = 2f;

    [Tooltip("Friction coefficient between divert surface and box (μ). Same formula as ConveyorBelt: F = μ × mass × g.")]
    public float frictionCoefficient = 0.6f;

    [Header("Geometry")]
    [Tooltip("Length of the conveyor belt in local Z (used to space divert points evenly).")]
    public float beltLength = 6f;

    [HideInInspector] public float measuredBeltLength = 0f;

    // triggerY is computed at runtime from the belt mesh — not a constant.
    private float triggerY;

    [Tooltip("Trigger zone width in X — auto-measured from belt renderers at runtime. Override only if measurement is wrong.")]
    public float triggerWidth = 1.4f;

    [Tooltip("Trigger zone depth in Z — narrow enough that zones don't overlap.")]
    public float triggerDepth = 0.6f;

    [Tooltip("Trigger zone height in Y.")]
    public float triggerHeight = 1.2f;

    [Tooltip("Half the length of a typical package in Z (m). Force begins when the package front face enters the zone, so the centre lags by this amount. Default 0.3 = 60 cm package.")]
    public float packageHalfLength = 0.3f;

    private void Start()
    {
        var conv = GetComponentInChildren<PCSConveyor>();
        if (conv != null) beltSpeed = conv.speed;

        MeasureBeltLength();
        BuildZones();
    }

    /// <summary>Callable at edit time by DiverterConfig.Build() to populate beltCenterLocalZ.</summary>
    public void MeasureBelt() => MeasureBeltLength();

    private void MeasureBeltLength()
    {
        if (TryMeasureFromConveyorCollider(out float colliderLength))
        {
            measuredBeltLength = colliderLength;
            Debug.Log($"Diverter '{name}': colliderLength={colliderLength:F3}  triggerWidth={triggerWidth:F3}  configuredBeltLength={beltLength:F3}  beltCenterLocalZ={beltCenterLocalZ:F3}  triggerY={triggerY:F3}", this);
            return;
        }

        var allRenderers = GetComponentsInChildren<Renderer>();
        // Exclude renderers under SortPoints (gaylords) — they inflate X bounds.
        var renderers = System.Array.FindAll(allRenderers, r => r.GetComponentInParent<SortPoint>() == null);
        if (renderers.Length == 0) return;

        // Accumulate world-space bounds of all belt renderers.
        Bounds wb = renderers[0].bounds;
        foreach (var r in renderers) wb.Encapsulate(r.bounds);

        // Project onto local Z for length.
        Vector3 localZ = transform.forward;
        float halfLen = Mathf.Abs(Vector3.Dot(wb.extents,
            new Vector3(Mathf.Abs(localZ.x), Mathf.Abs(localZ.y), Mathf.Abs(localZ.z))));
        float measured = halfLen * 2f;
        measuredBeltLength = measured;

        // Project onto local X for width → drives triggerWidth and ExitOffset().
        Vector3 localX = transform.right;
        float halfX = Mathf.Abs(Vector3.Dot(wb.extents,
            new Vector3(Mathf.Abs(localX.x), Mathf.Abs(localX.y), Mathf.Abs(localX.z))));
        triggerWidth = halfX * 2f;

        // Record where the belt centre sits in this transform's local Z space.
        beltCenterLocalZ = transform.InverseTransformPoint(wb.center).z;

        // Derive triggerY from the belt's actual top surface in local space.
        float worldSurfaceY = wb.max.y;
        float localSurfaceY = transform.InverseTransformPoint(new Vector3(wb.center.x, worldSurfaceY, wb.center.z)).y;
        triggerY = localSurfaceY + triggerHeight * 0.5f;

        // beltLength is intentionally NOT updated here — it is configured by DiverterConfig.Build()
        // to match the gaylord row length exactly.
        Debug.Log($"Diverter '{name}': meshLength={measured:F3}  triggerWidth={triggerWidth:F3}  configuredBeltLength={beltLength:F3}  beltCenterLocalZ={beltCenterLocalZ:F3}  triggerY={triggerY:F3}", this);
    }

    private bool TryMeasureFromConveyorCollider(out float measuredLength)
    {
        measuredLength = 0f;

        var conv = GetComponentInChildren<PCSConveyor>();
        Collider beltCollider = conv != null ? conv.GetComponent<Collider>() : null;
        if (beltCollider == null)
        {
            var singulator = GetComponentInChildren<PCSsingulator>();
            beltCollider = singulator != null ? singulator.GetComponent<Collider>() : null;
        }

        if (beltCollider is not BoxCollider bc)
            return false;

        Transform ct = bc.transform;
        Vector3 worldCenter = ct.TransformPoint(bc.center);
        Vector3 worldSize = Vector3.Scale(bc.size, ct.lossyScale);

        beltCenterLocalZ = transform.InverseTransformPoint(worldCenter).z;
        triggerWidth = worldSize.x;

        float worldSurfaceY = worldCenter.y + worldSize.y * 0.5f;
        float localSurfaceY = transform.InverseTransformPoint(new Vector3(worldCenter.x, worldSurfaceY, worldCenter.z)).y;
        triggerY = localSurfaceY + triggerHeight * 0.5f;
        measuredLength = worldSize.z;
        return true;
    }

    /// <summary>
    /// Z-distance a package travels along the belt while its centre of mass
    /// crosses from belt centre to belt edge, accounting for the divertSpeed cap.
    ///
    /// Phase 1 — constant acceleration (a = μg) until lateral velocity = divertSpeed:
    ///   x1 = divertSpeed² / (2a),  t1 = divertSpeed / a
    /// Phase 2 — constant lateral velocity divertSpeed until belt edge:
    ///   t2 = (triggerWidth/2 - x1) / divertSpeed  (only if divertSpeed is reached before the edge)
    ///
    /// If divertSpeed is never reached before the edge, pure kinematics applies:
    ///   t  = sqrt( triggerWidth / a )   [note: the ½ in d=½at² and the ½ in triggerWidth/2 cancel]
    /// </summary>
    public float ExitOffset()
    {
        float vz = beltSpeed;
        if (vz <= 0f) return 0f;

        float a = frictionCoefficient * Mathf.Abs(Physics.gravity.y);
        if (a <= 0f) return 0f;          // no friction → no diversion, avoid divide-by-zero

        if (divertSpeed <= 0f) return 0f; // no target speed → no diversion, avoid divide-by-zero

        float halfWidth = triggerWidth / 2f;

        // Lateral distance covered while accelerating to divertSpeed from rest.
        float x1 = (divertSpeed * divertSpeed) / (2f * a);

        float t;
        if (x1 >= halfWidth)
        {
            // Package is still accelerating when it reaches the belt edge — pure kinematics.
            // d = ½at²  →  t = sqrt(2d/a) = sqrt(triggerWidth/a)  (the ½ and /2 cancel)
            t = Mathf.Sqrt(triggerWidth / a);
        }
        else
        {
            // Package reaches divertSpeed at x1, then coasts the rest of the way.
            float t1 = divertSpeed / a;
            float t2 = (halfWidth - x1) / divertSpeed;
            t = t1 + t2;
        }

        return vz * t;
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
        float step       = beltLength / n;
        // Diversion force starts when the package ENTERS the trigger zone (at triggerCenter - triggerDepth/2),
        // not at the trigger centre. Shift the centre forward by triggerDepth/2 so that the
        // force-start point lands exactly exitOffset before the slot centre.
        float firstPoint = step * landingNormalized - exitOffset + triggerDepth / 2f - packageHalfLength;

        if (firstPoint < 0f)
            Debug.Log($"Diverter '{name}': firstPoint={firstPoint:F3} m — zone 0 sits {-firstPoint:F3} m before belt start (in pre-belt run). This is expected when exitOffset ({exitOffset:F3} m) is large.", this);

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
            dz.divertSpeed        = divertSpeed;
            dz.frictionCoefficient = frictionCoefficient;

            if (sortPoints != null && i * 2 + 1 < sortPoints.Length)
            {
                dz.leftPoint  = sortPoints[i * 2];
                dz.rightPoint = sortPoints[i * 2 + 1];
            }
            else
            {
                Debug.LogWarning($"Diverter '{name}': no SortPoints for position {i} — press Build on DiverterConfig.", this);
            }
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
        float step       = beltLength / n;
        float firstPoint = step * landingNormalized - exitOffset + triggerDepth / 2f - packageHalfLength;
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

            // Label each zone with SortPoint ID and address range (if DiverterConfig is assigned).
            string leftLabel  = "L: ?";
            string rightLabel = "R: ?";
            if (sortPoints != null && i * 2 + 1 < sortPoints.Length)
            {
                var l = sortPoints[i * 2];
                var r = sortPoints[i * 2 + 1];
                if (l != null) leftLabel  = $"L[{l.id}]: {l.addressMin}–{l.addressMax}";
                if (r != null) rightLabel = $"R[{r.id}]: {r.addressMin}–{r.addressMax}";
            }
            UnityEditor.Handles.Label(centre + Vector3.up * (triggerHeight * 0.5f + 0.1f),
                $"Point {i}\n{leftLabel}\n{rightLabel}");
        }
    }
#endif
}
