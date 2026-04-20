using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* pathfinder on the integer grid.
///
/// Two traversal modes:
///   loaded  = corridor cells only (robot is carrying a gaylord)
///   empty   = corridor cells + storage slot cells (robot passes underneath stored carts)
///
/// One-way rule: when leaving a cell whose direction is not "any" or "none",
/// the move direction must not equal the cell's forbidden delta (CellData.ForbiddenDelta).
/// This prevents head-on collisions while still allowing turns.
/// </summary>
public static class Pathfinder
{
    private static readonly Vector2Int[] Neighbors =
    {
        new Vector2Int(-1,  0), // North (row decreases)
        new Vector2Int( 1,  0), // South (row increases)
        new Vector2Int( 0, -1), // West  (col decreases)
        new Vector2Int( 0,  1), // East  (col increases)
    };

    /// <summary>
    /// Find a path from <paramref name="start"/> to <paramref name="goal"/> (0-based indices).
    /// Returns an ordered list of cells from start (exclusive) to goal (inclusive),
    /// or null if no path exists.
    /// </summary>
    public static List<Vector2Int> Find(
        Vector2Int start,
        Vector2Int goal,
        GridMap    map,
        bool       loaded)
    {
        if (start == goal) return new List<Vector2Int>();

        var openSet  = new SortedList<float, Vector2Int>(new DuplicateKeyComparer());
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore   = new Dictionary<Vector2Int, float>();
        var inOpen   = new HashSet<Vector2Int>();

        gScore[start] = 0f;
        float h = Heuristic(start, goal);
        openSet.Add(h, start);
        inOpen.Add(start);

        while (openSet.Count > 0)
        {
            // Pop lowest f-score
            var current = openSet.Values[0];
            openSet.RemoveAt(0);
            inOpen.Remove(current);

            if (current == goal)
                return ReconstructPath(cameFrom, current, start);

            var currentCell = map.gridData.GetCell(current);

            foreach (var delta in Neighbors)
            {
                var neighbor = current + delta;

                // One-way constraint on the source cell
                if (currentCell != null && !currentCell.AllowsExit(delta))
                    continue;

                // Traversability (type check + no-robot zones)
                if (!map.IsTraversable(neighbor, loaded))
                    continue;

                float tentativeG = gScore[current] + 1f;
                if (!gScore.TryGetValue(neighbor, out float existingG) || tentativeG < existingG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor]   = tentativeG;
                    float f = tentativeG + Heuristic(neighbor, goal);
                    if (!inOpen.Contains(neighbor))
                    {
                        openSet.Add(f, neighbor);
                        inOpen.Add(neighbor);
                    }
                }
            }
        }

        return null; // no path
    }

    private static float Heuristic(Vector2Int a, Vector2Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); // Manhattan

    private static List<Vector2Int> ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Vector2Int current,
        Vector2Int start)
    {
        var path = new List<Vector2Int>();
        while (current != start)
        {
            path.Add(current);
            current = cameFrom[current];
        }
        path.Reverse();
        return path;
    }

    // SortedList requires unique keys; break ties by insertion order using a counter.
    private class DuplicateKeyComparer : IComparer<float>
    {
        public int Compare(float x, float y)
        {
            int result = x.CompareTo(y);
            return result == 0 ? 1 : result; // never return 0 — treat equal keys as distinct
        }
    }
}
