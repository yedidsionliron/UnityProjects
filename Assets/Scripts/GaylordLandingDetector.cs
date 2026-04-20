using UnityEngine;

/// <summary>
/// Placed on the trigger slab inside each GaylordContainer at runtime.
/// Notifies PackageTracker when a package enters the gaylord.
/// </summary>
public class GaylordLandingDetector : MonoBehaviour
{
    public GaylordContainer owner;

    void OnTriggerEnter(Collider other)
    {
        var pkg = other.GetComponentInParent<Package>();
        if (pkg == null) return;
        PackageTracker.Instance?.RecordLanding(pkg, owner);
    }
}
