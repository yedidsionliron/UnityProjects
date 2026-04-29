using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

/// <summary>
/// Editor window for building the AMR station scene from Grid.json.
/// Open via: Tools > Station Builder
/// </summary>
public class StationGridBuilder : EditorWindow
{
    // ── Asset paths ────────────────────────────────────────────────────────
    private const string GridJsonPath       = "Data/Grid.json";
    private const string GridExcelPath      = "Data/Grid.xlsx";
    private const string GridParserScriptPath = "Data/parse_grid.js";
    private const string GridDataAssetPath  = "Assets/Scripts/AMR/StationGridData.asset";
    private const string ScenePath          = "Assets/Scenes/StationScene.unity";
    private const string GaylordPrefabPath  = "Assets/LastMileAssets/Prefabs/Gaylord.prefab";
    private const string SettingsAssetPath  = "Assets/Scripts/AMR/StationBuilderSettings.asset";
    private const string GlobalConfigAssetPath = "Assets/Config/StationLayoutConfig.asset";

    // ── Zone colours ───────────────────────────────────────────────────────
    private static readonly Dictionary<CellType, Color> ZoneColors = new Dictionary<CellType, Color>
    {
        { CellType.Storage,      new Color(0.80f, 0.90f, 1.00f) },
        { CellType.Corridor,     new Color(0.85f, 0.85f, 0.85f) },
        { CellType.InductZone,   new Color(1.00f, 0.98f, 0.70f) },
        { CellType.SorterSystem, new Color(0.55f, 0.55f, 0.55f) },
        { CellType.SortChute,    new Color(1.00f, 0.75f, 0.75f) },
        { CellType.EmptyBuffer,  new Color(1.00f, 0.85f, 0.50f) },
        { CellType.FeederQueue,  new Color(0.85f, 0.80f, 1.00f) },
        { CellType.Staging,      new Color(0.75f, 1.00f, 0.75f) },
        { CellType.Charging,     new Color(0.65f, 0.79f, 0.93f) }, // steel blue
        { CellType.Empty,        new Color(0.92f, 0.92f, 0.92f) }, // light gray

    };

    // ── Window state ───────────────────────────────────────────────────────
    private StationBuilderSettings _settings;
    private StationLayoutConfig _globalConfig;
    private float _measuredGaylordX;
    private float _measuredGaylordZ;
    private bool  _measured;
    private Vector2 _scroll;
    private GUIStyle _boxStyle;

    [MenuItem("Tools/Station Builder")]
    public static void Open() => GetWindow<StationGridBuilder>("Station Builder");

    [MenuItem("Tools/Station Builder/Rebuild Station Scene")]
    public static void RebuildStationSceneFromCurrentSettings()
    {
        var builder = CreateInstance<StationGridBuilder>();
        try
        {
            builder._settings = LoadOrCreateSettings();
            builder._globalConfig = LoadOrCreateGlobalConfig();
            builder.MeasureGaylord();
            builder.ComputeCellSize(out float cellWidth, out float cellDepth);
            if (!builder.GenerateGridJson())
                return;
            builder.BuildScene(cellWidth, cellDepth);
        }
        finally
        {
            DestroyImmediate(builder);
        }
    }

    [MenuItem("Tools/Station Builder/Place Station Gaylords")]
    public static void PlaceStorageGaylords()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject station = GameObject.Find("Station");
        if (station == null)
        {
            Debug.LogError($"StationGridBuilder: 'Station' root not found in {ScenePath}.");
            return;
        }

        var builder = CreateInstance<StationGridBuilder>();
        try
        {
            builder._settings = LoadOrCreateSettings();
            builder._globalConfig = LoadOrCreateGlobalConfig();
            var layoutBuilder = EnsureStationLayoutBuilder(station, builder._settings);
            if (layoutBuilder == null)
                return;

            layoutBuilder.RebuildStorageGaylords();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            DestroyImmediate(builder);
        }
    }

    // ── GUI ────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        _settings = LoadOrCreateSettings();
        _globalConfig = LoadOrCreateGlobalConfig();
        MeasureGaylord();
    }

    void OnGUI()
    {
        // Cache style after skin is initialised (not valid before first layout event)
        if (_boxStyle == null)
            _boxStyle = new GUIStyle(EditorStyles.helpBox);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        try
        {
            DrawGUI();
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Station Grid Builder", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // ── Settings asset ─────────────────────────────────────────────
        var newSettings = (StationBuilderSettings)EditorGUILayout.ObjectField(
            "Settings Asset", _settings, typeof(StationBuilderSettings), false);
        if (newSettings != _settings)
        {
            _settings = newSettings;
            _measured = false;
            MeasureGaylord();
        }

        if (_settings == null)
        {
            EditorGUILayout.HelpBox("No settings asset found.", MessageType.Error);
            return;
        }

        if (_globalConfig == null)
        {
            EditorGUILayout.HelpBox("No global config asset found.", MessageType.Error);
            return;
        }

        _settings.bufferPerSide = _globalConfig.cellBuffer;

        EditorGUILayout.Space(8);

        var newGlobalConfig = (StationLayoutConfig)EditorGUILayout.ObjectField(
            "Layout Config", _globalConfig, typeof(StationLayoutConfig), false);
        if (newGlobalConfig != _globalConfig)
            _globalConfig = newGlobalConfig;

        EditorGUILayout.Space(8);

        // ── Gaylord prefab ─────────────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        _settings.gaylordPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Gaylord Prefab", _settings.gaylordPrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            _measured = false;
            MeasureGaylord();
            EditorUtility.SetDirty(_settings);
        }

        // ── Measured dimensions (read-only) ────────────────────────────
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.FloatField("Measured Gaylord X (m)", _measuredGaylordX);
        EditorGUILayout.FloatField("Measured Gaylord Z (m)", _measuredGaylordZ);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(6);

        // ── Buffer ─────────────────────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        _settings.bufferPerSide = EditorGUILayout.Slider(
            new GUIContent("Buffer Per Side (m)", "Clearance added to each side. Total per axis = 2 × buffer."),
            _settings.bufferPerSide, 0f, 0.5f);
        if (EditorGUI.EndChangeCheck())
            EditorUtility.SetDirty(_settings);

        // ── Override toggle ────────────────────────────────────────────
        EditorGUI.BeginChangeCheck();
        _settings.overrideCellSize = EditorGUILayout.Toggle("Override Cell Size", _settings.overrideCellSize);
        if (EditorGUI.EndChangeCheck())
            EditorUtility.SetDirty(_settings);

        if (_settings.overrideCellSize)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            _settings.manualCellWidth = EditorGUILayout.FloatField("Cell Width (X, m)", _settings.manualCellWidth);
            _settings.manualCellDepth = EditorGUILayout.FloatField("Cell Depth (Z, m)", _settings.manualCellDepth);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_settings);
            EditorGUI.indentLevel--;
        }

        // ── Computed cell size preview ─────────────────────────────────
        ComputeCellSize(out float cellWidth, out float cellDepth);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical(_boxStyle);
        EditorGUILayout.LabelField("Computed Cell Size", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.FloatField("Cell Width (X, m)", cellWidth);
        EditorGUILayout.FloatField("Cell Depth (Z, m)", cellDepth);
        EditorGUI.EndDisabledGroup();
        if (_measured && !_settings.overrideCellSize)
            EditorGUILayout.LabelField(
                $"= Gaylord ({_measuredGaylordX:F3} × {_measuredGaylordZ:F3}) + 2×{_settings.bufferPerSide:F3} m buffer",
                EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        // ── Grid.json status ───────────────────────────────────────────
        EditorGUILayout.Space(8);
        string jsonPath = Path.Combine(Application.dataPath, "..", GridJsonPath).Replace('\\', '/');
        string excelPath = Path.Combine(Application.dataPath, "..", GridExcelPath).Replace('\\', '/');
        bool   jsonExists = File.Exists(jsonPath);
        bool   excelExists = File.Exists(excelPath);
        EditorGUILayout.HelpBox(
            excelExists
                ? $"{GridExcelPath} found. Rebuild will refresh {GridJsonPath} automatically."
                : $"{GridExcelPath} not found.",
            excelExists ? MessageType.Info : MessageType.Warning);

        // ── Build button ───────────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUI.BeginDisabledGroup(!excelExists || _settings.gaylordPrefab == null);
        if (GUILayout.Button("Build Station Scene", GUILayout.Height(36)))
        {
            if (GenerateGridJson())
                BuildScene(cellWidth, cellDepth);
        }
        EditorGUI.EndDisabledGroup();

        if (_settings.gaylordPrefab == null)
            EditorGUILayout.HelpBox("Assign a Gaylord Prefab to enable building.", MessageType.Warning);

        if (!Mathf.Approximately(_globalConfig.cellBuffer, _settings.bufferPerSide))
        {
            _globalConfig.cellBuffer = _settings.bufferPerSide;
            EditorUtility.SetDirty(_globalConfig);
        }

        EditorGUILayout.Space(6);
    }

    // ── Build ──────────────────────────────────────────────────────────────

    private void BuildScene(float cellWidth, float cellDepth)
    {
        // 1. Load Grid.json
        string jsonPath = Path.Combine(Application.dataPath, "..", GridJsonPath).Replace('\\', '/');
        string json     = File.ReadAllText(jsonPath);
        var raw         = JsonUtility.FromJson<GridJson>(json);
        if (raw?.cells == null)
        {
            Debug.LogError("StationGridBuilder: failed to parse Grid.json.");
            return;
        }

        // 2. Build / update StationGridData asset
        var gridData = AssetDatabase.LoadAssetAtPath<StationGridData>(GridDataAssetPath);
        if (gridData == null)
        {
            gridData = ScriptableObject.CreateInstance<StationGridData>();
            AssetDatabase.CreateAsset(gridData, GridDataAssetPath);
        }

        var cellDataArray = new CellData[raw.cells.Length];
        for (int i = 0; i < raw.cells.Length; i++)
        {
            var rc = raw.cells[i];
            cellDataArray[i] = new CellData
            {
                row       = rc.row,
                col       = rc.col,
                label     = rc.label ?? "",
                cellType  = ParseCellType(rc.cellType),
                direction = ParseDirection(rc.direction),
                isNoRobot = rc.isNoRobot,
            };
        }
        gridData.Populate(raw.rows, raw.cols, cellDataArray);
        EditorUtility.SetDirty(gridData);
        AssetDatabase.SaveAssets();

        // 3. Open / create scene
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene;
        string fullScenePath = Path.Combine(Application.dataPath, "..", ScenePath).Replace('\\', '/');
        scene = File.Exists(fullScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Station") DestroyImmediate(root);

        // 4. Build hierarchy
        var station     = new GameObject("Station");
        var gridRoot    = CreateChild("Grid",             station);
        var storageRoot = CreateChild("StorageSlots",     gridRoot);
        var corrRoot    = CreateChild("Corridors",        gridRoot);
        var chuteRoot   = CreateChild("SortChutes",       gridRoot);
        var bufferRoot  = CreateChild("EmptyCartBuffers", gridRoot);
        var queueRoot   = CreateChild("FeederQueue",      gridRoot);
        var stagingRoot = CreateChild("StagingArea",      gridRoot);
        var noRobotRoot = CreateChild("NoRobotZones",     gridRoot);
        var gaylordsRoot = CreateChild("Gaylords", station);
        CreateChild("Robots",   station);

        Vector3 origin = Vector3.zero;
        var materialCache = new Dictionary<Color, Material>();

        foreach (var cell in gridData.AllCells)
        {
            int     r   = cell.row - 1;
            int     c   = cell.col - 1;
            Vector3 pos = origin + new Vector3((c + 0.5f) * cellWidth, 0f, -(r + 0.5f) * cellDepth);

            var parent = GetParent(cell.cellType,
                storageRoot, corrRoot, chuteRoot, bufferRoot, queueRoot, stagingRoot, noRobotRoot);

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = string.IsNullOrEmpty(cell.label) ? $"Cell_{r}_{c}" : cell.label;
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Rectangular tile with 2% inner gap for visual clarity
            go.transform.localScale = new Vector3(cellWidth * 0.98f, cellDepth * 0.98f, 1f);
            DestroyImmediate(go.GetComponent<MeshCollider>());

            if (!ZoneColors.TryGetValue(cell.cellType, out Color color))
                color = Color.white;

            if (!materialCache.TryGetValue(color, out Material mat))
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
                materialCache[color] = mat;
            }
            go.GetComponent<Renderer>().sharedMaterial = mat;

            if (!string.IsNullOrEmpty(cell.label) && !IsDirectionLabel(cell.label))
                AddLabel(go.transform, cell.label, Mathf.Min(cellWidth, cellDepth));
        }

        // 5. GridMap component
        var gm        = station.AddComponent<GridMap>();
        gm.gridData   = gridData;
        gm.cellWidth  = cellWidth;
        gm.cellDepth  = cellDepth;
        gm.gridOrigin = origin;

        // 6. Gaylord database
        var gaylordDatabase = station.AddComponent<GaylordDatabase>();

        // 7. Station layout builder
        var layoutBuilder = station.AddComponent<StationLayoutBuilder>();
        layoutBuilder.gridMap              = gm;
        layoutBuilder.gaylordDatabase      = gaylordDatabase;
        layoutBuilder.gaylordPrefab        = _settings.gaylordPrefab;
        layoutBuilder.gaylordsRoot         = gaylordsRoot.transform;
        layoutBuilder.rebuildStorageOnStart = false;

        // 8. ReservationTable
        station.AddComponent<ReservationTable>();

        // 9. Charging area — right of last staging column
        var chargingGo = CreateChild("ChargingArea", station);
        var ca         = chargingGo.AddComponent<ChargingArea>();
        const int defaultChargingBays = 4;
        ca.bayCount    = defaultChargingBays;
        float bayX     = origin.x + (raw.cols + 1f) * cellWidth;
        float bayStartRow = 10f;
        float bayRowSpacing = 2f;
        for (int b = 0; b < defaultChargingBays; b++)
        {
            var bay = new GameObject($"Bay_{b}");
            bay.transform.SetParent(chargingGo.transform);
            bay.transform.position = new Vector3(
                bayX,
                origin.y,
                -(bayStartRow + b * bayRowSpacing) * cellDepth);
            ca.bayTransforms.Add(bay.transform);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"StationGridBuilder: built {ScenePath}  |  " +
                  $"Grid {raw.rows}×{raw.cols}  |  " +
                  $"Cell {cellWidth:F3} m (W) × {cellDepth:F3} m (D)");
    }

    private bool GenerateGridJson()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string excelPath = Path.Combine(projectRoot, GridExcelPath);
        string scriptPath = Path.Combine(projectRoot, GridParserScriptPath);

        if (!File.Exists(excelPath))
        {
            Debug.LogError($"StationGridBuilder: missing Excel source at {GridExcelPath}.");
            return false;
        }

        if (!File.Exists(scriptPath))
        {
            Debug.LogError($"StationGridBuilder: missing parser script at {GridParserScriptPath}.");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{GridParserScriptPath}\"",
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    Debug.LogError("StationGridBuilder: failed to start node process.");
                    return false;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(stdout))
                    Debug.Log(stdout.Trim());

                if (process.ExitCode != 0)
                {
                    if (!string.IsNullOrWhiteSpace(stderr))
                        Debug.LogError(stderr.Trim());
                    Debug.LogError($"StationGridBuilder: Grid parser failed with exit code {process.ExitCode}.");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                    Debug.LogWarning(stderr.Trim());
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError(
                "StationGridBuilder: failed to run node. " +
                "Make sure Node.js is installed and available on PATH.\n" + ex.Message);
            return false;
        }

        AssetDatabase.Refresh();
        return true;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ComputeCellSize(out float w, out float d)
    {
        if (_settings.overrideCellSize)
        {
            w = _settings.manualCellWidth;
            d = _settings.manualCellDepth;
            return;
        }
        float buf = _globalConfig != null ? _globalConfig.cellBuffer : 0.2f;
        w = _measuredGaylordX > 0f ? _measuredGaylordX + 2f * buf : 1.4f;
        d = _measuredGaylordZ > 0f ? _measuredGaylordZ + 2f * buf : 1.2f;
    }

    private void MeasureGaylord()
    {
        _measuredGaylordX = 0f;
        _measuredGaylordZ = 0f;
        _measured         = false;

        var prefab = _settings?.gaylordPrefab;
        if (prefab == null)
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GaylordPrefabPath);
        if (prefab == null) return;

        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (tmp == null) return;
        try
        {
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = tmp.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            _measuredGaylordX = b.size.x;
            _measuredGaylordZ = b.size.z;
            _measured         = true;

            // Auto-assign prefab to settings if not set
            if (_settings != null && _settings.gaylordPrefab == null)
            {
                _settings.gaylordPrefab = prefab;
                EditorUtility.SetDirty(_settings);
            }
        }
        finally { DestroyImmediate(tmp); }
    }

    private static StationBuilderSettings LoadOrCreateSettings()
    {
        var s = AssetDatabase.LoadAssetAtPath<StationBuilderSettings>(SettingsAssetPath);
        if (s != null) return s;

        s = CreateInstance<StationBuilderSettings>();
        // Auto-assign gaylord prefab
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GaylordPrefabPath);
        if (prefab != null) s.gaylordPrefab = prefab;

        string dir = Path.GetDirectoryName(SettingsAssetPath);
        if (!Directory.Exists(Path.Combine(Application.dataPath, "..", dir).Replace('\\', '/')))
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", dir).Replace('\\', '/'));

        AssetDatabase.CreateAsset(s, SettingsAssetPath);
        AssetDatabase.SaveAssets();
        return s;
    }

    private static StationLayoutConfig LoadOrCreateGlobalConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<StationLayoutConfig>(GlobalConfigAssetPath);
        if (config != null) return config;

        config = CreateInstance<StationLayoutConfig>();

        string dir = Path.GetDirectoryName(GlobalConfigAssetPath);
        if (!Directory.Exists(Path.Combine(Application.dataPath, "..", dir).Replace('\\', '/')))
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", dir).Replace('\\', '/'));

        AssetDatabase.CreateAsset(config, GlobalConfigAssetPath);
        AssetDatabase.SaveAssets();
        return config;
    }

    private static GameObject CreateChild(string name, GameObject parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, worldPositionStays: false);
        return go;
    }

    private static StationLayoutBuilder EnsureStationLayoutBuilder(GameObject station, StationBuilderSettings settings)
    {
        var gridMap = station.GetComponent<GridMap>();
        if (gridMap == null || gridMap.gridData == null)
        {
            Debug.LogError("StationGridBuilder: Station root is missing GridMap or gridData.");
            return null;
        }

        var gaylordsRoot = station.transform.Find("Gaylords");
        if (gaylordsRoot == null)
        {
            Debug.LogError("StationGridBuilder: Station root is missing the 'Gaylords' child.");
            return null;
        }

        var layoutBuilder = station.GetComponent<StationLayoutBuilder>();
        if (layoutBuilder == null)
            layoutBuilder = station.AddComponent<StationLayoutBuilder>();

        var gaylordDatabase = station.GetComponent<GaylordDatabase>();
        if (gaylordDatabase == null)
            gaylordDatabase = station.AddComponent<GaylordDatabase>();

        layoutBuilder.gridMap = gridMap;
        layoutBuilder.gaylordDatabase = gaylordDatabase;
        layoutBuilder.gaylordsRoot = gaylordsRoot;
        layoutBuilder.gaylordPrefab = settings != null ? settings.gaylordPrefab : null;
        layoutBuilder.rebuildStorageOnStart = false;
        return layoutBuilder;
    }

    private static Transform GetParent(CellType type,
        GameObject storage, GameObject corridor, GameObject chute,
        GameObject buffer,  GameObject queue,   GameObject staging, GameObject noRobot)
    {
        switch (type)
        {
            case CellType.Storage:      return storage.transform;
            case CellType.Corridor:     return corridor.transform;
            case CellType.SortChute:    return chute.transform;
            case CellType.EmptyBuffer:  return buffer.transform;
            case CellType.FeederQueue:  return queue.transform;
            case CellType.Staging:      return staging.transform;
            case CellType.InductZone:
            case CellType.SorterSystem: return noRobot.transform;
            default:                    return corridor.transform;
        }
    }

    private static void AddLabel(Transform parent, string text, float cellMin)
    {
        var go = new GameObject("_label");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = new Vector3(0f, 0.001f, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one * cellMin * 0.006f;
        var tm      = go.AddComponent<TextMesh>();
        tm.text     = text;
        tm.fontSize = 120;
        tm.anchor   = TextAnchor.MiddleCenter;
        tm.color    = Color.black;
    }

    private static bool IsDirectionLabel(string s)
    {
        switch (s.ToLowerInvariant())
        {
            case "up": case "down": case "left": case "right": case "any": return true;
            default: return false;
        }
    }

    private static CellType ParseCellType(string s)
    {
        if (System.Enum.TryParse<CellType>(s, out var t)) return t;
        return CellType.Empty;
    }

    private static CorridorDirection ParseDirection(string s)
    {
        if (string.IsNullOrEmpty(s)) return CorridorDirection.None;
        switch (s.ToLowerInvariant())
        {
            case "up":    return CorridorDirection.Up;
            case "down":  return CorridorDirection.Down;
            case "left":  return CorridorDirection.Left;
            case "right": return CorridorDirection.Right;
            case "any":   return CorridorDirection.Any;
            default:      return CorridorDirection.None;
        }
    }

    // ── JSON shims ─────────────────────────────────────────────────────────

    [System.Serializable] private class GridJson  { public int rows; public int cols; public RawCell[] cells; }
    [System.Serializable] private class RawCell   { public int row; public int col; public string label; public string cellType; public string direction; public bool isNoRobot; }
}
