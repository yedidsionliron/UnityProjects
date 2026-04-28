using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Imports the cleaned warehouse shell into StationScene and aligns it to the AMR floor plan.
/// The imported roots are grouped under a single parent so the operation is repeatable.
/// </summary>
public static class StationWarehouseImporter
{
    private const string StationScenePath = "Assets/Scenes/StationScene.unity";
    private const string WarehouseScenePath = "Assets/Scenes/WarehouseSceneSample_Copy.unity";
    private const string StationRootName = "Station";
    private const string ImportedWarehouseRootName = "ImportedWarehouse";

    [MenuItem("Tools/Station Builder/Import Warehouse Into Station Scene")]
    public static void ImportWarehouseIntoStationScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene stationScene = EditorSceneManager.OpenScene(StationScenePath, OpenSceneMode.Single);
        Scene warehouseScene = EditorSceneManager.OpenScene(WarehouseScenePath, OpenSceneMode.Additive);

        try
        {
            GameObject stationRoot = FindRootByName(stationScene, StationRootName);
            if (stationRoot == null)
            {
                Debug.LogError($"StationWarehouseImporter: '{StationRootName}' not found in {StationScenePath}.");
                return;
            }

            GridMap gridMap = stationRoot.GetComponent<GridMap>();
            if (gridMap == null || gridMap.gridData == null)
            {
                Debug.LogError("StationWarehouseImporter: Station root is missing GridMap or gridData.");
                return;
            }

            RemoveExistingImportedWarehouse(stationScene);

            var importedRoot = new GameObject(ImportedWarehouseRootName);
            importedRoot.transform.position = Vector3.zero;
            SceneManager.MoveGameObjectToScene(importedRoot, stationScene);

            foreach (var sourceRoot in warehouseScene.GetRootGameObjects())
            {
                GameObject clone = Object.Instantiate(sourceRoot);
                clone.name = sourceRoot.name;
                SceneManager.MoveGameObjectToScene(clone, stationScene);
                clone.transform.SetParent(importedRoot.transform, worldPositionStays: true);
            }

            AlignWarehouseToStation(importedRoot, gridMap);

            EditorSceneManager.MarkSceneDirty(stationScene);
            EditorSceneManager.SaveScene(stationScene);

            Debug.Log(
                $"StationWarehouseImporter: imported '{WarehouseScenePath}' into '{StationScenePath}' " +
                $"under '{ImportedWarehouseRootName}' and aligned it to the station grid.");
        }
        finally
        {
            EditorSceneManager.CloseScene(warehouseScene, removeScene: true);
        }
    }

    private static void AlignWarehouseToStation(GameObject importedWarehouseRoot, GridMap gridMap)
    {
        Bounds stationBounds = GetStationBounds(gridMap);
        Bounds warehouseBounds = GetRendererBounds(importedWarehouseRoot);

        Vector3 offset = Vector3.zero;
        offset.x = stationBounds.center.x - warehouseBounds.center.x;
        offset.z = stationBounds.center.z - warehouseBounds.center.z;
        offset.y = 0f;

        foreach (Transform child in importedWarehouseRoot.transform)
            child.position += offset;

        importedWarehouseRoot.transform.position = Vector3.zero;
    }

    private static Bounds GetStationBounds(GridMap gridMap)
    {
        float width = gridMap.gridData.cols * gridMap.cellWidth;
        float depth = gridMap.gridData.rows * gridMap.cellDepth;
        Vector3 center = gridMap.gridOrigin + new Vector3(width * 0.5f, 0f, -depth * 0.5f);
        Vector3 size = new Vector3(width, 0.01f, depth);
        return new Bounds(center, size);
    }

    private static Bounds GetRendererBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0)
            throw new System.InvalidOperationException("Imported warehouse has no renderers to align against.");

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void RemoveExistingImportedWarehouse(Scene stationScene)
    {
        GameObject existing = FindRootByName(stationScene, ImportedWarehouseRootName);
        if (existing != null)
            Object.DestroyImmediate(existing);
    }

    private static GameObject FindRootByName(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root;
        }
        return null;
    }
}
