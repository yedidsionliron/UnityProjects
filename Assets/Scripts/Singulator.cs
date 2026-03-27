using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("SingulatorTests.EditMode")]

/// <summary>
/// Time-scheduled singulator belt.
///
/// On entry each package is assigned a slot: a scheduled arrival time T_i at
/// the convergence point z_c. Forward drive uses a kinematic formula to arrive
/// at exactly beltSpeed and z_c at T_i. Lateral drive uses a minimum-time
/// profile (−4x/τ²) to arrive centered at T_i.
///
/// After passing z_c packages cruise at beltSpeed.
///
/// Scene setup:
///   • Assign beltCollider in the Inspector (the belt surface BoxCollider).
///   • Remove ConveyorBelt from this GameObject — Singulator drives everything.
/// </summary>
public class Singulator : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Tooltip("The belt surface BoxCollider. Assign explicitly or leave null for auto-search.")]
    public BoxCollider beltCollider;

    [Header("Belt")]
    [Tooltip("Maximum belt speed (m/s).")]
    public float beltSpeed = 1.5f;

    [Tooltip("Fraction of belt length at which packages converge to a single file (0–1).")]
    [Range(0f, 1f)]
    public float convergencePoint = 0.8f;

    [Tooltip("Edge-to-edge gap between packages at the convergence point (m).")]
    public float desiredGap = 0.3f;

    [Header("Drive Limits")]
    [Tooltip("Maximum forward acceleration (m/s²).")]
    public float maxAcceleration = 3.0f;

    [Tooltip("Maximum forward deceleration magnitude (m/s²).")]
    public float maxDeceleration = 2.0f;

    [Tooltip("Lateral force cap (m/s²).")]
    public float maxLateralAccel = 2.0f;

    [Tooltip("Lateral velocity damping multiplier. Applied as −lateralDamping × mass × vx.")]
    public float lateralDamping = 1.5f;

    // ── PackageAgent ──────────────────────────────────────────────────────────

    private class PackageAgent
    {
        public Rigidbody body;
        public float     packageLength;
        public int       slotIndex;
        public float     scheduledArrivalTime;
        public bool      pastConvergence;
    }

    // ── Private State ─────────────────────────────────────────────────────────

    private readonly Dictionary<Rigidbody, PackageAgent> agentMap =
        new Dictionary<Rigidbody, PackageAgent>();

    private readonly List<PackageAgent> activeAgents = new List<PackageAgent>();

    private int nextSlotIndex;

    // Belt geometry — computed once in Start.
    private Vector3 beltOrigin;    // world position of entry edge center
    private Vector3 beltFwd;
    private Vector3 beltRight;
    private float   beltLength;
    private float   zConvergence;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        if (beltCollider == null)
            beltCollider = GetComponent<BoxCollider>();
        if (beltCollider == null)
            beltCollider = GetComponentInChildren<BoxCollider>();

        if (beltCollider == null)
        {
            Debug.LogError(
                $"Singulator '{name}': no BoxCollider found. Assign one via the inspector.", this);
            enabled = false;
            return;
        }

        // Zero PhysX friction — all drive forces are applied explicitly.
        beltCollider.material = new PhysicsMaterial("SingulatorSurface")
        {
            dynamicFriction = 0f,
            staticFriction  = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness      = 0f,
            bounceCombine   = PhysicsMaterialCombine.Minimum,
        };

        Transform t = beltCollider.transform;
        beltFwd   = t.forward;
        beltRight = t.right;

        // Entry edge center: collider center offset half-length backward along belt.
        beltOrigin   = t.TransformPoint(beltCollider.center + Vector3.back * beltCollider.size.z * 0.5f);
        beltLength   = beltCollider.size.z * t.lossyScale.z;
        zConvergence = convergencePoint * beltLength;

        Debug.Log($"Singulator '{name}': belt {beltLength:F2} m, convergence at z={zConvergence:F2} m.", this);
    }

    private void FixedUpdate()
    {
        Step1_UpdateRegistry();
        if (agentMap.Count == 0) return;
        RerankPreConvergenceAgents();
        Step2_DriveAgents();
    }

    // ── Step 1: Agent Registry ────────────────────────────────────────────────

    private void Step1_UpdateRegistry()
    {
        Transform bcT      = beltCollider.transform;
        Vector3   center   = bcT.TransformPoint(beltCollider.center + Vector3.up * 0.05f);
        Vector3   halfExts = new Vector3(
            beltCollider.size.x * 0.5f * bcT.lossyScale.x,
            beltCollider.size.y * 0.5f * bcT.lossyScale.y + 0.05f,
            beltCollider.size.z * 0.5f * bcT.lossyScale.z
        );

        Collider[] hits = Physics.OverlapBox(
            center, halfExts, bcT.rotation, ~0, QueryTriggerInteraction.Ignore);

        var onBelt    = new HashSet<Rigidbody>();
        var newAgents = new List<(Rigidbody rb, float length)>();

        foreach (Collider hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            Rigidbody rb = hit.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            onBelt.Add(rb);

            if (!agentMap.ContainsKey(rb))
                newAgents.Add((rb, MeasurePackageLength(rb)));
        }

        // Priority: package closest to the convergence point gets the earliest slot.
        Vector3 convergenceWorld = beltOrigin + beltFwd * zConvergence;
        newAgents.Sort((a, b) =>
            Vector3.Distance(a.rb.position, convergenceWorld)
                .CompareTo(Vector3.Distance(b.rb.position, convergenceWorld)));

        foreach (var (rb, pkgLen) in newAgents)
        {
            var agent = new PackageAgent
            {
                body                 = rb,
                packageLength        = pkgLen,
                slotIndex            = nextSlotIndex++,
                scheduledArrivalTime = 0f,   // assigned by RerankPreConvergenceAgents
                pastConvergence      = BeltLocalZ(rb.position) >= zConvergence,
            };

            agentMap[rb] = agent;
            activeAgents.Add(agent);
        }

        // Remove agents no longer on the belt.
        var toRemove = new List<Rigidbody>();
        foreach (var kvp in agentMap)
            if (!onBelt.Contains(kvp.Key)) toRemove.Add(kvp.Key);

        foreach (var rb in toRemove)
        {
            activeAgents.Remove(agentMap[rb]);
            agentMap.Remove(rb);
        }
    }

    // ── Rerank ────────────────────────────────────────────────────────────────

    private void RerankPreConvergenceAgents()
    {
        // Find the latest scheduled arrival among packages already past convergence —
        // the first pre-convergence package must not arrive before that clears.
        float anchorT        = 0f;
        float anchorDuration = 0f;
        foreach (PackageAgent agent in activeAgents)
        {
            if (agent.pastConvergence && agent.scheduledArrivalTime >= anchorT)
            {
                anchorT        = agent.scheduledArrivalTime;
                anchorDuration = (agent.packageLength + desiredGap) / Mathf.Max(beltSpeed, 0.001f);
            }
        }

        // Collect and sort pre-convergence agents: closest to convergence point first.
        var preConv = new List<PackageAgent>();
        foreach (PackageAgent agent in activeAgents)
            if (!agent.pastConvergence)
                preConv.Add(agent);

        Vector3 convergenceWorld = beltOrigin + beltFwd * zConvergence;
        preConv.Sort((a, b) =>
        {
            int cmp = Vector3.Distance(a.body.position, convergenceWorld)
                          .CompareTo(Vector3.Distance(b.body.position, convergenceWorld));
            return cmp != 0 ? cmp : a.slotIndex.CompareTo(b.slotIndex);
        });

        float prevT = anchorT + anchorDuration;
        foreach (PackageAgent agent in preConv)
        {
            float distToConv   = Mathf.Max(0f, zConvergence - BeltLocalZ(agent.body.position));
            float minArrival   = Time.fixedTime + distToConv / Mathf.Max(beltSpeed, 0.001f);
            float slotDuration = (agent.packageLength + desiredGap) / Mathf.Max(beltSpeed, 0.001f);
            agent.scheduledArrivalTime = Mathf.Max(minArrival, prevT);
            prevT = agent.scheduledArrivalTime + slotDuration;
        }
    }

    // ── Step 2: Drive Agents ──────────────────────────────────────────────────

    private void Step2_DriveAgents()
    {
        const float epsilon = 1e-4f;
        float       dt      = Time.fixedDeltaTime;

        foreach (PackageAgent agent in activeAgents)
        {
            Rigidbody rb  = agent.body;
            float     z   = BeltLocalZ(rb.position);
            float     x   = BeltLocalX(rb.position);
            float     vz  = Vector3.Dot(rb.linearVelocity, beltFwd);
            float     vx  = Vector3.Dot(rb.linearVelocity, beltRight);
            float     tau = agent.scheduledArrivalTime - Time.fixedTime;

            if (tau <= 0f || z >= zConvergence)
                agent.pastConvergence = true;

            // ── Cruise mode (past convergence) ─────────────────────────────────
            if (agent.pastConvergence)
            {
                float slip = beltSpeed - vz;
                if (Mathf.Abs(slip) > epsilon)
                {
                    float a = Mathf.Clamp(slip / dt, -maxDeceleration, maxAcceleration);
                    rb.AddForce(beltFwd * rb.mass * a, ForceMode.Force);
                }
                continue;
            }

            // ── Forward drive — kinematic arrival at (z_c, beltSpeed, T_i) ─────
            // If the package can reach convergence at beltSpeed before T_i (slack),
            // just drive at beltSpeed — don't brake to hit the scheduled time.
            float distToConv = zConvergence - z;
            float a_z;
            if (distToConv - beltSpeed * tau <= 0f)
            {
                // Slack branch. Target constant-speed arrival at T_i rather than beltSpeed.
                // Gap packages:          tau ≈ distToConv/beltSpeed  → target ≈ beltSpeed (cruise freely).
                // Side-by-side packages: tau includes yielding time   → target < beltSpeed (slow to yield).
                float targetVz = distToConv / Mathf.Max(tau, 0.001f);
                float slip     = targetVz - vz;
                a_z = Mathf.Clamp(slip / dt, -maxDeceleration, maxAcceleration);
            }
            else
            {
                a_z = ComputeForwardAccel(
                    vz, beltSpeed, distToConv, tau, maxAcceleration, maxDeceleration);
            }
            rb.AddForce(beltFwd * rb.mass * a_z, ForceMode.Force);

            // ── Lateral drive — minimum-time centering profile ─────────────────
            //   a_x = −4x/τ²  drives x→0 with vx→0 at τ=0.
            //   Guard: when τ is very small the formula blows up; fall back to
            //   damping-only to let the D-term finish the job.
            if (tau > 0.1f && Mathf.Abs(x) > epsilon)
            {
                float a_x = Mathf.Clamp(-4f * x / (tau * tau), -maxLateralAccel, maxLateralAccel);
                rb.AddForce(beltRight * rb.mass * a_x, ForceMode.Force);
            }

            // Lateral damping — always applied pre-convergence.
            if (Mathf.Abs(vx) > epsilon)
                rb.AddForce(beltRight * (-lateralDamping * rb.mass * vx), ForceMode.Force);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    internal float BeltLocalZ(Vector3 worldPos) => Vector3.Dot(worldPos - beltOrigin, beltFwd);
    internal float BeltLocalX(Vector3 worldPos) => Vector3.Dot(worldPos - beltOrigin, beltRight);

    /// <summary>
    /// Kinematic forward acceleration to arrive at (distToConv=0, beltSpeed) in time tau.
    /// a = −0.5·(v_belt − vz)² / (distToConv − v_belt·τ)
    /// </summary>
    internal static float ComputeForwardAccel(
        float vz, float beltSpeed, float distToConv, float tau,
        float maxAcceleration, float maxDeceleration)
    {
        const float epsilon = 1e-4f;
        float denom = distToConv - beltSpeed * tau;
        float a_z   = Mathf.Abs(denom) < epsilon
            ? 0f
            : -0.5f * (beltSpeed - vz) * (beltSpeed - vz) / denom;
        return Mathf.Clamp(a_z, -maxDeceleration, maxAcceleration);
    }

    /// <summary>
    /// Computes the scheduled arrival time for a new slot.
    /// T_i = max(fixedTime + minTravelTime, lastSlotArrivalTime + slotDuration)
    /// </summary>
    internal static float ComputeSlotTime(
        float fixedTime, float minTravelTime,
        float lastSlotArrivalTime, float slotDuration)
    {
        float earliestSlot = fixedTime + minTravelTime;
        return Mathf.Max(earliestSlot, lastSlotArrivalTime + slotDuration);
    }

    /// <summary>OBB projection of the package along the belt axis (full length, not half).</summary>
    private float MeasurePackageLength(Rigidbody rb)
    {
        BoxCollider pkg = rb.GetComponentInChildren<BoxCollider>();
        if (pkg == null) return 0.5f;

        Transform t        = pkg.transform;   // use collider's own transform, not rb.transform
        Vector3   halfLocal = Vector3.Scale(pkg.size * 0.5f, t.lossyScale);
        float     halfExt  =
            Mathf.Abs(Vector3.Dot(beltFwd, t.right))   * halfLocal.x +
            Mathf.Abs(Vector3.Dot(beltFwd, t.up))       * halfLocal.y +
            Mathf.Abs(Vector3.Dot(beltFwd, t.forward))  * halfLocal.z;
        return halfExt * 2f;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (beltCollider == null) return;

        Transform t      = beltCollider.transform;
        Vector3   center = t.TransformPoint(beltCollider.center);
        Vector3   size   = Vector3.Scale(beltCollider.size, t.lossyScale);

        // Belt bounds.
        Gizmos.matrix = Matrix4x4.TRS(center, t.rotation, Vector3.one);
        Gizmos.color  = new Color(0.2f, 1f, 0.4f, 0.12f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color  = new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = Matrix4x4.identity;

        // Convergence line — computed inline so it draws correctly in edit mode.
        float   len       = beltCollider.size.z * t.lossyScale.z;
        float   halfWidth = beltCollider.size.x * t.lossyScale.x * 0.5f;
        Vector3 fwd       = t.forward;
        Vector3 right     = t.right;
        Vector3 origin    = t.TransformPoint(beltCollider.center + Vector3.back * beltCollider.size.z * 0.5f);
        Vector3 convMid   = origin + fwd * (convergencePoint * len);

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.9f);
        Gizmos.DrawLine(convMid - right * halfWidth, convMid + right * halfWidth);
        Gizmos.DrawSphere(convMid, 0.05f);

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 1.5f,
            $"Singulator  speed={beltSpeed} m/s  conv={convergencePoint:P0}  agents={agentMap?.Count ?? 0}");
    }
#endif
}
