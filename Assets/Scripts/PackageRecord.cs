/// <summary>
/// Immutable planned fields + mutable actual fields for one package's journey.
/// Populated by PackageTracker.
/// </summary>
public class PackageRecord
{
    // ── Set at spawn ───────────────────────────────────────────────────────
    public int    PackageId;
    public int    Address;

    public string PlannedGaylord;    // SortPoint name, or "Overflow", or "Unknown"
    public int    PlannedRangeMin;
    public int    PlannedRangeMax;

    public float  SpawnTime;

    // ── Set at landing ─────────────────────────────────────────────────────
    public string ActualGaylord;     // null until landed
    public int?   ActualRangeMin;    // null for overflow / not yet landed
    public int?   ActualRangeMax;

    public float  LandingTime;       // 0 until landed

    // ── Derived ────────────────────────────────────────────────────────────
    public bool   Landed    => ActualGaylord != null;
    public bool   IsCorrect => Landed && PlannedGaylord == ActualGaylord;
}
