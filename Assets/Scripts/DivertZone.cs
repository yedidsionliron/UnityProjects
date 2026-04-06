using UnityEngine;

/// <summary>
/// Trigger zone placed by Diverter at each divert position.
/// Routes a package left or right by checking its address against
/// the linked SortPoints' address ranges.
///
/// Physics: applies lateral friction force toward divertSpeed —
/// same model as ConveyorBelt but in the X direction. F = μ × m × g.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DivertZone : MonoBehaviour
{
    [HideInInspector] public SortPoint leftPoint;
    [HideInInspector] public SortPoint rightPoint;
    [HideInInspector] public float divertSpeed;
    [HideInInspector] public float frictionCoefficient;

    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;

        Package pkg = other.GetComponentInParent<Package>();
        if (pkg == null) return;

        float targetSpeed;
        if      (leftPoint  != null && leftPoint.Contains(pkg.address))  targetSpeed = -divertSpeed;
        else if (rightPoint != null && rightPoint.Contains(pkg.address)) targetSpeed =  divertSpeed;
        else return;

        float slip = targetSpeed - rb.linearVelocity.x;
        if (Mathf.Abs(slip) < 0.001f) return;

        float normalForce   = rb.mass * Mathf.Abs(Physics.gravity.y);
        float frictionForce = frictionCoefficient * normalForce;
        float maxForce      = rb.mass * Mathf.Abs(slip) / Time.fixedDeltaTime;
        float appliedForce  = Mathf.Min(frictionForce, maxForce);

        rb.AddForce(new Vector3(Mathf.Sign(slip) * appliedForce, 0f, 0f), ForceMode.Force);
    }
}
