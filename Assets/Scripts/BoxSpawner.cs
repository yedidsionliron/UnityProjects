using UnityEngine;
using System;

/// <summary>
/// Spawns a random box prefab above the conveyor belt at a set interval.
/// Controls only spawn rate and box size.
/// Other scripts can subscribe to OnBoxSpawned to do further setup.
/// </summary>
public class BoxSpawner : MonoBehaviour
{
    [Tooltip("Pool of box prefabs to pick from randomly")]
    public GameObject[] boxPrefabs;

    [Tooltip("Seconds between spawns")]
    public float spawnInterval = 1.2f;

    [Tooltip("Uniform scale applied to every spawned box")]
    public float spawnScale = 1f;

    [Tooltip("Physics material applied to package colliders to reduce inter-package friction")]
    public PhysicsMaterial packageMaterial;

    public event Action<GameObject> OnBoxSpawned;

    private float timer;

    private void Start()
    {
        if (boxPrefabs == null || boxPrefabs.Length == 0)
        {
            Debug.LogWarning("BoxSpawner: no prefabs assigned.", this);
            return;
        }
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
        GameObject prefab = boxPrefabs[UnityEngine.Random.Range(0, boxPrefabs.Length)];
        if (prefab == null)
        {
            Debug.LogError("BoxSpawner: selected prefab is null.", this);
            return;
        }

        Quaternion randomYaw = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        GameObject box = Instantiate(prefab, transform.position, randomYaw);
        box.transform.localScale = Vector3.one * spawnScale;

        if (packageMaterial != null)
        {
            foreach (var col in box.GetComponentsInChildren<Collider>())
                col.material = packageMaterial;
        }

        OnBoxSpawned?.Invoke(box);
    }
}
