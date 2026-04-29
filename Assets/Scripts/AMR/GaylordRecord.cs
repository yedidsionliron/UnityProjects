using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent metadata for one station Gaylord.
/// </summary>
[Serializable]
public class GaylordRecord
{
    [Tooltip("Unique Gaylord identifier. Used as the database key.")]
    public string id;

    [Tooltip("Grid label for the Gaylord's current location, e.g. 147 or St12.")]
    public string gridLabel;

    [Tooltip("0-based grid location.")]
    public Vector2Int gridCell;

    [Tooltip("Associated package addresses. Null by default until assigned.")]
    public List<int> addresses;

    [Tooltip("Associated route identifiers. Null by default until assigned.")]
    public List<string> routes;

    [Tooltip("Fullness level. Unset by default.")]
    [SerializeReference] public NullableFloat fullnessLevel;

    [Tooltip("Number of packages. Unset by default.")]
    [SerializeReference] public NullableInt packageCount;

    public static GaylordRecord CreateDefault(string id, string gridLabel, Vector2Int gridCell)
    {
        return new GaylordRecord
        {
            id = id,
            gridLabel = gridLabel,
            gridCell = gridCell,
            addresses = null,
            routes = null,
            fullnessLevel = null,
            packageCount = null,
        };
    }
}

[Serializable]
public class NullableFloat
{
    public float value;
}

[Serializable]
public class NullableInt
{
    public int value;
}
