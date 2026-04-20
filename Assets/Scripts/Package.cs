using UnityEngine;

/// <summary>
/// Data component placed on every package by BoxSpawner.
/// The Diverter reads this address to decide where to route the package.
///
/// Address mapping (per Diverter):
///   address 0, 1  →  divert point 0  (0 = left, 1 = right)
///   address 2, 3  →  divert point 1  (0 = left, 1 = right)
///   address 2i, 2i+1 → divert point i
///
/// A package with address >= numDivertPoints*2 passes through unaffected.
/// </summary>
public class Package : MonoBehaviour
{
    [Tooltip("Unique sequential ID assigned at spawn.")]
    public int id;

    [Tooltip("Destination address used by Diverter to route this package.")]
    public int address;

    private static PhysicsMaterial _sharedMaterial;

    private void Awake()
    {
        if (_sharedMaterial == null)
        {
            _sharedMaterial = new PhysicsMaterial("PackageSurface")
            {
                dynamicFriction = 0.5f,
                staticFriction  = 0.5f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounciness      = 0f,
                bounceCombine   = PhysicsMaterialCombine.Minimum,
            };
        }

        foreach (Collider col in GetComponentsInChildren<Collider>())
            col.material = _sharedMaterial;
    }
}
