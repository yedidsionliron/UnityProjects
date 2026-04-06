using System.Collections.Generic;
using UnityEngine;

namespace PCS
{
    /// <summary>
    /// Drop-in replacement for PCSConveyor on the Physics child of a singulator belt.
    ///
    /// Instead of moving a kinematic surface to drag packages via friction, this
    /// component applies per-package explicit forces to achieve time-scheduled
    /// single-file isolation and lateral centering.
    ///
    /// Scene setup:
    ///   • Place on the same GameObject as PCSConveyor (the Physics child).
    ///   • Remove or disable PCSConveyor on that same object.
    ///   • The Rigidbody on this object must be Kinematic.
    ///   • No configuration needed — belt geometry is read from this object's BoxCollider.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public class PCSsingulator : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Belt")]
        [Tooltip("Target belt speed (m/s).")]
        public float beltSpeed = 1.5f;

        [Tooltip("Fraction of belt length at which packages converge to single file (0–1).")]
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

        [Tooltip("Spring gain for post-convergence lateral centering (m/s² per metre of offset).")]
        public float lateralCenteringGain = 2.0f;

        [Header("Package Surface")]
        [Tooltip("Friction applied to package colliders so boxes slide off each other easily (0 = frictionless).")]
        public float packageFriction = 0f;

        // ── PackageAgent ──────────────────────────────────────────────────────

        private class PackageAgent
        {
            public Rigidbody body;
            public float     packageLength;
            public int       slotIndex;
            public float     scheduledArrivalTime;
            public bool      pastConvergence;
            public float     cruiseAcceleration;
        }

        // ── Private State ─────────────────────────────────────────────────────

        private readonly Dictionary<Rigidbody, PackageAgent> agentMap =
            new Dictionary<Rigidbody, PackageAgent>();

        private readonly List<PackageAgent> activeAgents = new List<PackageAgent>();

        // Collider materials saved on entry so they can be restored when a package leaves.
        private readonly Dictionary<Rigidbody, PhysicsMaterial[]> savedMaterials =
            new Dictionary<Rigidbody, PhysicsMaterial[]>();

        private int nextSlotIndex;

        // Belt geometry — computed once in Start.
        private BoxCollider     surfaceCollider;
        private PhysicsMaterial packageMaterial;   // applied to every registered package
        private Vector3         beltOrigin;
        private Vector3         beltFwd;
        private Vector3         beltRight;
        private float           beltLength;
        private float           zConvergence;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            surfaceCollider = GetComponent<BoxCollider>();

            // Zero PhysX friction — all drive forces are applied explicitly.
            // This also disables any residual dragging from the kinematic Rigidbody.
            surfaceCollider.material = new PhysicsMaterial("PCSsingulatorSurface")
            {
                dynamicFriction = 0f,
                staticFriction  = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness      = 0f,
                bounceCombine   = PhysicsMaterialCombine.Minimum,
            };

            // Near-zero friction for packages — shared across all registered boxes.
            // Recreate when Start() runs so Inspector changes at edit-time take effect.
            packageMaterial = new PhysicsMaterial("PCSsingulatorPackage")
            {
                dynamicFriction = packageFriction,
                staticFriction  = packageFriction,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness      = 0f,
                bounceCombine   = PhysicsMaterialCombine.Minimum,
            };

            beltFwd   = transform.forward;
            beltRight = transform.right;

            // Entry edge center: collider center offset half-length backward along belt.
            beltOrigin   = transform.TransformPoint(surfaceCollider.center + Vector3.back * surfaceCollider.size.z * 0.5f);
            beltLength   = surfaceCollider.size.z * transform.lossyScale.z;
            zConvergence = convergencePoint * beltLength;

            Debug.Log($"PCSsingulator '{name}': belt {beltLength:F2} m, convergence at z={zConvergence:F2} m.", this);
        }

        private void FixedUpdate()
        {
            Step1_UpdateRegistry();
            if (agentMap.Count == 0) return;
            RerankPreConvergenceAgents();
            Step2_DriveAgents();
        }

        // ── Step 1: Agent Registry ────────────────────────────────────────────

        private void Step1_UpdateRegistry()
        {
            Vector3 center   = transform.TransformPoint(surfaceCollider.center + Vector3.up * 0.05f);
            Vector3 halfExts = new Vector3(
                surfaceCollider.size.x * 0.5f * transform.lossyScale.x,
                surfaceCollider.size.y * 0.5f * transform.lossyScale.y + 0.05f,
                surfaceCollider.size.z * 0.5f * transform.lossyScale.z
            );

            Collider[] hits = Physics.OverlapBox(
                center, halfExts, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

            var onBelt    = new HashSet<Rigidbody>();
            var newAgents = new List<(Rigidbody rb, float length)>();

            var materialised = new HashSet<Rigidbody>();

            foreach (Collider hit in hits)
            {
                // Skip anything that belongs to the belt assembly (same root).
                if (hit.transform.root == transform.root) continue;
                Rigidbody rb = hit.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;

                onBelt.Add(rb);

                // Apply frictionless material to every detected rigidbody every frame —
                // not just new ones. Packages touching each other outside the zone may
                // have default friction and need it overwritten as soon as they're seen.
                if (materialised.Add(rb))
                {
                    // Save original materials on first encounter so we can restore on exit.
                    if (!savedMaterials.ContainsKey(rb))
                    {
                        Collider[] cols = rb.GetComponentsInChildren<Collider>();
                        var mats = new PhysicsMaterial[cols.Length];
                        for (int i = 0; i < cols.Length; i++)
                            mats[i] = cols[i].sharedMaterial;
                        savedMaterials[rb] = mats;
                    }

                    foreach (Collider col in rb.GetComponentsInChildren<Collider>())
                        col.material = packageMaterial;
                }

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
                // Restore collider materials to what they were before entering the singulator.
                if (savedMaterials.TryGetValue(rb, out PhysicsMaterial[] mats))
                {
                    Collider[] cols = rb.GetComponentsInChildren<Collider>();
                    for (int i = 0; i < cols.Length && i < mats.Length; i++)
                        cols[i].material = mats[i];
                    savedMaterials.Remove(rb);
                }

                activeAgents.Remove(agentMap[rb]);
                agentMap.Remove(rb);
            }
        }

        // ── Rerank ────────────────────────────────────────────────────────────

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
                return cmp != 0 ? cmp : b.slotIndex.CompareTo(a.slotIndex);
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

        // ── Step 2: Drive Agents ──────────────────────────────────────────────

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

                if (!agent.pastConvergence && (tau <= 0f || z >= zConvergence))
                {
                    agent.pastConvergence = true;
                    float d = beltLength * (1f - convergencePoint);
                    agent.cruiseAcceleration = d > 0.001f
                        ? Mathf.Clamp((beltSpeed * beltSpeed - vz * vz) / (2f * d), -maxDeceleration, maxAcceleration)
                        : 0f;
                }

                // ── Cruise mode (past convergence) ──────────────────────────
                if (agent.pastConvergence)
                {
                    // Forward: apply the one-time kinematic acceleration computed at the
                    // convergence crossing — chosen so the package reaches beltSpeed exactly
                    // at the belt exit, given distance = beltLength * (1 - convergencePoint).
                    rb.AddForce(beltFwd * rb.mass * agent.cruiseAcceleration, ForceMode.Force);

                    // Lateral: spring toward centerline + damping.
                    // Prevents packages that crossed center from drifting to the rail.
                    float a_x = -lateralCenteringGain * x - lateralDamping * vx;
                    a_x = Mathf.Clamp(a_x, -maxLateralAccel, maxLateralAccel);
                    if (Mathf.Abs(a_x) > epsilon)
                        rb.AddForce(beltRight * rb.mass * a_x, ForceMode.Force);

                    continue;
                }

                // ── Forward drive — kinematic arrival at (z_c, beltSpeed, T_i)
                // If the package can reach convergence at beltSpeed before T_i (slack),
                // just drive at beltSpeed — don't brake to hit the scheduled time.
                float distToConv = zConvergence - z;
                float denom      = distToConv - beltSpeed * tau;
                float a_z;
                if (denom <= 0f)
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
                    a_z = Mathf.Abs(denom) < epsilon
                        ? 0f
                        : -0.5f * (beltSpeed - vz) * (beltSpeed - vz) / denom;
                    a_z = Mathf.Clamp(a_z, -maxDeceleration, maxAcceleration);
                }
                rb.AddForce(beltFwd * rb.mass * a_z, ForceMode.Force);

                // ── Lateral drive — minimum-time centering (−4x/τ²) ─────────
                // Guard: when τ is very small, fall back to damping-only.
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

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Spatial capacity: sum of (packageLength + desiredGap) for all tracked packages,
        /// divided by belt length. Returns 0–1; above 1 means belt is over-full.
        /// </summary>
        public float GetCapacity()
        {
            if (beltLength <= 0f) return 0f;
            float used = 0f;
            foreach (var agent in activeAgents)
                used += agent.packageLength + desiredGap;
            return used / beltLength;
        }

        private float BeltLocalZ(Vector3 worldPos) => Vector3.Dot(worldPos - beltOrigin, beltFwd);
        private float BeltLocalX(Vector3 worldPos) => Vector3.Dot(worldPos - beltOrigin, beltRight);

        private float MeasurePackageLength(Rigidbody rb)
        {
            BoxCollider pkg = rb.GetComponentInChildren<BoxCollider>();
            if (pkg == null) return 0.5f;

            Transform t        = pkg.transform;
            Vector3   halfLocal = Vector3.Scale(pkg.size * 0.5f, t.lossyScale);
            float     halfExt  =
                Mathf.Abs(Vector3.Dot(beltFwd, t.right))   * halfLocal.x +
                Mathf.Abs(Vector3.Dot(beltFwd, t.up))       * halfLocal.y +
                Mathf.Abs(Vector3.Dot(beltFwd, t.forward))  * halfLocal.z;
            return halfExt * 2f;
        }

        // ── Gizmos ────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc == null) return;

            Vector3 center = transform.TransformPoint(bc.center);
            Vector3 size   = Vector3.Scale(bc.size, transform.lossyScale);

            // Belt bounds.
            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
            Gizmos.color  = new Color(0.2f, 1f, 0.4f, 0.12f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color  = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;

            // Convergence line.
            float   len       = bc.size.z * transform.lossyScale.z;
            float   halfWidth = bc.size.x * transform.lossyScale.x * 0.5f;
            Vector3 origin    = transform.TransformPoint(bc.center + Vector3.back * bc.size.z * 0.5f);
            Vector3 convMid   = origin + transform.forward * (convergencePoint * len);

            Gizmos.color = new Color(1f, 0.8f, 0f, 0.9f);
            Gizmos.DrawLine(convMid - transform.right * halfWidth, convMid + transform.right * halfWidth);
            Gizmos.DrawSphere(convMid, 0.05f);

            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.5f,
                $"PCSsingulator  speed={beltSpeed} m/s  conv={convergencePoint:P0}  agents={agentMap?.Count ?? 0}");
        }
#endif
    }
}
