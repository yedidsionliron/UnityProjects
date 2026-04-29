using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-level database of all station Gaylords.
/// </summary>
public class GaylordDatabase : MonoBehaviour
{
    [SerializeField]
    private List<GaylordRecord> records = new List<GaylordRecord>();

    private Dictionary<string, GaylordRecord> _byId;

    public IReadOnlyList<GaylordRecord> Records => records;

    public void RebuildIndex()
    {
        _byId = new Dictionary<string, GaylordRecord>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < records.Count; i++)
        {
            GaylordRecord record = records[i];
            if (record != null && !string.IsNullOrEmpty(record.id))
                _byId[record.id] = record;
        }
    }

    public void Clear()
    {
        records.Clear();
        _byId?.Clear();
    }

    public void AddOrReplace(GaylordRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.id))
            throw new ArgumentException("GaylordDatabase: record and record.id are required.");

        RebuildIndexIfNeeded();
        if (_byId.TryGetValue(record.id, out var existing))
        {
            int index = records.IndexOf(existing);
            if (index >= 0)
                records[index] = record;
        }
        else
        {
            records.Add(record);
        }

        _byId[record.id] = record;
    }

    public bool TryGet(string id, out GaylordRecord record)
    {
        RebuildIndexIfNeeded();
        return _byId.TryGetValue(id, out record);
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        RebuildIndexIfNeeded();
        if (!_byId.TryGetValue(id, out var existing))
            return false;

        _byId.Remove(id);
        return records.Remove(existing);
    }

    public bool TryUpdateLocation(string id, string gridLabel, Vector2Int gridCell)
    {
        if (!TryGet(id, out var record))
            return false;

        record.gridLabel = gridLabel;
        record.gridCell = gridCell;
        return true;
    }

    public bool TrySetAddresses(string id, List<int> addresses)
    {
        if (!TryGet(id, out var record))
            return false;

        record.addresses = addresses;
        return true;
    }

    public bool TrySetRoutes(string id, List<string> routes)
    {
        if (!TryGet(id, out var record))
            return false;

        record.routes = routes;
        return true;
    }

    public bool TrySetFullnessLevel(string id, float? fullnessLevel)
    {
        if (!TryGet(id, out var record))
            return false;

        record.fullnessLevel = fullnessLevel.HasValue
            ? new NullableFloat { value = fullnessLevel.Value }
            : null;
        return true;
    }

    public bool TrySetPackageCount(string id, int? packageCount)
    {
        if (!TryGet(id, out var record))
            return false;

        record.packageCount = packageCount.HasValue
            ? new NullableInt { value = packageCount.Value }
            : null;
        return true;
    }

    private void OnEnable() => RebuildIndex();

    private void RebuildIndexIfNeeded()
    {
        if (_byId == null)
            RebuildIndex();
    }
}
