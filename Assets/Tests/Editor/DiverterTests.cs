using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for DiverterConfig / Diverter zone-layout logic.
///
/// Run via: Window > General > Test Runner > EditMode (in Unity Editor),
/// or from the CLI with -runTests -testPlatform editmode.
/// </summary>
public class DiverterTests
{
    // -----------------------------------------------------------------------
    // 1. BeltLength property
    // -----------------------------------------------------------------------

    [Test]
    public void BeltLength_Equals_NumDivertPoints_Times_GaylordWidth()
    {
        var go  = new GameObject("TestDiverterConfig");
        var cfg = go.AddComponent<DiverterConfig>();

        cfg.numDivertPoints = 8;
        cfg.gaylordWidth    = 1.5f;

        Assert.AreEqual(12f, cfg.BeltLength, 0.0001f,
            "BeltLength must equal numDivertPoints × gaylordWidth.");

        Object.DestroyImmediate(go);
    }

    [Test]
    public void BeltLength_ChangesWithNumDivertPoints()
    {
        var go  = new GameObject("TestDiverterConfig");
        var cfg = go.AddComponent<DiverterConfig>();
        cfg.gaylordWidth = 1.2f;

        cfg.numDivertPoints = 10;
        Assert.AreEqual(12f, cfg.BeltLength, 0.0001f);

        cfg.numDivertPoints = 5;
        Assert.AreEqual(6f, cfg.BeltLength, 0.0001f);

        Object.DestroyImmediate(go);
    }

    // -----------------------------------------------------------------------
    // 2. MeasureBelt must NOT overwrite the editor-configured beltLength
    // -----------------------------------------------------------------------

    /// <summary>
    /// Before the fix: MeasureBeltLength() overwrote beltLength with the
    /// measured mesh length (~8 m), discarding the gaylord-row length (9.6 m).
    /// After the fix: beltLength is preserved so BuildZones() spaces zones
    /// at gaylordWidth intervals, not at (measuredLength / numDivertPoints).
    /// </summary>
    [Test]
    public void MeasureBelt_WithRenderer_DoesNotOverride_ConfiguredBeltLength()
    {
        const float configuredLength = 9.6f; // 8 × 1.2 m
        const float meshLength       = 8f;   // intentionally different (tile rounding)

        var go = new GameObject("TestDiverter");
        var d  = go.AddComponent<Diverter>();
        d.beltLength = configuredLength;

        // Attach a fake belt mesh whose measured length differs from configuredLength.
        var belt = GameObject.CreatePrimitive(PrimitiveType.Cube);
        belt.transform.SetParent(go.transform, false);
        belt.transform.localScale = new Vector3(1.4f, 0.1f, meshLength);

        d.MeasureBelt();

        Assert.AreEqual(configuredLength, d.beltLength, 0.001f,
            "MeasureBelt() must not overwrite the editor-set beltLength. " +
            "It should only update beltCenterLocalZ and triggerY.");

        Object.DestroyImmediate(go);
    }

    [Test]
    public void MeasureBelt_WithNoRenderers_PreservesBeltLength()
    {
        var go = new GameObject("TestDiverter");
        var d  = go.AddComponent<Diverter>();
        d.beltLength = 12f;

        d.MeasureBelt(); // returns early — no renderers

        Assert.AreEqual(12f, d.beltLength, 0.0001f,
            "beltLength must be unchanged when there are no renderers to measure.");

        Object.DestroyImmediate(go);
    }

    // -----------------------------------------------------------------------
    // 3. Zone spacing == gaylordWidth after Start()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Core regression test.
    /// Before fix: Start() calls MeasureBeltLength() which sets beltLength to the
    /// measured mesh length (~8 m). BuildZones() then spaces zones at 8/5 = 1.6 m
    /// instead of the gaylord pitch of 1.2 m.
    /// After fix: beltLength is preserved at 6 m → zone spacing = 1.2 m.
    /// </summary>
    [Test]
    public void Start_ZoneSpacing_MatchesGaylordWidth_EvenWhenMeshLengthDiffers()
    {
        const int   n            = 5;
        const float gaylordWidth = 1.2f;
        const float meshLength   = 8f; // longer mesh — before fix this poisons zone spacing

        var go = new GameObject("TestDiverter");
        var d  = go.AddComponent<Diverter>();
        d.numDivertPoints   = n;
        d.beltLength        = n * gaylordWidth; // 6.0 m — configured by editor Build()
        d.beltCenterLocalZ  = 0f;
        d.triggerWidth      = 1.4f;
        d.triggerDepth      = 0.4f;
        d.triggerHeight     = 1.2f;
        d.divertSpeed       = 0f;  // exitOffset = 0 for simple, deterministic geometry
        d.landingNormalized = 0.5f;
        d.sortPoints        = null;

        // Fake belt mesh that is LONGER than the configured gaylord-row length.
        var belt = GameObject.CreatePrimitive(PrimitiveType.Cube);
        belt.transform.SetParent(go.transform, false);
        belt.transform.localScale = new Vector3(1.4f, 0.1f, meshLength);

        InvokeStart(d);

        var zones = CollectZones(go);
        Assert.AreEqual(n, zones.Count, "Expected exactly numDivertPoints DivertZone children.");

        zones.Sort((a, b) => a.localPosition.z.CompareTo(b.localPosition.z));

        for (int i = 1; i < zones.Count; i++)
        {
            float step = zones[i].localPosition.z - zones[i - 1].localPosition.z;
            Assert.AreEqual(gaylordWidth, step, 0.001f,
                $"Zone step {i - 1}→{i} = {step:F4} m, expected gaylordWidth {gaylordWidth} m. " +
                "beltLength may have been overwritten by MeasureBeltLength().");
        }

        Object.DestroyImmediate(go);
    }

    // -----------------------------------------------------------------------
    // 4. Zone front-edge aligns with gaylord slot centre (exitOffset = 0)
    // -----------------------------------------------------------------------

    /// <summary>
    /// When exitOffset is 0 (divertSpeed = 0), a package entering the trigger
    /// zone at its front edge should be exactly at the gaylord slot centre —
    /// i.e. it has zero forward travel during diversion, so it lands right there.
    ///
    /// Zone front edge Z  = zone centre Z  − triggerDepth / 2
    /// Expected slot centre Z = beltStart + (i + 0.5) × gaylordWidth
    /// </summary>
    [Test]
    public void Start_ZoneFrontEdge_AlignsWithGaylordSlotCentre()
    {
        const int   n            = 5;
        const float gaylordWidth = 1.2f;
        const float triggerDepth = 0.4f;

        var go = new GameObject("TestDiverter");
        var d  = go.AddComponent<Diverter>();
        d.numDivertPoints   = n;
        d.beltLength        = n * gaylordWidth;
        d.beltCenterLocalZ  = 0f;
        d.triggerWidth      = 1.4f;
        d.triggerDepth      = triggerDepth;
        d.triggerHeight     = 1.2f;
        d.divertSpeed       = 0f;
        d.landingNormalized = 0.5f;
        d.sortPoints        = null;

        InvokeStart(d);

        var zones = CollectZones(go);
        Assert.AreEqual(n, zones.Count);
        zones.Sort((a, b) => a.localPosition.z.CompareTo(b.localPosition.z));

        float beltStart = d.beltCenterLocalZ - d.beltLength / 2f;
        for (int i = 0; i < n; i++)
        {
            float expectedSlotCentre = beltStart + (i + 0.5f) * gaylordWidth;
            float zoneFrontEdge      = zones[i].localPosition.z - triggerDepth / 2f;
            Assert.AreEqual(expectedSlotCentre, zoneFrontEdge, 0.001f,
                $"Zone {i} front edge ({zoneFrontEdge:F4}) should equal gaylord slot {i} centre ({expectedSlotCentre:F4}).");
        }

        Object.DestroyImmediate(go);
    }

    // -----------------------------------------------------------------------
    // 5. Zone count matches numDivertPoints
    // -----------------------------------------------------------------------

    [Test]
    public void Start_ZoneCount_EqualsNumDivertPoints()
    {
        foreach (int n in new[] { 1, 5, 10, 20 })
        {
            var go = new GameObject($"TestDiverter_n{n}");
            var d  = go.AddComponent<Diverter>();
            d.numDivertPoints   = n;
            d.beltLength        = n * 1.2f;
            d.beltCenterLocalZ  = 0f;
            d.triggerWidth      = 1.4f;
            d.triggerDepth      = 0.4f;
            d.triggerHeight     = 1.2f;
            d.divertSpeed       = 0f;
            d.landingNormalized = 0.5f;
            d.sortPoints        = null;

            InvokeStart(d);

            int count = CollectZones(go).Count;
            Assert.AreEqual(n, count, $"numDivertPoints={n} should produce {n} DivertZone children.");

            Object.DestroyImmediate(go);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void InvokeStart(Diverter d)
    {
        typeof(Diverter)
            .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(d, null);
    }

    private static List<Transform> CollectZones(GameObject root)
    {
        var result = new List<Transform>();
        foreach (Transform child in root.transform)
            if (child.GetComponent<DivertZone>() != null)
                result.Add(child);
        return result;
    }
}
