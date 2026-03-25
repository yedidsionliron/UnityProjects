using UnityEngine;

/// <summary>
/// Maintains 5 interior BoxColliders (floor + 4 walls) fitted to the Gaylord's
/// world-space bounds. Colliders are placed at scene root (not parented to the
/// Gaylord) and are world-axis-aligned regardless of the Gaylord's rotation.
/// Colliders are only created at runtime (Play mode). In Edit mode, gizmos show
/// the intended bounds without creating any scene objects.
/// </summary>
public class GaylordContainer : MonoBehaviour
{
    [Tooltip("Wall collider thickness")]
    public float wallThickness = 0.05f;

    [Header("Bounds (0 = mesh min, 1 = mesh max)")]
    [Range(0f, 1f)] public float xMin = 0.07f;
    [Range(0f, 1f)] public float xMax = 0.928f;
    [Range(0f, 1f)] public float yMin = 0.16f;
    [Range(0f, 1f)] public float yMax = 0.925f;
    [Range(0f, 1f)] public float zMin = 0.044f;
    [Range(0f, 1f)] public float zMax = 0.954f;

    [Header("Physics")]
    public PhysicsMaterial interiorMaterial;

    [System.NonSerialized]
    public GameObject collidersRoot;

    // ------------------------------------------------------------------ //

    void OnEnable()
    {
        if (Application.isPlaying)
            RebuildColliders();
    }

    void OnDisable() => DestroyColliders();

    // ------------------------------------------------------------------ //

    public void DestroyColliders()
    {
        if (collidersRoot == null) return;
        if (Application.isPlaying) Destroy(collidersRoot);
        else DestroyImmediate(collidersRoot);
        collidersRoot = null;
    }

    public void RebuildColliders()
    {
        DestroyColliders();

        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        // Compute the adjusted box from the 0-1 sliders
        Vector3 bMin = bounds.min;
        Vector3 bSize = bounds.size;

        float minX = bMin.x + bSize.x * xMin;
        float maxX = bMin.x + bSize.x * xMax;
        float minY = bMin.y + bSize.y * yMin;
        float maxY = bMin.y + bSize.y * yMax;
        float minZ = bMin.z + bSize.z * zMin;
        float maxZ = bMin.z + bSize.z * zMax;

        float sizeX = Mathf.Max(0f, maxX - minX);
        float sizeY = Mathf.Max(0f, maxY - minY);
        float sizeZ = Mathf.Max(0f, maxZ - minZ);

        float cx = minX + sizeX / 2f;
        float cy = minY + sizeY / 2f;
        float cz = minZ + sizeZ / 2f;

        float t = wallThickness / 2f;

        var panels = new (Vector3 worldPos, Vector3 size)[]
        {
            // Floor
            (new Vector3(cx, minY + t,      cz), new Vector3(sizeX, wallThickness, sizeZ)),
            // Front (+Z)
            (new Vector3(cx, cy, maxZ - t),       new Vector3(sizeX, sizeY, wallThickness)),
            // Back  (-Z)
            (new Vector3(cx, cy, minZ + t),       new Vector3(sizeX, sizeY, wallThickness)),
            // Right (+X)
            (new Vector3(maxX - t, cy, cz),       new Vector3(wallThickness, sizeY, sizeZ)),
            // Left  (-X)
            (new Vector3(minX + t, cy, cz),       new Vector3(wallThickness, sizeY, sizeZ)),
        };

        var root = new GameObject("__GaylordColliders");

        foreach (var (worldPos, size) in panels)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(root.transform, true);
            go.transform.position = worldPos;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            if (interiorMaterial != null)
                col.material = interiorMaterial;
        }

        collidersRoot = root;
    }

    // ------------------------------------------------------------------ //

    void OnDrawGizmos()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        Vector3 bMin = bounds.min;
        Vector3 bSize = bounds.size;

        float minX = bMin.x + bSize.x * xMin;  float maxX = bMin.x + bSize.x * xMax;
        float minY = bMin.y + bSize.y * yMin;  float maxY = bMin.y + bSize.y * yMax;
        float minZ = bMin.z + bSize.z * zMin;  float maxZ = bMin.z + bSize.z * zMax;

        float sizeX = Mathf.Max(0f, maxX - minX);
        float sizeY = Mathf.Max(0f, maxY - minY);
        float sizeZ = Mathf.Max(0f, maxZ - minZ);

        Vector3 center = new Vector3(minX + sizeX / 2f, minY + sizeY / 2f, minZ + sizeZ / 2f);
        Vector3 size   = new Vector3(sizeX, sizeY, sizeZ);

        // Interior wire box
        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        Gizmos.DrawWireCube(center, size);

        // Floor slab
        Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
        Gizmos.DrawCube(new Vector3(center.x, minY, center.z), new Vector3(sizeX, wallThickness, sizeZ));

        // Open top in yellow
        Gizmos.color = new Color(1f, 1f, 0f, 0.8f);
        float hw = sizeX / 2f, hd = sizeZ / 2f;
        Vector3 top = new Vector3(center.x, minY + sizeY, center.z);
        Gizmos.DrawLine(top + new Vector3(-hw, 0, -hd), top + new Vector3( hw, 0, -hd));
        Gizmos.DrawLine(top + new Vector3( hw, 0, -hd), top + new Vector3( hw, 0,  hd));
        Gizmos.DrawLine(top + new Vector3( hw, 0,  hd), top + new Vector3(-hw, 0,  hd));
        Gizmos.DrawLine(top + new Vector3(-hw, 0,  hd), top + new Vector3(-hw, 0, -hd));
    }
}
