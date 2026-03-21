using UnityEngine;

/// <summary>
/// Trigger zone created by Diverter at runtime at each divert point.
/// Applies lateral friction force toward divertSpeed — same physics as ConveyorBelt
/// but in the X direction. F = μ × N where N = mass × g.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DivertZone : MonoBehaviour
{
    [HideInInspector] public Diverter diverter;
    [HideInInspector] public int pointIndex;

    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;

        Package pkg = other.GetComponentInParent<Package>();
        if (pkg == null) return;

        // Map address to a global lane using modulo so all lanes get equal share.
        int lane = (pkg.address - 1) % diverter.totalLanes;

        // Translate to this diverter's local lane space.
        int localLane = lane - diverter.addressOffset;
        if (localLane < 0 || localLane >= diverter.numDivertPoints * 2) return;
        if (localLane / 2 != pointIndex) return;

        bool goRight = (localLane % 2 == 1);
        float targetSpeed = goRight ? diverter.divertSpeed : -diverter.divertSpeed;

        // Friction-based lateral force: F = μ × N, capped to not overshoot target speed.
        float slip          = targetSpeed - rb.linearVelocity.x;
        if (Mathf.Abs(slip) < 0.001f) return;

        float normalForce   = rb.mass * Mathf.Abs(Physics.gravity.y);
        float frictionForce = diverter.frictionCoefficient * normalForce;
        float maxForce      = rb.mass * Mathf.Abs(slip) / Time.fixedDeltaTime;
        float appliedForce  = Mathf.Min(frictionForce, maxForce);

        rb.AddForce(new Vector3(Mathf.Sign(slip) * appliedForce, 0f, 0f), ForceMode.Force);
    }
}
