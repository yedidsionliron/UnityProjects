using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Tracks every package from spawn to gaylord landing.
/// Exports a timestamped CSV to DataTables/ at the project root on Stop or via ExportCSV().
///
/// Setup: place on any scene GameObject. It will auto-find BoxSpawner on Start.
/// </summary>
public class PackageTracker : MonoBehaviour
{
    public static PackageTracker Instance { get; private set; }

    // Live record count visible in the Inspector
    [SerializeField, HideInInspector] private int _totalSpawned;
    [SerializeField, HideInInspector] private int _totalLanded;
    [SerializeField, HideInInspector] private int _totalCorrect;

    public int TotalSpawned => _totalSpawned;
    public int TotalLanded  => _totalLanded;
    public int TotalCorrect => _totalCorrect;

    private readonly Dictionary<Package, PackageRecord> _records = new Dictionary<Package, PackageRecord>();
    private SortPoint[] _sortPoints;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Cache all SortPoints once — they are edit-time objects and don't change at runtime.
        _sortPoints = FindObjectsByType<SortPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // Subscribe to every BoxSpawner in the scene.
        foreach (var spawner in FindObjectsByType<BoxSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            spawner.OnBoxSpawned += OnBoxSpawned;
    }

    void OnApplicationQuit() => ExportCSV();

    // ── Spawn handler ──────────────────────────────────────────────────────

    private void OnBoxSpawned(GameObject box)
    {
        var pkg = box.GetComponent<Package>();
        if (pkg == null) return;

        var record = new PackageRecord
        {
            PackageId = pkg.id,
            Address   = pkg.address,
            SpawnTime = Time.time,
        };

        // Resolve planned gaylord from address.
        SortPoint planned = FindSortPoint(pkg.address);
        if (planned != null)
        {
            record.PlannedGaylord  = planned.name;
            record.PlannedRangeMin = planned.addressMin;
            record.PlannedRangeMax = planned.addressMax;
        }
        else
        {
            record.PlannedGaylord  = "Unknown";
        }

        _records[pkg] = record;
        _totalSpawned++;
    }

    // ── Landing handler (called by GaylordContainer) ───────────────────────

    public void RecordLanding(Package pkg, GaylordContainer gaylord)
    {
        if (pkg == null || gaylord == null) return;
        if (!_records.TryGetValue(pkg, out var record)) return;
        if (record.Landed) return; // already recorded (trigger can fire multiple times)

        record.LandingTime = Time.time;

        // Derive actual gaylord identity from the parent SortPoint — same reference
        // used for PlannedGaylord — so IsCorrect comparison is apples-to-apples.
        var sp = gaylord.GetComponentInParent<SortPoint>();
        if (sp != null)
        {
            record.ActualGaylord  = sp.name;
            record.ActualRangeMin = sp.addressMin;
            record.ActualRangeMax = sp.addressMax;
        }
        else
        {
            // Overflow or unregistered gaylord — no SortPoint parent.
            record.ActualGaylord = gaylord.transform.parent != null
                ? gaylord.transform.parent.name
                : gaylord.name;
        }

        _totalLanded++;
        if (record.IsCorrect) _totalCorrect++;
    }

    // ── CSV Export ─────────────────────────────────────────────────────────

    public void ExportCSV()
    {
        if (_records.Count == 0)
        {
            Debug.Log("PackageTracker: no records to export.");
            return;
        }

        string folder = Path.Combine(Application.dataPath, "..", "DataTables");
        Directory.CreateDirectory(folder);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filePath  = Path.Combine(folder, $"packages_{timestamp}.csv");

        var sb = new StringBuilder();
        sb.AppendLine("PackageId,Address,PlannedGaylord,PlannedRangeMin,PlannedRangeMax," +
                      "ActualGaylord,ActualRangeMin,ActualRangeMax,IsCorrect,SpawnTime,LandingTime");

        foreach (var rec in _records.Values)
        {
            sb.AppendLine(string.Join(",",
                rec.PackageId,
                rec.Address,
                Escape(rec.PlannedGaylord),
                rec.PlannedRangeMin,
                rec.PlannedRangeMax,
                Escape(rec.ActualGaylord),
                rec.ActualRangeMin.HasValue ? rec.ActualRangeMin.ToString() : "",
                rec.ActualRangeMax.HasValue ? rec.ActualRangeMax.ToString() : "",
                rec.Landed ? rec.IsCorrect.ToString() : "",
                rec.SpawnTime.ToString("F3"),
                rec.LandingTime > 0f ? rec.LandingTime.ToString("F3") : ""
            ));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"PackageTracker: exported {_records.Count} records to {filePath}  " +
                  $"({_totalLanded} landed, {_totalCorrect} correct).");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private SortPoint FindSortPoint(int address)
    {
        if (_sortPoints == null) return null;
        foreach (var sp in _sortPoints)
            if (sp != null && sp.Contains(address)) return sp;
        return null;
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Wrap in quotes if value contains a comma or quote.
        if (s.Contains(",") || s.Contains("\""))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
