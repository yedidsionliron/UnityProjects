using UnityEngine;

/// <summary>
/// Attach to the conveyor GameObject.
/// Scrolls the material UV on all child MeshRenderers to simulate belt movement.
/// </summary>
public class ConveyorBeltVisual : MonoBehaviour
{
    [Tooltip("Should match the beltSpeed on ConveyorBelt.cs")]
    public float scrollSpeed = 2f;

    private Renderer[] renderers;
    private float offset;

    private void Start()
    {
        renderers = GetComponentsInChildren<Renderer>();
    }

    private void Update()
    {
        offset += scrollSpeed * Time.deltaTime;
        foreach (Renderer r in renderers)
        {
            // Scroll on a per-instance material to avoid affecting shared assets
            r.material.mainTextureOffset = new Vector2(0, offset);
        }
    }
}
