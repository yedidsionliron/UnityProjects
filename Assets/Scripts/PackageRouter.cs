using UnityEngine;

/// <summary>
/// Assigns a random routing address to every box spawned by BoxSpawner.
/// Supports multiple diverters — addresses are divided evenly across all sort points.
/// Total lanes = sum of (numDivertPoints * 2) across all diverters.
/// Each diverter's addressOffset is set automatically at startup.
/// Attach to the same GameObject as BoxSpawner.
/// </summary>
[RequireComponent(typeof(BoxSpawner))]
public class PackageRouter : MonoBehaviour
{
    [Tooltip("All diverters in order along the belt. Offsets are calculated automatically.")]
    public Diverter[] diverters;

    private int totalLanes;

    private void Start()
    {
        // Assign each diverter its address offset based on position in the array.
        int offset = 0;
        foreach (Diverter d in diverters)
        {
            d.addressOffset = offset;
            offset += d.numDivertPoints * 2;
        }
        totalLanes = offset;

        // Push totalLanes to each diverter so DivertZone can use it.
        foreach (Diverter d in diverters)
            d.totalLanes = totalLanes;

        if (totalLanes == 0)
            Debug.LogWarning("PackageRouter: no diverters assigned or all have 0 divert points.", this);

        for (int i = 0; i < diverters.Length; i++)
            Debug.Log($"PackageRouter: Diverter[{i}] '{diverters[i].name}' offset={diverters[i].addressOffset} lanes={diverters[i].numDivertPoints * 2} totalLanes={diverters[i].totalLanes}", diverters[i]);

        GetComponent<BoxSpawner>().OnBoxSpawned += OnBoxSpawned;
    }

    private void OnBoxSpawned(GameObject box)
    {
        Package pkg = box.GetComponent<Package>();
        if (pkg == null) pkg = box.AddComponent<Package>();
        pkg.address = totalLanes > 0 ? Random.Range(1, 40001) : 1;
    }
}
