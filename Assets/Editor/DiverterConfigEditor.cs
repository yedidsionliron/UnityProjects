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
        var addressInit = FindFirstObjectByType<AddressInit>(FindObjectsInactive.Include);
        if (addressInit != null)
            cfg.addressInit = addressInit;

        // Auto-load gaylord prefab and initialise dimensions from its actual size.
        var gaylord = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LastMileAssets/Prefabs/Gaylord.prefab");
        if (gaylord != null)
        {
            cfg.gaylordPrefab = gaylord;
            float footprintZ = MeasureGaylordFootprintZ(gaylord);
            if (footprintZ > 0f) cfg.gaylordWidth = footprintZ;
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

        // 1. Measure the gaylord prefab's actual Z footprint so adjacent gaylords never overlap.
        float gaylordFootprintZ  = MeasureGaylordFootprintZ(cfg.gaylordPrefab);
        float effectiveSlotPitch = Mathf.Max(cfg.gaylordWidth, gaylordFootprintZ);
        float targetBeltLength   = cfg.numDivertPoints * effectiveSlotPitch;

        if (gaylordFootprintZ > cfg.gaylordWidth)
            Debug.LogWarning($"DiverterConfig: gaylordWidth ({cfg.gaylordWidth:F3} m) is smaller than the " +
                             $"prefab's Z footprint ({gaylordFootprintZ:F3} m). Slot pitch clamped to {effectiveSlotPitch:F3} m.", cfg);

        // 2. Size and rebuild the conveyor belt with all railings disabled.
        float gaylordFootprintX = MeasureGaylordFootprintX(cfg.gaylordPrefab);
        if (gaylordFootprintX > 0f) pcs.width = gaylordFootprintX;
        pcs.length = ComputeTileCount(pcs, targetBeltLength);
        pcs.CheckRailingData();
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < pcs.railingData[side].enabledStates.Count; i++)
                pcs.railingData[side].enabledStates[i] = false;
        pcs.CreatePCS();
        EditorUtility.SetDirty(pcs);

        // 3. Measure actual belt geometry post-rebuild.
        //    Lock beltLength to the gaylord row length so runtime trigger zones align with gaylords.
        //    Use pcs.width (the configured total belt width) for gaylord X placement — renderer bounds
        //    only capture the belt surface mesh, which is (width-0.4) wide when railings are disabled,
        //    leaving the 0.2 m side-frame unaccounted and causing gaylords to overlap the belt.
        diverter.numDivertPoints = cfg.numDivertPoints;
        diverter.MeasureBelt();
        diverter.beltLength = targetBeltLength;
        var conv = pcs.GetComponentInChildren<PCSConveyor>();
        diverter.beltSpeed = conv != null ? conv.speed : 0f;
        float beltHalfX = pcs.width / 2f * pcs.transform.lossyScale.x;
        // Ground level = bottom of conveyor supports, computed from PCSConfig's support height.
        float groundWorldY = pcs.transform.TransformPoint(new Vector3(0f, -(2f * pcs.conveyorSupportHeight), 0f)).y;
        EditorUtility.SetDirty(diverter);

        // 4. Create SortPoints + gaylords + assign address ranges.
        float gaylordZScale = gaylordFootprintZ > 0f ? effectiveSlotPitch / gaylordFootprintZ : 1f;
        cfg.BuildSortPoints(diverter, beltHalfX, effectiveSlotPitch, gaylordZScale, groundWorldY);
        diverter.sortPoints = cfg.sortPoints;

        EditorUtility.SetDirty(diverter);

        Debug.Log($"DiverterConfig: gaylordFootprintX={gaylordFootprintX:F3} m  gaylordFootprintZ={gaylordFootprintZ:F3} m  " +
                  $"effectiveSlotPitch={effectiveSlotPitch:F3} m  beltHalfX={beltHalfX:F3} m  " +
                  $"→ {cfg.numDivertPoints} positions, belt={targetBeltLength:F2} m × {pcs.width:F3} m ({pcs.length} tiles).", cfg);
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

    private float MeasureBeltLength(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0) return 0f;

        Bounds wb = renderers[0].bounds;
        foreach (var r in renderers) wb.Encapsulate(r.bounds);

        Vector3 fwd = go.transform.forward;
        return Mathf.Abs(Vector3.Dot(wb.extents,
            new Vector3(Mathf.Abs(fwd.x), Mathf.Abs(fwd.y), Mathf.Abs(fwd.z)))) * 2f;
    }

    /// <summary>
    /// Instantiates the gaylord prefab at the world origin, measures its Z extent
    /// (depth along belt direction when localRotation = identity), then destroys it.
    /// Returns 0 if measurement fails.
    /// </summary>
    private float MeasureGaylordFootprintX(GameObject prefab)
    {
        if (prefab == null) return 0f;

        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (tmp == null) return 0f;

        try
        {
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = tmp.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0) return 0f;

            Bounds wb = renderers[0].bounds;
            foreach (var r in renderers) wb.Encapsulate(r.bounds);
            return wb.size.x;
        }
        finally
        {
            DestroyImmediate(tmp);
        }
    }

    private float MeasureGaylordFootprintZ(GameObject prefab)
    {
        if (prefab == null) return 0f;

        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (tmp == null) return 0f;

        try
        {
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = tmp.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0) return 0f;

            Bounds wb = renderers[0].bounds;
            foreach (var r in renderers) wb.Encapsulate(r.bounds);
            return wb.size.z;
        }
        finally
        {
            DestroyImmediate(tmp);
        }
    }
}
