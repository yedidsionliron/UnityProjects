using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PCS;

/// <summary>
/// Builds a local singulator in StationScene and fits it to the exact grid area
/// explicitly allocated to the Singulator system region.
/// </summary>
public static class StationSingulatorBuilder
{
    private const string StationScenePath = "Assets/Scenes/StationScene.unity";
    private const string StationRootName = "Station";
    private const string SingulatorRootName = "StationSingulator";
    private const string FeederSystemRootName = "StationFeederSystem";
    private const string FeederRootName = "Feeder";
    private const string BackupRootName = "backup";
    private const string CapacitySensorName = "CapacitySensor";
    private const string BeltPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/belt.prefab";
    private const string StartCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/startCap.prefab";
    private const string EndCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/endCap.prefab";
    private const string RailingPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railing.prefab";
    private const string RailingStartCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railingCapStart.prefab";
    private const string RailingEndCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railingCapEnd.prefab";
    private const string RailingDoubleCapPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/railingCapDouble.prefab";
    private const string InternalsPrefabPath = "Assets/PCS/Styles/Belt/Prefabs/internals.prefab";
    private const string SupportPrefabPath = "Assets/LastMileAssets/Models/ConveyorSupport.fbx";
    private const int ReferenceFeederTiles = 10;
    private const int ReferenceBackupTiles = 10;

    [MenuItem("Tools/Station Builder/Build Station Singulator")]
    [MenuItem("Tools/Build Station Singulator")]
    public static void BuildStationSingulator()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene stationScene = EditorSceneManager.OpenScene(StationScenePath, OpenSceneMode.Single);
        GameObject station = FindRootByName(stationScene, StationRootName);
        if (station == null)
        {
            Debug.LogError($"StationSingulatorBuilder: '{StationRootName}' not found in {StationScenePath}.");
            return;
        }

        var gridMap = station.GetComponent<GridMap>();
        if (gridMap == null || gridMap.gridData == null)
        {
            Debug.LogError("StationSingulatorBuilder: Station root is missing GridMap or gridData.");
            return;
        }

        if (!gridMap.gridData.TryGetRegionRect(SystemRegion.Singulator, out RectInt rect))
        {
            Debug.LogError(
                "StationSingulatorBuilder: grid does not define a rectangular Singulator system region. " +
                "Mark the singulator footprint explicitly in Grid.xlsx, regenerate Grid.json, and rebuild the station scene.");
            return;
        }

        if (!gridMap.gridData.TryGetRegionRect(SystemRegion.Feeder, out RectInt feederRect))
        {
            Debug.LogError(
                "StationSingulatorBuilder: grid does not define a rectangular Feeder system region. " +
                "Mark the feeder footprint explicitly in Grid.xlsx, regenerate Grid.json, and rebuild the station scene.");
            return;
        }

        GameObject singulatorRoot = GetOrCreateSingulatorRoot(station.transform);
        BuildLocalSingulator(singulatorRoot, gridMap, rect);
        BuildLocalFeederAssembly(station.transform, singulatorRoot, gridMap, feederRect);

        EditorSceneManager.MarkSceneDirty(stationScene);
        EditorSceneManager.SaveScene(stationScene);
    }

    private static void BuildLocalSingulator(GameObject singulatorRoot, GridMap gridMap, RectInt rect)
    {
        ClearChildren(singulatorRoot.transform);
        singulatorRoot.transform.localScale = Vector3.one;
        singulatorRoot.transform.rotation = Quaternion.identity;

        var pcsConfig = singulatorRoot.GetComponent<PCSConfig>();
        if (pcsConfig == null)
            pcsConfig = singulatorRoot.AddComponent<PCSConfig>();

        var singulator = singulatorRoot.GetComponent<Singulator>();
        if (singulator == null)
            singulator = singulatorRoot.AddComponent<Singulator>();

        ConfigurePCSConfig(
            pcsConfig,
            rect,
            gridMap,
            speed: 2f,
            slopeAngle: 0f,
            height: 1.05f,
            singulatorMode: true);
        ConfigureSingulator(singulator);

        pcsConfig.CreatePCS();

        Bounds bounds = MeasureWorldBounds(singulatorRoot.transform);
        Vector3 targetCenter = GetRectCenterWorld(gridMap, rect);
        Vector2 targetSize = GetRectSizeWorld(gridMap, rect);

        FitRootToTargetFootprint(singulatorRoot.transform, bounds, targetSize);
        AlignRootToTargetCenter(singulatorRoot.transform, targetCenter, gridMap.gridOrigin.y);

        EditorUtility.SetDirty(pcsConfig);
        EditorUtility.SetDirty(singulator);
        EditorUtility.SetDirty(singulatorRoot);
    }

    private static void BuildLocalFeederAssembly(
        Transform stationRoot,
        GameObject singulatorRoot,
        GridMap gridMap,
        RectInt feederRect)
    {
        GameObject feederSystemRoot = GetOrCreateChildRoot(stationRoot, FeederSystemRootName);
        ClearChildren(feederSystemRoot.transform);
        feederSystemRoot.transform.localScale = Vector3.one;
        feederSystemRoot.transform.localRotation = Quaternion.identity;
        feederSystemRoot.transform.localPosition = Vector3.zero;

        SplitFeederRect(feederRect, out RectInt feederBeltRect, out RectInt backupRect);

        GameObject feederRoot = new GameObject(FeederRootName);
        feederRoot.transform.SetParent(feederSystemRoot.transform, worldPositionStays: false);
        BuildConveyorRoot(feederRoot, gridMap, feederBeltRect, speed: 0.6f, slopeAngle: 10f, height: 0.927f, singulatorMode: false);

        GameObject backupRoot = new GameObject(BackupRootName);
        backupRoot.transform.SetParent(feederSystemRoot.transform, worldPositionStays: false);
        BuildConveyorRoot(backupRoot, gridMap, backupRect, speed: 0.6f, slopeAngle: 0f, height: 0.927f, singulatorMode: false);

        GameObject sensorRoot = new GameObject(CapacitySensorName);
        sensorRoot.transform.SetParent(feederSystemRoot.transform, worldPositionStays: false);
        sensorRoot.transform.position = GetRectCenterWorld(gridMap, feederRect) + Vector3.up * 0.5f;

        var sensor = sensorRoot.AddComponent<BeltCapacitySensor>();
        sensor.singulatorBelt = singulatorRoot;
        sensor.feederBelt = feederRoot;
        sensor.capacityThreshold = 1f;
        sensor.stopDuration = 0.5f;

        EditorUtility.SetDirty(feederRoot);
        EditorUtility.SetDirty(backupRoot);
        EditorUtility.SetDirty(sensorRoot);
        EditorUtility.SetDirty(sensor);
        EditorUtility.SetDirty(feederSystemRoot);
    }

    private static void BuildConveyorRoot(
        GameObject conveyorRoot,
        GridMap gridMap,
        RectInt rect,
        float speed,
        float slopeAngle,
        float height,
        bool singulatorMode)
    {
        ClearChildren(conveyorRoot.transform);
        conveyorRoot.transform.localScale = Vector3.one;
        conveyorRoot.transform.rotation = Quaternion.identity;

        var pcsConfig = conveyorRoot.GetComponent<PCSConfig>();
        if (pcsConfig == null)
            pcsConfig = conveyorRoot.AddComponent<PCSConfig>();

        ConfigurePCSConfig(
            pcsConfig,
            rect,
            gridMap,
            speed,
            slopeAngle,
            height,
            singulatorMode);

        pcsConfig.CreatePCS();

        Bounds bounds = MeasureWorldBounds(conveyorRoot.transform);
        Vector3 targetCenter = GetRectCenterWorld(gridMap, rect);
        Vector2 targetSize = GetRectSizeWorld(gridMap, rect);

        FitRootToTargetFootprint(conveyorRoot.transform, bounds, targetSize);
        AlignRootToTargetCenter(conveyorRoot.transform, targetCenter, gridMap.gridOrigin.y);

        EditorUtility.SetDirty(pcsConfig);
    }

    private static void ConfigurePCSConfig(
        PCSConfig pcsConfig,
        RectInt rect,
        GridMap gridMap,
        float speed,
        float slopeAngle,
        float height,
        bool singulatorMode)
    {
        pcsConfig.editMode = PCSConfig.EditModes.None;
        pcsConfig.width = GetRectSizeWorld(gridMap, rect).x;
        pcsConfig.height = height;
        pcsConfig.speed = speed;
        pcsConfig.internalsEnabled = false;
        pcsConfig.internalsCount = 3;
        pcsConfig.singulatorMode = singulatorMode;
        pcsConfig.settingsImported = true;
        pcsConfig.conveyorSlopeAngle = slopeAngle;
        pcsConfig.conveyorSupportPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SupportPrefabPath);
        pcsConfig.conveyorSupportScale = new Vector3(13f, 13f, 91f);

        float targetDepth = GetRectSizeWorld(gridMap, rect).y;
        float beltPitch = 0.2f;
        pcsConfig.length = Mathf.Max(1, Mathf.CeilToInt(targetDepth / beltPitch));

        ConfigurePCSPart(ref pcsConfig.belt, BeltPrefabPath, true, new Vector3(0f, 0f, 0.2f));
        ConfigurePCSPart(ref pcsConfig.startCap, StartCapPrefabPath, true, new Vector3(0f, 0f, 0.19500005f));
        ConfigurePCSPart(ref pcsConfig.endCap, EndCapPrefabPath, false, new Vector3(0f, 0f, 0.19500005f));
        ConfigurePCSPart(ref pcsConfig.railing, RailingPrefabPath, true, Vector3.zero);
        ConfigurePCSPart(ref pcsConfig.railingStartCap, RailingStartCapPrefabPath, true, Vector3.zero);
        ConfigurePCSPart(ref pcsConfig.railingEndCap, RailingEndCapPrefabPath, false, Vector3.zero);
        ConfigurePCSPart(ref pcsConfig.railingDoubleCap, RailingDoubleCapPrefabPath, false, Vector3.zero);
        ConfigurePCSPart(ref pcsConfig.internals, InternalsPrefabPath, true, new Vector3(0f, 0f, 0.18f));

        pcsConfig.CheckRailingData();
        for (int side = 0; side < 2; side++)
        {
            for (int i = 0; i < pcsConfig.railingData[side].enabledStates.Count; i++)
                pcsConfig.railingData[side].enabledStates[i] = true;
        }
    }

    private static void ConfigurePCSPart(ref PCSPart part, string prefabPath, bool mirror, Vector3 positionOffset)
    {
        if (part == null)
            part = new PCSPart();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"StationSingulatorBuilder: failed to load PCS prefab '{prefabPath}'.");
            return;
        }

        part.prefab = prefab;
        part.gameObject = null;
        part.mirror = mirror;
        part.positionOffset = positionOffset;
        part.parent = null;
        part.lengthMode = PCSPart.LengthMode.Stretch;
        part.renderers = prefab.GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    private static void ConfigureSingulator(Singulator singulator)
    {
        singulator.beltCollider = null;
        singulator.beltSpeed = 2f;
        singulator.convergencePoint = 0.8f;
        singulator.desiredGap = 0.6f;
        singulator.maxAcceleration = 3f;
        singulator.maxDeceleration = 2f;
        singulator.maxLateralAccel = 2f;
        singulator.lateralDamping = 1.2f;
    }

    private static void SplitFeederRect(RectInt feederRect, out RectInt feederBeltRect, out RectInt backupRect)
    {
        if (feederRect.height < 2)
            throw new InvalidOperationException("Feeder region must be at least two cells deep to fit feeder and backup.");

        int totalReference = ReferenceFeederTiles + ReferenceBackupTiles;
        int feederHeight = Mathf.Clamp(
            Mathf.RoundToInt((float)feederRect.height * ReferenceFeederTiles / totalReference),
            1,
            feederRect.height - 1);
        int backupHeight = feederRect.height - feederHeight;

        feederBeltRect = new RectInt(feederRect.xMin, feederRect.yMin, feederRect.width, feederHeight);
        backupRect = new RectInt(feederRect.xMin, feederRect.yMin + feederHeight, feederRect.width, backupHeight);
    }

    private static Vector3 GetRectCenterWorld(GridMap gridMap, RectInt rect)
    {
        float minX = gridMap.gridOrigin.x + rect.xMin * gridMap.cellWidth;
        float maxX = gridMap.gridOrigin.x + rect.xMax * gridMap.cellWidth;
        float maxZ = gridMap.gridOrigin.z - rect.yMin * gridMap.cellDepth;
        float minZ = gridMap.gridOrigin.z - rect.yMax * gridMap.cellDepth;
        return new Vector3((minX + maxX) * 0.5f, gridMap.gridOrigin.y, (minZ + maxZ) * 0.5f);
    }

    private static Vector2 GetRectSizeWorld(GridMap gridMap, RectInt rect) =>
        new Vector2(rect.width * gridMap.cellWidth, rect.height * gridMap.cellDepth);

    private static void FitRootToTargetFootprint(Transform root, Bounds currentBounds, Vector2 targetSize)
    {
        if (currentBounds.size.x <= 0.0001f || currentBounds.size.z <= 0.0001f)
            return;

        Vector3 scale = root.localScale;
        scale.x *= targetSize.x / currentBounds.size.x;
        scale.z *= targetSize.y / currentBounds.size.z;
        root.localScale = scale;
    }

    private static void AlignRootToTargetCenter(Transform root, Vector3 targetCenter, float groundY)
    {
        Bounds bounds = MeasureWorldBounds(root);
        Vector3 delta = new Vector3(
            targetCenter.x - bounds.center.x,
            groundY - bounds.min.y,
            targetCenter.z - bounds.center.z);
        root.position += delta;
    }

    private static Bounds MeasureWorldBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0)
            return new Bounds(root.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static GameObject GetOrCreateSingulatorRoot(Transform stationRoot)
    {
        Transform existing = stationRoot.Find(SingulatorRootName);
        if (existing != null)
            return existing.gameObject;

        var go = new GameObject(SingulatorRootName);
        go.transform.SetParent(stationRoot, worldPositionStays: false);
        return go;
    }

    private static GameObject GetOrCreateChildRoot(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        return go;
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
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
