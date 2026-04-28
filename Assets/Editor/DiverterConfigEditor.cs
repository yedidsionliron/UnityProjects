using UnityEditor;
using UnityEngine;
using PCS;

/// <summary>
/// Custom inspector for DiverterConfig.
/// Lives in Assembly-CSharp-Editor so it can see both the PCS assembly
/// (PCSConfig) and the ConveyorEditor assembly (DiverterConfig, Diverter, SortPoint).
///
/// DiverterConfig lives on the same GameObject as PCSConfig. Reset() adds PCSConfig
/// automatically. Build() drives PCSConfig to size the belt, then creates SortPoints.
/// </summary>
[CustomEditor(typeof(DiverterConfig))]
public class DiverterConfigEditor : Editor
{
    private readonly struct GaylordMeasurements
    {
        public readonly float Width;
        public readonly float Height;
        public readonly float Depth;

        public GaylordMeasurements(float width, float height, float depth)
        {
            Width = width;
            Height = height;
            Depth = depth;
        }
    }

    private readonly struct DiverterBuildMetrics
    {
        public readonly GaylordMeasurements Gaylord;
        public readonly float SlotPitch;
        public readonly float BeltSurfaceWidth;
        public readonly float PcsWidth;
        public readonly float GaylordHalfX;
        public readonly float TargetBeltLength;
        public readonly float ConveyorHeight;

        public DiverterBuildMetrics(
            GaylordMeasurements gaylord,
            float slotPitch,
            float beltSurfaceWidth,
            float pcsWidth,
            float gaylordHalfX,
            float targetBeltLength,
            float conveyorHeight)
        {
            Gaylord = gaylord;
            SlotPitch = slotPitch;
            BeltSurfaceWidth = beltSurfaceWidth;
            PcsWidth = pcsWidth;
            GaylordHalfX = gaylordHalfX;
            TargetBeltLength = targetBeltLength;
            ConveyorHeight = conveyorHeight;
        }
    }

    // Called when the component is first added to a GameObject.
    private void Reset()
    {
        var cfg = (DiverterConfig)target;

        // Ensure PCSConfig exists on the same GameObject.
        if (cfg.GetComponent<PCSConfig>() == null)
            Undo.AddComponent<PCSConfig>(cfg.gameObject);

        // Ensure Diverter exists on the same GameObject.
        if (cfg.GetComponent<Diverter>() == null)
            Undo.AddComponent<Diverter>(cfg.gameObject);

        // Auto-find AddressInit in the scene.
        var addressInit = FindAnyObjectByType<AddressInit>(FindObjectsInactive.Include);
        if (addressInit != null)
            cfg.addressInit = addressInit;

        // Auto-load gaylord prefab and initialise dimensions from its actual size.
        var gaylord = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LastMileAssets/Prefabs/Gaylord.prefab");
        if (gaylord != null)
        {
            cfg.gaylordPrefab = gaylord;
            if (TryMeasureGaylord(gaylord, out var measured))
            {
                cfg.gaylordWidth = measured.Depth;
                cfg.gaylordDepth = measured.Width * 0.5f;
            }
        }

        EditorUtility.SetDirty(cfg);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var cfg = (DiverterConfig)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(
            $"Belt length: ≥{cfg.BeltLength:F2} m (slot pitch clamped to gaylord footprint if larger)  |  Sort points: {cfg.numDivertPoints * 2}",
            EditorStyles.helpBox);
        EditorGUILayout.Space(4);

        if (GUILayout.Button("Build", GUILayout.Height(32)))
            Build(cfg);
    }

    private void Build(DiverterConfig cfg)
    {
        if (!Validate(cfg)) return;

        Undo.RegisterFullObjectHierarchyUndo(cfg.gameObject, "DiverterConfig Build");

        // Destroy all __GaylordColliders objects in the scene — at root, under the DiverterConfig,
        // or anywhere else. Colliders are runtime-only and must never be saved in the scene.
        foreach (var go in cfg.gameObject.scene.GetRootGameObjects())
            if (go.name == "__GaylordColliders")
                Undo.DestroyObjectImmediate(go);
        // Also sweep every transform in the scene (handles nested leftovers from old builds).
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.gameObject.name == "__GaylordColliders")
                Undo.DestroyObjectImmediate(t.gameObject);

        PCSConfig pcs      = cfg.GetComponent<PCSConfig>();
        Diverter  diverter = cfg.GetComponent<Diverter>();

        DiverterBuildMetrics metrics = DeriveBuildMetrics(cfg, pcs);
        cfg.gaylordWidth = metrics.SlotPitch;
        cfg.gaylordDepth = metrics.GaylordHalfX;
        EditorUtility.SetDirty(cfg);

        // 2. Size and rebuild the conveyor belt with all railings disabled.
        if (diverter.beltSpeed > 0f)
            pcs.speed = diverter.beltSpeed;
        pcs.width = metrics.PcsWidth;
        pcs.height = metrics.ConveyorHeight;
        pcs.length = ComputeTileCount(pcs, metrics.TargetBeltLength);
        pcs.CheckRailingData();
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < pcs.railingData[side].enabledStates.Count; i++)
                pcs.railingData[side].enabledStates[i] = false;
        pcs.CreatePCS();
        EditorUtility.SetDirty(pcs);

        // 3. Measure actual belt geometry post-rebuild.
        //    Lock beltLength to the gaylord row length so runtime trigger zones align with gaylords.
        //    Use the measured belt surface width for X placement so Gaylords sit adjacent to the
        //    actual belt edge, not the wider outer frame width.
        diverter.numDivertPoints = cfg.numDivertPoints;
        diverter.MeasureBelt();
        diverter.beltLength = metrics.TargetBeltLength;
        var conv = pcs.GetComponentInChildren<PCSConveyor>();
        diverter.beltSpeed = conv != null ? conv.speed : 0f;
        float beltHalfX = diverter.triggerWidth * 0.5f;
        float groundWorldY = pcs.transform.position.y;
        EditorUtility.SetDirty(diverter);

        // 4. Create SortPoints + gaylords + assign address ranges.
        float gaylordZScale = metrics.Gaylord.Depth > 0f ? metrics.SlotPitch / metrics.Gaylord.Depth : 1f;
        cfg.BuildSortPoints(diverter, beltHalfX, metrics.GaylordHalfX, metrics.SlotPitch, gaylordZScale, groundWorldY);
        diverter.sortPoints = cfg.sortPoints;

        EditorUtility.SetDirty(diverter);

        Debug.Log($"DiverterConfig: gaylordWidth={metrics.Gaylord.Width:F3} m  gaylordDepth={metrics.Gaylord.Depth:F3} m  " +
                  $"gaylordHeight={metrics.Gaylord.Height:F3} m  slotPitch={metrics.SlotPitch:F3} m  beltHalfX={beltHalfX:F3} m  " +
                  $"→ {cfg.numDivertPoints} positions, belt={metrics.TargetBeltLength:F2} m × {pcs.width:F3} m ({pcs.length} tiles).", cfg);
    }

    private bool Validate(DiverterConfig cfg)
    {
        var pcs = cfg.GetComponent<PCSConfig>();
        if (pcs == null)
        {
            Debug.LogError("DiverterConfig: no PCSConfig on this GameObject. Remove and re-add DiverterConfig to auto-create it.", cfg);
            return false;
        }
        if (!pcs.settingsImported)
        {
            Debug.LogError("DiverterConfig: PCSConfig has not been configured yet (settingsImported = false). " +
                           "Assign the belt and cap prefabs in PCSConfig, then press Build.", cfg);
            return false;
        }
        if (cfg.GetComponent<Diverter>() == null)
        {
            Debug.LogError("DiverterConfig: no Diverter on this GameObject. Remove and re-add DiverterConfig to auto-create it.", cfg);
            return false;
        }
        if (cfg.addressInit   == null) { Debug.LogError("DiverterConfig: addressInit not assigned.",   cfg); return false; }
        if (cfg.gaylordPrefab == null) { Debug.LogError("DiverterConfig: gaylordPrefab not assigned.", cfg); return false; }
        return true;
    }

    private int ComputeTileCount(PCSConfig pcs, float targetLength)
    {
        // belt.positionOffset.z is the per-tile Z step PCS uses when placing tiles
        // (line: endZ = startCap.positionOffset.z + belt.positionOffset.z * length).
        // Reading it directly avoids building temporary belts and trying to measure
        // renderer bounds after CombineMeshes, which is unreliable in edit mode.
        float tileWidth = pcs.belt.positionOffset.z;

        if (tileWidth <= 0f)
        {
            // Fallback: derive tile width from the belt prefab's renderer bounds.
            var r = pcs.belt.prefab != null ? pcs.belt.prefab.GetComponent<Renderer>() : null;
            tileWidth = r != null ? r.bounds.size.z : 0f;
        }

        if (tileWidth <= 0f)
        {
            Debug.LogWarning("DiverterConfig: could not measure belt tile width — defaulting to 1 tile per metre.", pcs);
            return Mathf.Max(1, Mathf.RoundToInt(targetLength));
        }

        // Use CeilToInt so the physical belt is never shorter than the gaylord row.
        int count = Mathf.Max(1, Mathf.CeilToInt(targetLength / tileWidth));
        Debug.Log($"DiverterConfig: tile width = {tileWidth:F3} m  →  {count} tiles for {targetLength:F2} m belt.", pcs);
        return count;
    }

    private DiverterBuildMetrics DeriveBuildMetrics(DiverterConfig cfg, PCSConfig pcs)
    {
        GaylordMeasurements measured = TryMeasureGaylord(cfg.gaylordPrefab, out var gaylord)
            ? gaylord
            : new GaylordMeasurements(cfg.gaylordDepth * 2f, 0f, cfg.gaylordWidth);

        float slotPitch = measured.Depth > 0f ? measured.Depth : cfg.gaylordWidth;
        float beltSurfaceWidth = measured.Width > 0f ? measured.Width : cfg.gaylordDepth * 2f;
        float gaylordHalfX = beltSurfaceWidth * 0.5f;
        float pcsWidth = beltSurfaceWidth + pcs.GetSideFrameAllowance();
        float targetBeltLength = cfg.numDivertPoints * slotPitch;
        float supportHeight = measured.Height > 0f
            ? pcs.GetHeightForBeltTop(measured.Height, beltSurfaceWidth)
            : pcs.height;

        return new DiverterBuildMetrics(
            measured,
            slotPitch,
            beltSurfaceWidth,
            pcsWidth,
            gaylordHalfX,
            targetBeltLength,
            supportHeight);
    }

    private bool TryMeasureGaylord(GameObject prefab, out GaylordMeasurements measured)
    {
        measured = default;
        if (prefab == null) return false;

        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (tmp == null) return false;

        try
        {
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = tmp.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0) return false;

            Bounds wb = renderers[0].bounds;
            foreach (var r in renderers) wb.Encapsulate(r.bounds);
            measured = new GaylordMeasurements(wb.size.x, wb.size.y, wb.size.z);
            return true;
        }
        finally
        {
            DestroyImmediate(tmp);
        }
    }
}
