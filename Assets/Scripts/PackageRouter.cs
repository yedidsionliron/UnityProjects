using UnityEngine;

/// <summary>
/// Assigns a random routing address to every box spawned by BoxSpawner.
/// Supports multiple diverters — addresses are divided evenly across all sort points.
/// Total lanes = sum of (numDivertPoints * 2) across all diverters.
/// Each diverter's addressOffset is set automatically at startup.
///
/// Also spawns Gaylord containers on both sides of each Diverter.
/// Number of Gaylords per side = numDivertPoints. Gaylord width along the belt
/// is scaled so all Gaylords fill the full belt length.
///
/// Attach to the same GameObject as BoxSpawner.
/// </summary>
[RequireComponent(typeof(BoxSpawner))]
public class PackageRouter : MonoBehaviour
{
    [Tooltip("All diverters in order along the belt. Offsets are calculated automatically.")]
    public Diverter[] diverters;

    [Header("Gaylords")]
    [Tooltip("Mesh from the Gaylord FBX. Expand Assets/Scenes/Gaylord.fbx in the Project window and drag the mesh here.")]
    public Mesh gaylordMesh;

    [Tooltip("Material for the Gaylord (Assets/Materials/Gaylord).")]
    public Material gaylordMaterial;

    [Tooltip("World-space size of the Gaylord along the belt direction (Z) at scale (200,100,200).")]
    public float gaylordNaturalLength = 2f;

    [Tooltip("World-space size of the Gaylord away from the belt (X) at scale (200,100,200).")]
    public float gaylordNaturalDepth = 2f;

    [Tooltip("Gap between belt edge and nearest face of the Gaylord (metres).")]
    public float gaylordGap = 0.1f;

    [Tooltip("Absolute world-space Y position of every Gaylord. " +
             "0 = ground. Increase if the mesh pivot is not at the base of the model.")]
    public float gaylordYOffset = 0f;

    private int totalLanes;

    private void Start()
    {
        if (diverters == null || diverters.Length == 0)
        {
            Debug.LogWarning("PackageRouter: diverters array is empty — nothing to set up.", this);
            GetComponent<BoxSpawner>().OnBoxSpawned += OnBoxSpawned;
            return;
        }

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

        SpawnGaylords();
    }

    // ------------------------------------------------------------------ //
    //  Gaylord placement
    // ------------------------------------------------------------------ //

    private void SpawnGaylords()
    {
        if (gaylordMesh == null)
            Debug.LogWarning("PackageRouter: gaylordMesh not assigned — Gaylords will spawn without a visible mesh.", this);

        // Derive natural size from the mesh bounds (already import-scaled).
        // At the baked-in localScale of (200, 100, 200) these give world-space extents.
        float naturalLength = gaylordMesh != null ? gaylordMesh.bounds.size.z * 200f : gaylordNaturalLength;
        float naturalDepth  = gaylordMesh != null ? gaylordMesh.bounds.size.x * 200f : gaylordNaturalDepth;

        // Y correction: pivot may not be at the visual base.
        // Raises the pivot so the bottom face (bounds.min.y * yScale) sits at gaylordYOffset.
        float yGroundCorrection = gaylordMesh != null ? -gaylordMesh.bounds.min.y * 100f : 0f;

        // Z correction: pivot may not be at the visual centre of the mesh in Z.
        // bounds.center.z / bounds.size.z is the normalised offset (0 = centred, 0.5 = pivot at start face).
        // Multiplying by sliceZ converts it to diverter-local units so the visual edge
        // lands exactly at the slice boundary instead of the visual centre.
        float zPivotNorm = (gaylordMesh != null && gaylordMesh.bounds.size.z > 0f)
            ? gaylordMesh.bounds.center.z / gaylordMesh.bounds.size.z
            : 0f;
        Debug.Log($"PackageRouter: mesh pivot offsets — " +
                  $"zPivotNorm={zPivotNorm:F3} (0=centred, 0.5=pivot at start face)  " +
                  $"yGroundCorrection={yGroundCorrection:F3} m", this);

        int total = 0;

        foreach (Diverter d in diverters)
        {
            int n = d.numDivertPoints;
            if (n <= 0)
            {
                Debug.LogWarning($"PackageRouter: Diverter '{d.name}' has numDivertPoints=0, skipping.", this);
                continue;
            }

            float sliceZ  = d.beltLength / n;
            float zScale  = naturalLength > 0f ? sliceZ / naturalLength : 1f;
            float xOffset = d.triggerWidth / 2f + gaylordGap + naturalDepth / 2f;

            Debug.Log($"PackageRouter: Diverter '{d.name}' — spawning {n * 2} Gaylords " +
                      $"(sliceZ={sliceZ:F2} zScale={zScale:F2} xOffset={xOffset:F2})", d);

            for (int i = 0; i < n; i++)
            {
                // beltCenterLocalZ: where the belt's visual centre sits in Diverter local Z.
                // Subtract zPivotNorm * sliceZ to correct for the mesh pivot not being at its visual centre.
                float beltStartZ = d.beltCenterLocalZ - d.beltLength / 2f;
                float localZ = beltStartZ + (i + 0.5f) * sliceZ - zPivotNorm * sliceZ;

                PlaceGaylord(d, i, new Vector3(-xOffset, yGroundCorrection, localZ), zScale, yGroundCorrection, "L");
                PlaceGaylord(d, i, new Vector3( xOffset, yGroundCorrection, localZ), zScale, yGroundCorrection, "R");
                total += 2;
            }
        }

        Debug.Log($"PackageRouter: spawned {total} Gaylords total.", this);
    }

    private void PlaceGaylord(Diverter d, int index, Vector3 localPos, float zScale, float yGroundCorrection, string side)
    {
        Vector3 worldPos = d.transform.TransformPoint(localPos);
        // Place pivot so the mesh's visible bottom face sits at gaylordYOffset (world ground = 0).
        worldPos.y = gaylordYOffset + yGroundCorrection;

        GameObject g = new GameObject($"Gaylord_{d.name}_{index}_{side}");
        g.transform.SetParent(d.transform, worldPositionStays: false);
        g.transform.position = worldPos;
        g.transform.rotation = d.transform.rotation;
        g.transform.localScale = new Vector3(200f, 100f, 200f * zScale);

        if (gaylordMesh != null)
        {
            g.AddComponent<MeshFilter>().sharedMesh = gaylordMesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = gaylordMaterial;
        }

        g.AddComponent<GaylordContainer>();
    }

    // ------------------------------------------------------------------ //

    private void OnBoxSpawned(GameObject box)
    {
        Package pkg = box.GetComponent<Package>();
        if (pkg == null) pkg = box.AddComponent<Package>();
        pkg.address = totalLanes > 0 ? Random.Range(1, 40001) : 1;
    }
}
