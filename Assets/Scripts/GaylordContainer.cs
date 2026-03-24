using UnityEngine;

/// <summary>
/// Maintains 5 interior BoxColliders (floor + 4 walls) on the Gaylord so
/// packages accumulate inside. Colliders live as child objects and scale
/// automatically with the Gaylord's transform.
///
/// Just attach this component — colliders are created immediately from mesh bounds.
/// Walls are anchored to the floor and rise by wallHeight, so they never block
/// the open top.
/// </summary>
[ExecuteAlways]
public class GaylordContainer : MonoBehaviour
{
    [Header("Interior Dimensions")]
    [Tooltip("Width (X) and Depth (Z) of the interior in local space")]
    public Vector2 interiorFootprint = Vector2.zero;   // zero = not yet set

    [Tooltip("Y position of the floor in local space")]
    public float floorY = 0f;

    [Tooltip("How tall the walls rise from the floor (local space). Set to match inside height of the box.")]
    public float wallHeight = 1f;

    [Tooltip("Wall collider thickness")]
    public float wallThickness = 0.05f;

    [Header("Physics")]
    public PhysicsMaterial interiorMaterial;

    // ------------------------------------------------------------------ //

    void OnEnable()
    {
        if (interiorFootprint == Vector2.zero)
            AutoDetectFromMesh();

        RebuildColliders();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) RebuildColliders();
        };
    }
#endif

    // ------------------------------------------------------------------ //

    public void AutoDetectFromMesh()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        var scale = transform.lossyScale;

        // Width and depth from world bounds, converted to local space
        interiorFootprint = new Vector2(
            bounds.size.x / scale.x,
            bounds.size.z / scale.z
        );

        // Floor at the bottom of the mesh, wall height = full mesh height
        // Both in local space
        floorY      = transform.InverseTransformPoint(bounds.min).y;
        wallHeight  = bounds.size.y / scale.y;
    }

    public void RebuildColliders()
    {
        var existing = transform.Find("__GaylordColliders");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }

        if (interiorFootprint == Vector2.zero) return;

        var root = new GameObject("__GaylordColliders");
        root.transform.SetParent(transform, false);

        float hw = interiorFootprint.x / 2f;
        float hd = interiorFootprint.y / 2f;
        float t  = wallThickness / 2f;

        // Walls are anchored to the floor and rise by wallHeight.
        // Wall center Y = floorY + wallHeight/2
        float wallCenterY = floorY + wallHeight / 2f;

        var panels = new (Vector3 center, Vector3 size)[]
        {
            // Floor
            (new Vector3(0,           floorY + t,   0),   new Vector3(interiorFootprint.x, wallThickness, interiorFootprint.y)),
            // Front (+Z)
            (new Vector3(0,           wallCenterY,  hd - t),  new Vector3(interiorFootprint.x, wallHeight, wallThickness)),
            // Back  (-Z)
            (new Vector3(0,           wallCenterY, -hd + t),  new Vector3(interiorFootprint.x, wallHeight, wallThickness)),
            // Right (+X)
            (new Vector3( hw - t,     wallCenterY,  0),    new Vector3(wallThickness, wallHeight, interiorFootprint.y)),
            // Left  (-X)
            (new Vector3(-hw + t,     wallCenterY,  0),    new Vector3(wallThickness, wallHeight, interiorFootprint.y)),
        };

        foreach (var (center, size) in panels)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            if (interiorMaterial != null)
                col.material = interiorMaterial;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    // ------------------------------------------------------------------ //

    void OnDrawGizmos()
    {
        if (interiorFootprint == Vector2.zero) return;

        float hw = interiorFootprint.x / 2f;
        float hd = interiorFootprint.y / 2f;
        float topY = floorY + wallHeight;

        Gizmos.matrix = transform.localToWorldMatrix;

        // Floor slab
        Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
        Gizmos.DrawCube(new Vector3(0, floorY, 0),
                        new Vector3(interiorFootprint.x, wallThickness, interiorFootprint.y));

        // Interior wire box (walls only, open top)
        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        float midY = floorY + wallHeight / 2f;
        Gizmos.DrawWireCube(new Vector3(0, midY, 0),
                            new Vector3(interiorFootprint.x, wallHeight, interiorFootprint.y));

        // Open top in yellow
        Gizmos.color = new Color(1f, 1f, 0f, 0.8f);
        var top = new Vector3(0, topY, 0);
        Gizmos.DrawLine(top + new Vector3(-hw, 0, -hd), top + new Vector3( hw, 0, -hd));
        Gizmos.DrawLine(top + new Vector3( hw, 0, -hd), top + new Vector3( hw, 0,  hd));
        Gizmos.DrawLine(top + new Vector3( hw, 0,  hd), top + new Vector3(-hw, 0,  hd));
        Gizmos.DrawLine(top + new Vector3(-hw, 0,  hd), top + new Vector3(-hw, 0, -hd));
    }
}
