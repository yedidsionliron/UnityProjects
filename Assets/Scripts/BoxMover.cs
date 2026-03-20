using UnityEngine;

/// <summary>
/// Attach to the box. Moves it along the Z axis at beltSpeed every physics tick.
/// </summary>
public class BoxMover : MonoBehaviour
{
    public float beltSpeed = 2f;

    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (rb == null) return;
        rb.WakeUp();
        Vector3 vel = rb.linearVelocity;
        vel.x = 0f;
        vel.z = beltSpeed;
        rb.linearVelocity = vel;
    }
}
