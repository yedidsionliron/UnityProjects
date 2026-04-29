using UnityEngine;

/// <summary>
/// Cell type classification matching the Grid.xlsx / Grid.json data.
/// </summary>
public enum CellType
{
    Empty,
    Storage,
    Corridor,
    InductZone,
    SorterSystem,
    SortChute,
    EmptyBuffer,
    FeederQueue,
    Staging,
    Charging,
}

/// <summary>
/// Explicit subsystem ownership carried from the grid. This is the source of truth
/// for builder footprints inside shared sorter-space cells.
/// </summary>
public enum SystemRegion
{
    None,
    Diverter,
    Singulator,
    Feeder,
}

/// <summary>
/// Direction constraint on a corridor cell.
/// The forbidden movement direction is the OPPOSITE of this value.
///   Up    → forbid South
///   Down  → forbid North
///   Left  → forbid East
///   Right → forbid West
///   Any   → no restriction
/// </summary>
public enum CorridorDirection { None, Up, Down, Left, Right, Any }

/// <summary>
/// Immutable definition of one grid cell, parsed from Grid.json at editor time
/// and stored in <see cref="StationGridData"/>.
/// </summary>
[System.Serializable]
public class CellData
{
    [Tooltip("1-based row index (row 1 = top of facility)")]
    public int row;

    [Tooltip("1-based column index (col 1 = left edge)")]
    public int col;

    public CellType cellType;

    [Tooltip("Explicit subsystem ownership for shared sorter-space cells")]
    public SystemRegion systemRegion;

    [Tooltip("Text label from Excel cell, e.g. S5, E3, St12, 147, up, Diverter")]
    public string label;

    [Tooltip("One-way direction for corridor cells; None for non-corridor cells")]
    public CorridorDirection direction;

    [Tooltip("True for InductZone and SorterSystem — robots may never enter")]
    public bool isNoRobot;

    // ── Derived helpers ────────────────────────────────────────────────────

    /// <summary>0-based (row-1, col-1) for array indexing.</summary>
    public Vector2Int Index => new Vector2Int(row - 1, col - 1);

    /// <summary>
    /// The movement direction (as a grid delta) that is forbidden when leaving this cell.
    /// Returns Vector2Int.zero if there is no restriction.
    /// Row increases southward, col increases eastward.
    /// </summary>
    public Vector2Int ForbiddenDelta
    {
        get
        {
            switch (direction)
            {
                case CorridorDirection.Up:    return new Vector2Int( 1, 0);  // forbid South (row+1)
                case CorridorDirection.Down:  return new Vector2Int(-1, 0);  // forbid North (row-1)
                case CorridorDirection.Left:  return new Vector2Int( 0, 1);  // forbid East  (col+1)
                case CorridorDirection.Right: return new Vector2Int( 0,-1);  // forbid West  (col-1)
                default:                     return Vector2Int.zero;
            }
        }
    }

    /// <summary>
    /// True if a robot moving by <paramref name="delta"/> is allowed to leave this cell.
    /// </summary>
    public bool AllowsExit(Vector2Int delta)
    {
        Vector2Int forbidden = ForbiddenDelta;
        return forbidden == Vector2Int.zero || delta != forbidden;
    }
}
