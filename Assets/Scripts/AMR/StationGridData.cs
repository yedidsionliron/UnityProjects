using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that holds the parsed grid layout from Grid.json.
/// Populated by the StationGridBuilder editor tool (Tools > Build Station Scene).
/// </summary>
[CreateAssetMenu(menuName = "AMR/Station Grid Data", fileName = "StationGridData")]
public class StationGridData : ScriptableObject
{
    public int rows;
    public int cols;

    [SerializeField]
    private CellData[] _cells;

    // ── Fast lookup ────────────────────────────────────────────────────────
    // Built on first access; not serialised (rebuilt after domain reloads).
    private Dictionary<Vector2Int, CellData> _index;
    private Dictionary<string, Vector2Int>   _labelIndex;

    // ── Populate from parsed JSON ──────────────────────────────────────────

    public void Populate(int rows, int cols, CellData[] cells)
    {
        this.rows = rows;
        this.cols = cols;
        _cells     = cells;
        _index     = null;
        _labelIndex = null;
    }

    // ── Accessors ──────────────────────────────────────────────────────────

    public CellData[] AllCells => _cells;

    /// <summary>Get cell by 0-based index (row, col).</summary>
    public CellData GetCell(int row, int col)
    {
        BuildIndex();
        _index.TryGetValue(new Vector2Int(row, col), out var cell);
        return cell;
    }

    /// <summary>Get cell by 0-based index.</summary>
    public CellData GetCell(Vector2Int idx) => GetCell(idx.x, idx.y);

    /// <summary>
    /// Resolve a label string (e.g. "S5", "147", "St12") to its 0-based grid index.
    /// Returns false if the label is not found.
    /// </summary>
    public bool TryResolveLabel(string label, out Vector2Int idx)
    {
        BuildLabelIndex();
        return _labelIndex.TryGetValue(label, out idx);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void BuildIndex()
    {
        if (_index != null) return;
        _index = new Dictionary<Vector2Int, CellData>(_cells?.Length ?? 0);
        if (_cells == null) return;
        foreach (var c in _cells)
            _index[new Vector2Int(c.row - 1, c.col - 1)] = c;
    }

    private void BuildLabelIndex()
    {
        if (_labelIndex != null) return;
        BuildIndex();
        _labelIndex = new Dictionary<string, Vector2Int>(StringComparer.OrdinalIgnoreCase);
        if (_cells == null) return;
        foreach (var c in _cells)
        {
            if (!string.IsNullOrEmpty(c.label) && !IsDirectionLabel(c.label))
            {
                var key = new Vector2Int(c.row - 1, c.col - 1);
                // First occurrence wins (merged cells repeat the top-left label)
                if (!_labelIndex.ContainsKey(c.label))
                    _labelIndex[c.label] = key;
            }
        }
    }

    private static bool IsDirectionLabel(string s)
    {
        switch (s.ToLowerInvariant())
        {
            case "up": case "down": case "left": case "right": case "any": return true;
            default: return false;
        }
    }

    private void OnEnable() { _index = null; _labelIndex = null; }
}
