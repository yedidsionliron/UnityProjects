using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Rebuilds scene layout elements from the logical station grid.
/// Populates station Gaylords from the logical station grid.
/// Currently supports numbered storage cells and E# empty-buffer cells.
/// </summary>
public class StationLayoutBuilder : MonoBehaviour
{
    [Header("References")]
    public GridMap gridMap;
    public GaylordDatabase gaylordDatabase;
    public GameObject gaylordPrefab;
    public Transform gaylordsRoot;

    [Header("Startup")]
    [Tooltip("When enabled, repopulates managed Gaylord cells on play if the managed root is empty.")]
    public bool rebuildStorageOnStart;

    [SerializeField]
    private string storageRootName = "StorageGaylords";

    void Start()
    {
        if (!Application.isPlaying)
            return;

        if (!TryResolveReferences())
            return;

        if (rebuildStorageOnStart && GetManagedStorageRoot().childCount == 0)
            RebuildStorageGaylords();

        SyncStorageOccupancy();
    }

    [ContextMenu("Rebuild Station Gaylords")]
    public void RebuildStorageGaylords()
    {
        if (!TryResolveReferences())
            return;

        var managedRoot = GetManagedStorageRoot();
        ClearChildren(managedRoot);
        RemoveDatabaseEntriesForCells(ShouldPlaceGaylordAt);

        foreach (var cell in gridMap.gridData.AllCells)
        {
            if (!ShouldPlaceGaylordAt(cell))
                continue;

            var instance = InstantiateGaylord(managedRoot);
            if (instance == null)
                continue;

            string gaylordId = BuildGaylordId(cell.label);
            instance.name = gaylordId;
            instance.transform.SetPositionAndRotation(gridMap.CellToWorld(cell.Index), Quaternion.identity);
            AlignInstanceToCell(instance, gridMap.CellToWorld(cell.Index), gridMap.gridOrigin.y);
            AttachRuntimeMetadata(instance, gaylordId, cell.label, cell.Index);
            gaylordDatabase.AddOrReplace(GaylordRecord.CreateDefault(gaylordId, cell.label, cell.Index));
        }

        SyncStorageOccupancy();
    }

    [ContextMenu("Register SortChute Gaylords From Diverter")]
    public void RegisterSortChuteGaylordsFromDiverter()
    {
        if (!TryResolveReferences())
            return;

        Transform diverterRoot = transform.Find("StationDiverter");
        if (diverterRoot == null)
        {
            Debug.LogError("StationLayoutBuilder: 'StationDiverter' root not found.", this);
            return;
        }

        RegisterSortChuteGaylordsFromDiverter(diverterRoot);
    }

    public void RegisterSortChuteGaylordsFromDiverter(Transform diverterRoot)
    {
        if (!TryResolveReferences() || diverterRoot == null)
            return;

        var sortLabels = GetOrderedSortChuteLabels(includeOverflow: false);
        var sortPoints = diverterRoot.GetComponentsInChildren<SortPoint>(includeInactive: true);
        System.Array.Sort(sortPoints, CompareSortPoints);

        if (sortPoints.Length != sortLabels.Count)
        {
            Debug.LogError(
                $"StationLayoutBuilder: Diverter sort-point count ({sortPoints.Length}) does not match grid S-cell count ({sortLabels.Count}).",
                this);
            return;
        }

        RemoveDatabaseEntriesForCells(IsSortChuteCell);

        for (int i = 0; i < sortPoints.Length; i++)
            RegisterSortPointGaylord(sortPoints[i], sortLabels[i]);

        string overflowLabel = GetOverflowSortChuteLabel();
        if (!string.IsNullOrEmpty(overflowLabel))
            RegisterOverflowGaylord(diverterRoot, overflowLabel);
    }

    [ContextMenu("Sync Station Gaylord Occupancy")]
    public void SyncStorageOccupancy()
    {
        if (!TryResolveReferences() || !Application.isPlaying)
            return;

        foreach (var cell in gridMap.gridData.AllCells)
        {
            if (ShouldPlaceGaylordAt(cell) || IsSortChuteCell(cell))
                gridMap.SetOccupied(cell.Index, null);
        }

        var managedRoot = FindManagedStorageRoot();
        if (managedRoot == null)
            return;

        foreach (Transform child in managedRoot)
        {
            if (!TryGetLabelFromName(child.name, out string label))
                continue;

            if (gridMap.gridData.TryResolveLabel(label, out Vector2Int cell))
                gridMap.SetOccupied(cell, child.gameObject);
        }

        Transform diverterRoot = transform.Find("StationDiverter");
        if (diverterRoot == null)
            return;

        var runtimeGaylords = diverterRoot.GetComponentsInChildren<GaylordRuntime>(includeInactive: true);
        foreach (var runtime in runtimeGaylords)
        {
            if (runtime == null || !IsSortChuteLabel(runtime.gridLabel))
                continue;

            gridMap.SetOccupied(runtime.gridCell, runtime.gameObject);
        }
    }

    private bool TryResolveReferences()
    {
        if (gridMap == null)
            gridMap = GetComponent<GridMap>();

        if (gaylordDatabase == null)
            gaylordDatabase = GetComponent<GaylordDatabase>();

        if (gaylordsRoot == null)
        {
            Transform existingRoot = transform.Find("Gaylords");
            if (existingRoot != null)
                gaylordsRoot = existingRoot;
        }

        if (gridMap == null || gridMap.gridData == null)
        {
            Debug.LogError("StationLayoutBuilder: GridMap with StationGridData is required.", this);
            return false;
        }

        if (gaylordDatabase == null)
        {
            Debug.LogError("StationLayoutBuilder: GaylordDatabase is required.", this);
            return false;
        }

        if (gaylordPrefab == null)
        {
            Debug.LogError("StationLayoutBuilder: gaylordPrefab is not assigned.", this);
            return false;
        }

        if (gaylordsRoot == null)
        {
            Debug.LogError("StationLayoutBuilder: gaylordsRoot is not assigned.", this);
            return false;
        }

        return true;
    }

    private Transform GetManagedStorageRoot()
    {
        Transform managedRoot = FindManagedStorageRoot();
        if (managedRoot != null)
            return managedRoot;

        var go = new GameObject(storageRootName);
        go.transform.SetParent(gaylordsRoot, worldPositionStays: false);
        return go.transform;
    }

    private Transform FindManagedStorageRoot() =>
        gaylordsRoot != null ? gaylordsRoot.Find(storageRootName) : null;

    private GameObject InstantiateGaylord(Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return PrefabUtility.InstantiatePrefab(gaylordPrefab, parent) as GameObject;
#endif
        return Instantiate(gaylordPrefab, parent);
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i).gameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(child);
            else
#endif
                Destroy(child);
        }
    }

    private static void AlignInstanceToCell(GameObject instance, Vector3 cellCenter, float groundY)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0)
        {
            instance.transform.position = new Vector3(cellCenter.x, groundY, cellCenter.z);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 correction = new Vector3(
            cellCenter.x - bounds.center.x,
            groundY - bounds.min.y,
            cellCenter.z - bounds.center.z);

        instance.transform.position += correction;
    }

    private static void AttachRuntimeMetadata(GameObject instance, string gaylordId, string gridLabel, Vector2Int gridCell)
    {
        var runtime = instance.GetComponent<GaylordRuntime>();
        if (runtime == null)
            runtime = instance.AddComponent<GaylordRuntime>();

        runtime.gaylordId = gaylordId;
        runtime.gridLabel = gridLabel;
        runtime.gridCell = gridCell;
    }

    private static string BuildGaylordId(string gridLabel) => $"G-{gridLabel}";

    private void RegisterSortPointGaylord(SortPoint sortPoint, string gridLabel)
    {
        if (sortPoint == null || !gridMap.gridData.TryResolveLabel(gridLabel, out Vector2Int gridCell))
            return;

        GameObject gaylord = FindPrimaryGaylord(sortPoint.transform);
        if (gaylord == null)
        {
            Debug.LogWarning($"StationLayoutBuilder: no Gaylord found under sort point '{sortPoint.name}'.", this);
            return;
        }

        RegisterGaylordInstance(gaylord, gridLabel, gridCell);
    }

    private void RegisterOverflowGaylord(Transform diverterRoot, string gridLabel)
    {
        if (!gridMap.gridData.TryResolveLabel(gridLabel, out Vector2Int gridCell))
            return;

        Transform overflowRoot = diverterRoot.Find("OverflowGaylord");
        if (overflowRoot == null)
        {
            Debug.LogWarning("StationLayoutBuilder: overflow Gaylord root not found under diverter.", this);
            return;
        }

        GameObject gaylord = FindPrimaryGaylord(overflowRoot);
        if (gaylord == null)
        {
            Debug.LogWarning("StationLayoutBuilder: overflow Gaylord instance not found.", this);
            return;
        }

        RegisterGaylordInstance(gaylord, gridLabel, gridCell);
    }

    private void RegisterGaylordInstance(GameObject instance, string gridLabel, Vector2Int gridCell)
    {
        string gaylordId = BuildGaylordId(gridLabel);
        instance.name = gaylordId;
        AttachRuntimeMetadata(instance, gaylordId, gridLabel, gridCell);
        gaylordDatabase.AddOrReplace(GaylordRecord.CreateDefault(gaylordId, gridLabel, gridCell));
    }

    private void RemoveDatabaseEntriesForCells(System.Predicate<CellData> match)
    {
        if (match == null)
            return;

        foreach (var cell in gridMap.gridData.AllCells)
        {
            if (match(cell))
                gaylordDatabase.Remove(BuildGaylordId(cell.label));
        }
    }

    private List<string> GetOrderedSortChuteLabels(bool includeOverflow)
    {
        var labels = new List<string>();
        foreach (var cell in gridMap.gridData.AllCells)
        {
            if (!IsSortChuteCell(cell))
                continue;

            bool isOverflow = string.Equals(cell.label, "S29");
            if (isOverflow == includeOverflow)
                labels.Add(cell.label);
        }

        labels.Sort(CompareGridLabels);
        return labels;
    }

    private string GetOverflowSortChuteLabel()
    {
        foreach (var cell in gridMap.gridData.AllCells)
        {
            if (IsSortChuteCell(cell) && string.Equals(cell.label, "S29"))
                return cell.label;
        }

        return null;
    }

    private static bool ShouldPlaceGaylordAt(CellData cell)
    {
        if (cell == null || string.IsNullOrEmpty(cell.label))
            return false;

        switch (cell.cellType)
        {
            case CellType.Storage:
                return IsNumericLabel(cell.label);

            case CellType.EmptyBuffer:
                return IsEmptyBufferLabel(cell.label);

            default:
                return false;
        }
    }

    private static bool IsSortChuteCell(CellData cell) =>
        cell != null &&
        cell.cellType == CellType.SortChute &&
        IsSortChuteLabel(cell.label);

    private static bool IsNumericLabel(string label) =>
        !string.IsNullOrEmpty(label) &&
        int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsEmptyBufferLabel(string label)
    {
        if (string.IsNullOrEmpty(label) || label.Length < 2 || label[0] != 'E')
            return false;

        return int.TryParse(label.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsSortChuteLabel(string label)
    {
        if (string.IsNullOrEmpty(label) || label.Length < 2 || label[0] != 'S')
            return false;

        return int.TryParse(label.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static int CompareGridLabels(string a, string b)
    {
        int aNumber = ExtractTrailingNumber(a);
        int bNumber = ExtractTrailingNumber(b);
        int numberCompare = aNumber.CompareTo(bNumber);
        return numberCompare != 0 ? numberCompare : string.Compare(a, b, true, CultureInfo.InvariantCulture);
    }

    private static int CompareSortPoints(SortPoint a, SortPoint b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;
        return a.id.CompareTo(b.id);
    }

    private static int ExtractTrailingNumber(string label)
    {
        if (string.IsNullOrEmpty(label))
            return int.MinValue;

        int start = 1;
        return int.TryParse(label.Substring(start), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : int.MinValue;
    }

    private static GameObject FindPrimaryGaylord(Transform root)
    {
        if (root == null)
            return null;

        var runtime = root.GetComponentInChildren<GaylordContainer>(includeInactive: true);
        if (runtime != null)
            return runtime.gameObject;

        if (root.childCount > 0)
            return root.GetChild(0).gameObject;

        return null;
    }

    private static bool TryGetLabelFromName(string objectName, out string label)
    {
        const string prefix = "G-";
        if (objectName.StartsWith(prefix))
        {
            label = objectName.Substring(prefix.Length);
            return IsNumericLabel(label) || IsEmptyBufferLabel(label) || IsSortChuteLabel(label);
        }

        label = null;
        return false;
    }
}
