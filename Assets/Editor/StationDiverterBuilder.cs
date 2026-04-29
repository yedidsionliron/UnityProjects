using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Globalization;
using PCS;

/// <summary>
/// Rebuilds the StationScene diverter from its local DiverterConfig
/// and registers its S# Gaylords against the station database.
/// </summary>
public static class StationDiverterBuilder
{
    private const string StationScenePath = "Assets/Scenes/StationScene.unity";
    private const string StationRootName = "Station";
    private const string DiverterRootName = "StationDiverter";
    private const string AddressInitObjectName = "AddressInit";
    private const string GaylordPrefabPath = "Assets/LastMileAssets/Prefabs/Gaylord.prefab";
    private const string BeltPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/belt.prefab";
    private const string StartCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/startCap.prefab";
    private const string EndCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/endCap.prefab";
    private const string RailingPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railing.prefab";
    private const string RailingStartCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railingCapStart.prefab";
    private const string RailingEndCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railingCapEnd.prefab";
    private const string RailingDoubleCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railingCapDouble.prefab";
    private const string InternalsPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/internals.prefab";

    [MenuItem("Tools/Station Builder/Build Station Diverter")]
    public static void BuildStationDiverter()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene stationScene = EditorSceneManager.OpenScene(StationScenePath, OpenSceneMode.Single);

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

        GameObject diverterRoot = EnsureLocalDiverter(station, gridMap.gridData);
        if (diverterRoot == null)
        {
            LogAvailableDiverterConfigs();
            Debug.LogError(
                $"StationDiverterBuilder: no local DiverterConfig found in {StationScenePath}. " +
                "Failed to create/configure a local station diverter.");
            return;
        }

        var diverterConfig = diverterRoot.GetComponent<DiverterConfig>();
        if (diverterConfig == null)
        {
            Debug.LogError($"StationDiverterBuilder: '{DiverterRootName}' is missing DiverterConfig.");
            return;
        }

        if (!TryGetSortChuteMapping(gridMap.gridData, diverterConfig, out var sortLabels, out string overflowLabel))
            return;

        RebindAddressInit(station, diverterConfig);
        RebuildDiverter(diverterConfig);
        AlignDiverterToSortChutes(diverterRoot.transform, gridMap, sortLabels, overflowLabel);
        layoutBuilder.RegisterSortChuteGaylordsFromDiverter(diverterRoot.transform, sortLabels, overflowLabel);

        EditorSceneManager.MarkSceneDirty(stationScene);
        EditorSceneManager.SaveScene(stationScene);
    }

    private static void AlignDiverterToSortChutes(
        Transform diverterRoot,
        GridMap gridMap,
        IReadOnlyList<string> sortLabels,
        string overflowLabel)
    {
        var sortPoints = diverterRoot.GetComponentsInChildren<SortPoint>(includeInactive: true);
        System.Array.Sort(sortPoints, (a, b) => a.id.CompareTo(b.id));

        if (sortPoints.Length != sortLabels.Count)
        {
            Debug.LogError(
                $"StationDiverterBuilder: diverter sort-point count ({sortPoints.Length}) does not match mapped S-cell count ({sortLabels.Count}).");
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

        Vector2Int overflowCell = default;
        bool hasOverflowTarget = !string.IsNullOrEmpty(overflowLabel) &&
                                 gridMap.gridData.TryResolveLabel(overflowLabel, out overflowCell);
        Vector3 overflowTarget = hasOverflowTarget ? gridMap.CellToWorld(overflowCell) : Vector3.zero;
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
            float score = hasOverflowTarget
                ? ComputeOverflowScore(position, rotation, overflowRoot, overflowTarget)
                : 0f;
            if (score < bestScore)
            {
                bestScore = score;
                bestPose = new Pose(position, rotation);
            }
        }

        diverterRoot.SetPositionAndRotation(bestPose.position, bestPose.rotation);
    }

    private static GameObject FindExistingLocalDiverter(GameObject stationRoot)
    {
        Transform existing = stationRoot.transform.Find(DiverterRootName);
        if (existing != null && existing.GetComponent<DiverterConfig>() != null)
            return existing.gameObject;

        return null;
    }

    private static GameObject EnsureLocalDiverter(GameObject stationRoot, StationGridData gridData)
    {
        GameObject diverterRoot = FindExistingLocalDiverter(stationRoot);
        if (diverterRoot == null)
            diverterRoot = GetOrCreateLocalDiverterRoot(stationRoot);

        ConfigureLocalDiverter(diverterRoot, stationRoot, gridData);
        return diverterRoot.GetComponent<DiverterConfig>() != null ? diverterRoot : null;
    }

    private static GameObject GetOrCreateLocalDiverterRoot(GameObject stationRoot)
    {
        Transform existing = stationRoot.transform.Find(DiverterRootName);
        if (existing != null)
            return existing.gameObject;

        var go = new GameObject(DiverterRootName);
        go.transform.SetParent(stationRoot.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    private static void ConfigureLocalDiverter(GameObject diverterRoot, GameObject stationRoot, StationGridData gridData)
    {
        var diverterConfig = diverterRoot.GetComponent<DiverterConfig>();
        bool createdDiverterConfig = diverterConfig == null;
        if (diverterConfig == null)
            diverterConfig = diverterRoot.AddComponent<DiverterConfig>();

        var pcsConfig = diverterRoot.GetComponent<PCSConfig>();
        if (pcsConfig == null)
            pcsConfig = diverterRoot.AddComponent<PCSConfig>();

        var diverter = diverterRoot.GetComponent<Diverter>();
        if (diverter == null)
            diverter = diverterRoot.AddComponent<Diverter>();

        ConfigurePCSConfig(pcsConfig);
        ConfigureDiverter(diverter);
        ConfigureDiverterConfig(diverterConfig, stationRoot, gridData, createdDiverterConfig);

        EditorUtility.SetDirty(pcsConfig);
        EditorUtility.SetDirty(diverter);
        EditorUtility.SetDirty(diverterConfig);
        EditorUtility.SetDirty(diverterRoot);
    }

    private static void ConfigurePCSConfig(PCSConfig pcsConfig)
    {
        pcsConfig.editMode = PCSConfig.EditModes.None;
        pcsConfig.length = Mathf.Max(1, pcsConfig.length);
        pcsConfig.width = 2f;
        pcsConfig.height = 1.05f;
        pcsConfig.internalsEnabled = false;
        pcsConfig.internalsCount = 3;
        pcsConfig.speed = 2f;
        pcsConfig.singulatorMode = false;
        pcsConfig.settingsImported = true;

        ConfigurePCSPart(ref pcsConfig.belt, BeltPrefabPath, mirror: true, new Vector3(0f, 0f, 0.2f), PCSPart.LengthMode.Stretch);
        ConfigurePCSPart(ref pcsConfig.startCap, StartCapPrefabPath, mirror: true, new Vector3(0f, 0f, 0.19500005f), PCSPart.LengthMode.Stretch);
        ConfigurePCSPart(ref pcsConfig.endCap, EndCapPrefabPath, mirror: false, new Vector3(0f, 0f, 0.19500005f), PCSPart.LengthMode.Stretch);
        ConfigurePCSPart(ref pcsConfig.railing, RailingPrefabPath, mirror: true, Vector3.zero, PCSPart.LengthMode.Stretch);
        ConfigurePCSPart(ref pcsConfig.railingStartCap, RailingStartCapPrefabPath, mirror: false, Vector3.zero, PCSPart.LengthMode.Stretch);
        ConfigurePCSPart(ref pcsConfig.railingEndCap, RailingEndCapPrefabPath, mirror: false, Vector3.zero, PCSPart.LengthMode.Stretch);
        ConfigurePCSPart(ref pcsConfig.railingDoubleCap, RailingDoubleCapPrefabPath, mirror: false, Vector3.zero, PCSPart.LengthMode.Stretch);
        ConfigurePCSPart(ref pcsConfig.internals, InternalsPrefabPath, mirror: true, Vector3.zero, PCSPart.LengthMode.Stretch);

        pcsConfig.CheckRailingData();
        for (int side = 0; side < 2; side++)
        {
            for (int i = 0; i < pcsConfig.railingData[side].enabledStates.Count; i++)
                pcsConfig.railingData[side].enabledStates[i] = false;
        }
    }

    private static void ConfigurePCSPart(
        ref PCSPart part,
        string prefabPath,
        bool mirror,
        Vector3 positionOffset,
        PCSPart.LengthMode lengthMode)
    {
        if (part == null)
            part = new PCSPart();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"StationDiverterBuilder: failed to load PCS prefab at '{prefabPath}'.");
            return;
        }

        part.prefab = prefab;
        part.gameObject = null;
        part.mirror = mirror;
        part.positionOffset = positionOffset;
        part.parent = null;
        part.lengthMode = lengthMode;
        part.renderers = prefab.GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    private static void ConfigureDiverter(Diverter diverter)
    {
        diverter.landingNormalized = 0.3f;
        diverter.divertSpeed = 2f;
        diverter.frictionCoefficient = 0.6f;
        diverter.beltLength = 6f;
        diverter.triggerDepth = 0.6f;
        diverter.triggerHeight = 1.2f;
        diverter.packageHalfLength = 0.3f;
    }

    private static void ConfigureDiverterConfig(
        DiverterConfig diverterConfig,
        GameObject stationRoot,
        StationGridData gridData,
        bool createdDiverterConfig)
    {
        var addressInit = stationRoot.GetComponentInChildren<AddressInit>(includeInactive: true);
        if (addressInit == null)
        {
            var go = new GameObject(AddressInitObjectName);
            go.transform.SetParent(stationRoot.transform, worldPositionStays: false);
            addressInit = go.AddComponent<AddressInit>();
        }

        diverterConfig.addressInit = addressInit;
        diverterConfig.gaylordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GaylordPrefabPath);
        diverterConfig.gaylordGap = 0.1f;
        diverterConfig.gaylordBufferPerSide = 0.1f;
        diverterConfig.addOverflowGaylord = true;

        if (createdDiverterConfig || diverterConfig.numDivertPoints <= 0)
        {
            int totalSortChutes = CountSortChuteLabels(gridData);
            int overflowSlots = diverterConfig.addOverflowGaylord && totalSortChutes > 0 ? 1 : 0;
            int pairedSortSlots = Mathf.Max(0, totalSortChutes - overflowSlots);
            diverterConfig.numDivertPoints = Mathf.Max(1, pairedSortSlots / 2);
        }
    }

    private static int CountSortChuteLabels(StationGridData gridData)
    {
        int count = 0;
        foreach (var cell in gridData.AllCells)
        {
            if (cell.cellType == CellType.SortChute && IsNumberedSortLabel(cell.label))
                count++;
        }

        return count;
    }

    private static void LogAvailableDiverterConfigs()
    {
        var diverterConfigs = Object.FindObjectsByType<DiverterConfig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (diverterConfigs == null || diverterConfigs.Length == 0)
        {
            Debug.LogWarning("StationDiverterBuilder: found 0 loaded DiverterConfig components.");
            return;
        }

        foreach (var diverterConfig in diverterConfigs)
        {
            if (diverterConfig == null)
                continue;

            string path = GetTransformPath(diverterConfig.transform);
            string sceneName = diverterConfig.gameObject.scene.IsValid() ? diverterConfig.gameObject.scene.path : "<invalid scene>";
            Debug.LogWarning(
                $"StationDiverterBuilder: found DiverterConfig on '{path}' in scene '{sceneName}' " +
                $"(activeSelf={diverterConfig.gameObject.activeSelf}, activeInHierarchy={diverterConfig.gameObject.activeInHierarchy}).",
                diverterConfig);
        }
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }

        return path;
    }

    private static void RebindAddressInit(GameObject station, DiverterConfig diverterConfig)
    {
        var targetAddressInit = station.GetComponentInChildren<AddressInit>(includeInactive: true);
        if (targetAddressInit == null)
        {
            var go = new GameObject(AddressInitObjectName);
            go.transform.SetParent(station.transform, worldPositionStays: false);
            targetAddressInit = go.AddComponent<AddressInit>();
        }

        diverterConfig.addressInit = targetAddressInit;
        EditorUtility.SetDirty(targetAddressInit);
        EditorUtility.SetDirty(diverterConfig);
    }

    private static void RebuildDiverter(DiverterConfig diverterConfig)
    {
        ClearGeneratedDiverterChildren(diverterConfig.transform);
        DiverterConfigEditor.BuildConfig(diverterConfig);
    }

    private static void ClearGeneratedDiverterChildren(Transform diverterRoot)
    {
        for (int i = diverterRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = diverterRoot.GetChild(i);
            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static bool TryGetSortChuteMapping(
        StationGridData gridData,
        DiverterConfig diverterConfig,
        out List<string> sortLabels,
        out string overflowLabel)
    {
        var allSortLabels = new List<string>();
        foreach (var cell in gridData.AllCells)
        {
            if (cell.cellType != CellType.SortChute || !IsNumberedSortLabel(cell.label))
                continue;

            allSortLabels.Add(cell.label);
        }

        allSortLabels.Sort((a, b) => ExtractLabelNumber(a).CompareTo(ExtractLabelNumber(b)));

        int requiredSortLabels = diverterConfig.numDivertPoints * 2;
        int requiredLabels = requiredSortLabels + (diverterConfig.addOverflowGaylord ? 1 : 0);
        if (allSortLabels.Count < requiredLabels)
        {
            Debug.LogError(
                $"StationDiverterBuilder: grid only has {allSortLabels.Count} S-cells, but the diverter config requires {requiredLabels} " +
                $"({diverterConfig.numDivertPoints} divert positions -> {requiredSortLabels} sort-point Gaylords{(diverterConfig.addOverflowGaylord ? " + overflow" : string.Empty)}).",
                diverterConfig);
            sortLabels = null;
            overflowLabel = null;
            return false;
        }

        sortLabels = allSortLabels.GetRange(0, requiredSortLabels);
        overflowLabel = diverterConfig.addOverflowGaylord
            ? allSortLabels[requiredSortLabels]
            : null;
        return true;
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
