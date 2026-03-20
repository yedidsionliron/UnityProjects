using UnityEngine;

/// <summary>
/// Spawns a random box prefab above the conveyor belt at a set interval.
/// Attach to an empty GameObject positioned above the belt start.
/// Assign the NAACo Box_1 … Box_4 Built-in prefabs to the boxPrefabs array.
/// </summary>
public class BoxSpawner : MonoBehaviour
{
    [Tooltip("Pool of box prefabs to pick from randomly")]
    public GameObject[] boxPrefabs;

    [Tooltip("Seconds between spawns")]
    public float spawnInterval = 3f;

    [Tooltip("Uniform scale applied to every spawned box (NAACo boxes are ~0.2-0.4 m; 3 = ~0.6-1.2 m)")]
    public float spawnScale = 3f;

    [Tooltip("Mass given to each spawned box (kg)")]
    public float boxMass = 1f;

    private float timer;

    private void Start()
    {
        if (boxPrefabs == null || boxPrefabs.Length == 0)
        {
            Debug.LogWarning("BoxSpawner: no prefabs assigned — drag Box_1…Box_4 into the Box Prefabs array.", this);
            return;
        }
        Debug.Log($"BoxSpawner: Start() — {boxPrefabs.Length} prefabs assigned, interval={spawnInterval}s", this);
        // Spawn one immediately so you don't wait for the first interval.
        SpawnBox();
    }

    private void Update()
    {
        if (boxPrefabs == null || boxPrefabs.Length == 0) return;

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnBox();
        }
    }

    private void SpawnBox()
    {
        GameObject prefab = boxPrefabs[Random.Range(0, boxPrefabs.Length)];
        if (prefab == null)
        {
            Debug.LogError("BoxSpawner: selected prefab is null — check the Box Prefabs array for missing references.", this);
            return;
        }
        Debug.Log($"BoxSpawner: Spawning {prefab.name} at {transform.position}", this);
        GameObject box = Instantiate(prefab, transform.position, Quaternion.identity);

        box.transform.localScale = Vector3.one * spawnScale;

        // Ensure a Rigidbody exists so physics drives the box.
        Rigidbody rb = box.GetComponent<Rigidbody>();
        if (rb == null)
            rb = box.AddComponent<Rigidbody>();

        rb.mass = boxMass;
        rb.useGravity = true;
        rb.isKinematic = false;
        // Freeze rotation so boxes don't tumble off the belt.
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Replace any Built-in pipeline materials with URP Lit so boxes aren't pink.
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit != null)
        {
            foreach (Renderer r in box.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].shader.name != "Universal Render Pipeline/Lit")
                    {
                        Material m = new Material(urpLit);
                        m.mainTexture = mats[i].mainTexture;
                        mats[i] = m;
                    }
                }
                r.materials = mats;
            }
        }
    }
}
