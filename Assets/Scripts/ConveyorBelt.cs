using UnityEngine;

/// <summary>
/// Place on PaletArrow.010 (kinematic Rigidbody + BoxCollider).
///
/// Every FixedUpdate, queries the belt volume for dynamic Rigidbodies resting
/// on top. Friction force is computed as F = μ × N where N = mass × g,
/// matching how a real belt drives a box through surface friction.
/// Force is capped so the box never overshoots belt speed in one step.
///
/// PhysX friction on the belt collider is zero so it doesn't generate a
/// counter-force that fights our explicit drive force.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ConveyorBelt : MonoBehaviour
{
    [Tooltip("Belt surface speed in m/s (along +Z of this transform)")]
    public float beltSpeed = 2f;

    [Tooltip("Friction coefficient between belt surface and box (μ). " +
             "Typical rubber-on-cardboard ≈ 0.5–0.8.")]
    public float frictionCoefficient = 0.7f;

    private BoxCollider bc;

    private void Start()
    {
        Physics.defaultContactOffset = 0.001f;

        bc = GetComponent<BoxCollider>();
        if (bc == null)
        {
            Debug.LogError($"ConveyorBelt '{name}': no BoxCollider found. Add one manually or let PCS create it.", this);
            enabled = false;
            return;
        }

        // Zero PhysX friction — we drive the box ourselves.
        // Non-zero friction here would create a counter-force opposing our drive.
        bc.material = new PhysicsMaterial("BeltSurface")
        {
            dynamicFriction = 0f,
            staticFriction  = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness      = 0f,
            bounceCombine   = PhysicsMaterialCombine.Minimum
        };
    }

    private void FixedUpdate()
    {
        // Scan the belt volume (extended slightly upward to catch boxes on top).
        Vector3 worldCenter = transform.TransformPoint(bc.center + Vector3.up * 0.05f);
        Vector3 halfExtents = new Vector3(
            bc.size.x * 0.5f * transform.lossyScale.x,
            bc.size.y * 0.5f * transform.lossyScale.y + 0.05f,
            bc.size.z * 0.5f * transform.lossyScale.z
        );

        Collider[] hits = Physics.OverlapBox(
            worldCenter, halfExtents, transform.rotation,
            ~0, QueryTriggerInteraction.Ignore
        );

        Vector3 beltDir = transform.forward;

        foreach (Collider hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Rigidbody rb = hit.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            // Slip = how much faster the belt moves than the box.
            float slip = beltSpeed - Vector3.Dot(rb.linearVelocity, beltDir);
            if (Mathf.Abs(slip) < 0.001f) continue;

            // Friction force: F = μ × N, where N = mass × g (flat belt, normal force = weight).
            float normalForce   = rb.mass * Mathf.Abs(Physics.gravity.y);
            float frictionForce = frictionCoefficient * normalForce;

            // Cap so the box doesn't overshoot belt speed in one physics step.
            float maxForce    = rb.mass * Mathf.Abs(slip) / Time.fixedDeltaTime;
            float appliedForce = Mathf.Min(frictionForce, maxForce);

            rb.AddForce(beltDir * Mathf.Sign(slip) * appliedForce, ForceMode.Force);
        }
    }
}
