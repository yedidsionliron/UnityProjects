using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Imports the reference diverter from SceneSingulator into StationScene
/// and registers its S# Gaylords against the station database.
/// </summary>
public static class StationDiverterBuilder
{
    private const string StationScenePath = "Assets/Scenes/StationScene.unity";
    private const string ReferenceScenePath = "Assets/Scenes/SceneSingulator.unity";
    private const string StationRootName = "Station";
    private const string DiverterRootName = "StationDiverter";
    private const string AddressInitObjectName = "AddressInit";
    private const string OverflowSortLabel = "S29";

    [MenuItem("Tools/Station Builder/Build Station Diverter")]
    public static void BuildStationDiverter()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene stationScene = EditorSceneManager.OpenScene(StationScenePath, OpenSceneMode.Single);
        Scene referenceScene = EditorSceneManager.OpenScene(ReferenceScenePath, OpenSceneMode.Additive);

        try
        {
            GameObject station = FindRootByName(stationScene, StationRootName);
            if (station == null)
            {
                Debug.LogError($"StationDiverterBuilder: '{StationRootName}' not found in {StationScenePath}.");
                return;
            }

            var gridMap = station.GetComponent<GridMap>();
            if (gridMap == null || gridMap.gridData == null)
            {
                Debug.LogError("StationDiverterBuilder: Station root is missing GridMap or gridData.");
                return;
            }

            var layoutBuilder = station.GetComponent<StationLayoutBuilder>();
            if (layoutBuilder == null)
            {
                Debug.LogError("StationDiverterBuilder: Station root is missing StationLayoutBuilder.");
                return;
            }

            GameObject source = FindReferenceDiverter(referenceScene);
            if (source == null)
            {
                Debug.LogError("StationDiverterBuilder: no reference diverter found in SceneSingulator.");
                return;
            }

            RemoveExistingDiverter(station.transform);

            GameObject clone = Object.Instantiate(source);
            clone.name = DiverterRootName;
            SceneManager.MoveGameObjectToScene(clone, stationScene);
            clone.transform.SetParent(station.transform, worldPositionStays: true);

            RebindAddressInit(referenceScene, station, clone);
            AlignDiverterToSortChutes(clone.transform, gridMap);
            layoutBuilder.RegisterSortChuteGaylordsFromDiverter(clone.transform);

            EditorSceneManager.MarkSceneDirty(stationScene);
            EditorSceneManager.SaveScene(stationScene);
        }
        finally
        {
            EditorSceneManager.CloseScene(referenceScene, removeScene: true);
        }
    }

    private static void AlignDiverterToSortChutes(Transform diverterRoot, GridMap gridMap)
    {
        var sortPoints = diverterRoot.GetComponentsInChildren<SortPoint>(includeInactive: true);
        System.Array.Sort(sortPoints, (a, b) => a.id.CompareTo(b.id));
        List<string> sortLabels = GetOrderedSortLabels(gridMap.gridData);

        if (sortPoints.Length != sortLabels.Count)
        {
            Debug.LogError(
                $"StationDiverterBuilder: diverter sort-point count ({sortPoints.Length}) does not match S-cell count ({sortLabels.Count}).");
            return;
        }

        Vector3[] localSortPoints = new Vector3[sortPoints.Length];
        Vector3[] targetWorldPoints = new Vector3[sortPoints.Length];

        for (int i = 0; i < sortPoints.Length; i++)
        {
            localSortPoints[i] = diverterRoot.InverseTransformPoint(sortPoints[i].transform.position);
            if (!gridMap.gridData.TryResolveLabel(sortLabels[i], out Vector2Int cell))
            {
                Debug.LogError($"StationDiverterBuilder: missing grid label '{sortLabels[i]}'.");
                return;
            }
            targetWorldPoints[i] = gridMap.CellToWorld(cell);
        }

        Quaternion sourceRotation = diverterRoot.rotation;
        Vector3 sourcePosition = diverterRoot.position;

        if (!gridMap.gridData.TryResolveLabel(OverflowSortLabel, out Vector2Int overflowCell))
        {
            Debug.LogError($"StationDiverterBuilder: missing overflow grid label '{OverflowSortLabel}'.");
            return;
        }

        Vector3 overflowTarget = gridMap.CellToWorld(overflowCell);
        Transform overflowRoot = diverterRoot.Find("OverflowGaylord");

        Pose bestPose = default;
        float bestScore = float.MaxValue;
        Quaternion[] candidates =
        {
            sourceRotation,
            sourceRotation * Quaternion.Euler(0f, 180f, 0f)
        };

        foreach (Quaternion rotation in candidates)
        {
            Vector3 offset = ComputeAverageOffset(rotation, localSortPoints, targetWorldPoints);
            Vector3 position = new Vector3(offset.x, sourcePosition.y, offset.z);
            float score = ComputeOverflowScore(position, rotation, overflowRoot, overflowTarget);
            if (score < bestScore)
            {
                bestScore = score;
                bestPose = new Pose(position, rotation);
            }
        }

        diverterRoot.SetPositionAndRotation(bestPose.position, bestPose.rotation);
    }

    private static void RemoveExistingDiverter(Transform stationRoot)
    {
        Transform existing = stationRoot.Find(DiverterRootName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);
    }

    private static GameObject FindReferenceDiverter(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var diverterConfig = root.GetComponentInChildren<DiverterConfig>(includeInactive: true);
            if (diverterConfig != null)
                return diverterConfig.gameObject;
        }

        return null;
    }

    private static void RebindAddressInit(Scene referenceScene, GameObject station, GameObject diverterRoot)
    {
        var sourceAddressInit = FindAddressInit(referenceScene);
        var targetAddressInit = station.GetComponentInChildren<AddressInit>(includeInactive: true);
        if (targetAddressInit == null)
        {
            var go = new GameObject(AddressInitObjectName);
            go.transform.SetParent(station.transform, worldPositionStays: false);
            targetAddressInit = go.AddComponent<AddressInit>();
        }

        if (sourceAddressInit != null)
            targetAddressInit.totalAddressSpace = sourceAddressInit.totalAddressSpace;

        var diverterConfig = diverterRoot.GetComponent<DiverterConfig>();
        if (diverterConfig != null)
            diverterConfig.addressInit = targetAddressInit;
    }

    private static AddressInit FindAddressInit(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var addressInit = root.GetComponentInChildren<AddressInit>(includeInactive: true);
            if (addressInit != null)
                return addressInit;
        }

        return null;
    }

    private static List<string> GetOrderedSortLabels(StationGridData gridData)
    {
        var labels = new List<string>();
        foreach (var cell in gridData.AllCells)
        {
            if (cell.cellType != CellType.SortChute || !IsNumberedSortLabel(cell.label))
                continue;

            int number = ExtractLabelNumber(cell.label);
            if (number >= 1 && number <= 28)
                labels.Add(cell.label);
        }

        labels.Sort((a, b) => ExtractLabelNumber(a).CompareTo(ExtractLabelNumber(b)));
        return labels;
    }

    private static Vector3 ComputeAverageOffset(Quaternion rotation, Vector3[] localPoints, Vector3[] targetPoints)
    {
        Vector3 total = Vector3.zero;
        for (int i = 0; i < localPoints.Length; i++)
            total += targetPoints[i] - (rotation * localPoints[i]);

        Vector3 average = total / Mathf.Max(1, localPoints.Length);
        average.y = 0f;
        return average;
    }

    private static float ComputeOverflowScore(Vector3 position, Quaternion rotation, Transform overflowRoot, Vector3 overflowTarget)
    {
        if (overflowRoot == null)
            return 0f;

        Vector3 localOverflow = overflowRoot.parent == null
            ? overflowRoot.position
            : overflowRoot.parent.InverseTransformPoint(overflowRoot.position);

        Vector3 predicted = position + (rotation * localOverflow);
        predicted.y = 0f;
        overflowTarget.y = 0f;
        return (predicted - overflowTarget).sqrMagnitude;
    }

    private static bool IsNumberedSortLabel(string label) =>
        !string.IsNullOrEmpty(label) &&
        label.Length >= 2 &&
        label[0] == 'S' &&
        int.TryParse(label.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static int ExtractLabelNumber(string label) =>
        int.TryParse(label.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : int.MinValue;

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
