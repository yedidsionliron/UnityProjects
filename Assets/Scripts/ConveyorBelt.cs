using UnityEngine;

/// <summary>
/// Place on PaletArrow.010 (which has its own kinematic Rigidbody and BoxCollider).
///
/// Every physics tick, queries the belt's own BoxCollider volume for dynamic
/// Rigidbodies and pushes them toward beltSpeed. No collision callbacks needed.
///
/// The BoxCollider on PaletArrow.010 has friction = 0 so PhysX doesn't generate
/// a -Z resistance force that would fight our explicit drive force.
/// When the box slides past the belt end it leaves the OverlapBox volume →
/// zero force → gravity pulls it off naturally.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class ConveyorBelt : MonoBehaviour
{
    [Tooltip("Belt surface speed in m/s (+Z)")]
    public float beltSpeed = 2f;

    [Tooltip("How hard the belt pushes the box (scales with mass × gravity)")]
    public float frictionCoefficient = 1.2f;

    private BoxCollider bc;

    private void Start()
    {
        // Smaller contact offset reduces the "ghost edge" catch between adjacent colliders.
        Physics.defaultContactOffset = 0.001f;

        bc = GetComponent<BoxCollider>();

        // Zero friction: the collider provides vertical support only.
        // Any non-zero friction would create a -Z PhysX force opposing our drive force.
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
        // World-space centre and half-extents of the belt collider,
        // extended slightly upward to catch objects resting on top.
        Vector3 worldCenter  = transform.TransformPoint(bc.center + Vector3.up * 0.05f);
        Vector3 halfExtents  = new Vector3(
            bc.size.x * 0.5f * transform.lossyScale.x,
            bc.size.y * 0.5f * transform.lossyScale.y + 0.05f,
            bc.size.z * 0.5f * transform.lossyScale.z
        );

        Collider[] hits = Physics.OverlapBox(
            worldCenter, halfExtents, transform.rotation,
            ~0, QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            Rigidbody rb = hit.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            float slip     = beltSpeed - rb.linearVelocity.z;
            float maxForce = frictionCoefficient * rb.mass * Mathf.Abs(Physics.gravity.y);
            float t        = Mathf.Clamp01(Mathf.Abs(slip) / 0.1f);

            rb.AddForce(new Vector3(0f, 0f, Mathf.Sign(slip) * maxForce * t), ForceMode.Force);
        }
    }
}
