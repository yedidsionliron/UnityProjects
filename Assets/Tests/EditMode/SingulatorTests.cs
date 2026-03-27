using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode unit tests for Singulator pure-math helpers.
/// No physics engine, no scene required.
/// Run via Window → General → Test Runner → EditMode.
/// </summary>
public class SingulatorTests
{
    // ── BeltLocalZ / BeltLocalX ────────────────────────────────────────────────

    private Singulator MakeSingulator(Vector3 origin, Vector3 fwd, Vector3 right)
    {
        var go = new GameObject("TestBelt");
        var s  = go.AddComponent<Singulator>();

        // Inject geometry directly via a thin BoxCollider on a child so Start()
        // won't run (EditMode), and we set the backing fields via reflection.
        var bc = go.AddComponent<BoxCollider>();
        s.beltCollider = bc;

        // We set private fields with reflection so tests target the real helpers.
        SetField(s, "beltOrigin",   origin);
        SetField(s, "beltFwd",      fwd);
        SetField(s, "beltRight",    right);
        SetField(s, "beltLength",   10f);
        SetField(s, "zConvergence", 8f);
        return s;
    }

    private static void SetField(object obj, string name, object value)
    {
        var f = obj.GetType().GetField(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(f, $"Field '{name}' not found on {obj.GetType().Name}");
        f.SetValue(obj, value);
    }

    [Test]
    public void BeltLocalZ_AtOrigin_IsZero()
    {
        Vector3 origin = new Vector3(1f, 0f, 2f);
        var s = MakeSingulator(origin, Vector3.forward, Vector3.right);
        Assert.AreEqual(0f, s.BeltLocalZ(origin), 1e-5f);
    }

    [Test]
    public void BeltLocalZ_OneMetreForward_IsOne()
    {
        Vector3 origin = Vector3.zero;
        var s = MakeSingulator(origin, Vector3.forward, Vector3.right);
        Assert.AreEqual(1f, s.BeltLocalZ(new Vector3(0f, 0f, 1f)), 1e-5f);
    }

    [Test]
    public void BeltLocalZ_BehindOrigin_IsNegative()
    {
        Vector3 origin = Vector3.zero;
        var s = MakeSingulator(origin, Vector3.forward, Vector3.right);
        Assert.Less(s.BeltLocalZ(new Vector3(0f, 0f, -0.5f)), 0f);
    }

    [Test]
    public void BeltLocalX_AtOrigin_IsZero()
    {
        Vector3 origin = Vector3.zero;
        var s = MakeSingulator(origin, Vector3.forward, Vector3.right);
        Assert.AreEqual(0f, s.BeltLocalX(origin), 1e-5f);
    }

    [Test]
    public void BeltLocalX_HalfMetreRight_IsHalf()
    {
        Vector3 origin = Vector3.zero;
        var s = MakeSingulator(origin, Vector3.forward, Vector3.right);
        Assert.AreEqual(0.5f, s.BeltLocalX(new Vector3(0.5f, 0f, 0f)), 1e-5f);
    }

    [Test]
    public void BeltLocalX_IsIndependentOfForwardOffset()
    {
        Vector3 origin = Vector3.zero;
        var s = MakeSingulator(origin, Vector3.forward, Vector3.right);
        float x1 = s.BeltLocalX(new Vector3(0.3f, 0f, 0f));
        float x2 = s.BeltLocalX(new Vector3(0.3f, 0f, 5f));
        Assert.AreEqual(x1, x2, 1e-5f);
    }

    // ── ComputeSlotTime ────────────────────────────────────────────────────────

    [Test]
    public void SlotTime_FirstPackage_UsesEarliestSlot()
    {
        float fixedTime          = 10f;
        float minTravelTime      = 4f;    // zConvergence / beltSpeed
        float lastSlotArrival    = 0f;    // no previous slot
        float slotDuration       = 0.5f;

        float T = Singulator.ComputeSlotTime(fixedTime, minTravelTime, lastSlotArrival, slotDuration);

        // earliestSlot = 14.0, lastSlot+duration = 0.5 → max = 14.0
        Assert.AreEqual(14f, T, 1e-5f);
    }

    [Test]
    public void SlotTime_SecondPackage_UsesLastSlotWhenLarger()
    {
        float fixedTime       = 10f;
        float minTravelTime   = 4f;    // earliestSlot = 14.0
        float lastSlotArrival = 16f;   // previous slot at t=16
        float slotDuration    = 0.5f;  // need +0.5 gap

        float T = Singulator.ComputeSlotTime(fixedTime, minTravelTime, lastSlotArrival, slotDuration);

        // earliestSlot=14, lastSlot+duration=16.5 → max = 16.5
        Assert.AreEqual(16.5f, T, 1e-5f);
    }

    [Test]
    public void SlotTime_NeverBeforeEarliestSlot()
    {
        float fixedTime       = 100f;
        float minTravelTime   = 5f;
        float lastSlotArrival = 50f;   // very old slot
        float slotDuration    = 1f;

        float T = Singulator.ComputeSlotTime(fixedTime, minTravelTime, lastSlotArrival, slotDuration);

        Assert.GreaterOrEqual(T, fixedTime + minTravelTime - 1e-5f);
    }

    // ── ComputeForwardAccel ────────────────────────────────────────────────────

    [Test]
    public void ForwardAccel_DenomNearZero_ReturnsZero()
    {
        // distToConv - beltSpeed * tau ≈ 0 → a_z = 0
        float beltSpeed   = 1.5f;
        float tau         = 4f;
        float distToConv  = beltSpeed * tau;   // denom = 0 exactly

        float a = Singulator.ComputeForwardAccel(0f, beltSpeed, distToConv, tau, 3f, 2f);
        Assert.AreEqual(0f, a, 1e-4f);
    }

    [Test]
    public void ForwardAccel_AlreadyAtSpeed_NearZero()
    {
        // vz == beltSpeed → numerator = 0 → a_z = 0 regardless of denom
        float beltSpeed  = 1.5f;
        float distToConv = 5f;
        float tau        = 3f;

        float a = Singulator.ComputeForwardAccel(beltSpeed, beltSpeed, distToConv, tau, 3f, 2f);
        Assert.AreEqual(0f, a, 1e-4f);
    }

    [Test]
    public void ForwardAccel_ClampedToMaxAcceleration()
    {
        // Force a huge positive a_z by making package very slow and denom slightly negative.
        float beltSpeed   = 1.5f;
        float vz          = 0f;
        float distToConv  = 0.01f;
        float tau         = 10f;   // beltSpeed*tau >> distToConv → denom negative

        float a = Singulator.ComputeForwardAccel(vz, beltSpeed, distToConv, tau, 3f, 2f);
        Assert.LessOrEqual(a, 3f + 1e-5f);
    }

    [Test]
    public void ForwardAccel_ClampedToMaxDeceleration()
    {
        // Force a large negative a_z: package moving fast, little distance left, much time.
        float beltSpeed   = 1.5f;
        float vz          = 10f;  // much faster than belt
        float distToConv  = 5f;
        float tau         = 0.5f;

        float a = Singulator.ComputeForwardAccel(vz, beltSpeed, distToConv, tau, 3f, 2f);
        Assert.GreaterOrEqual(a, -2f - 1e-5f);
    }

    // ── pastConvergence transition logic (state machine) ──────────────────────

    [Test]
    public void PastConvergence_TauZero_ShouldSwitchToCruise()
    {
        // When tau <= 0, the drive loop sets pastConvergence = true.
        // We verify that ComputeForwardAccel at that point still returns a clamped value
        // (the caller is responsible for the flag, not this function).
        // This test documents the contract: tau=0 is handled by the caller guard, not here.
        float a = Singulator.ComputeForwardAccel(1.0f, 1.5f, 3f, 0f, 3f, 2f);
        // denom = 3 - 1.5*0 = 3, numerator = -0.5*(0.5)^2 = -0.125, a = -0.125/3 ≈ -0.042
        Assert.That(a, Is.InRange(-2f, 3f), "Result should be within clamp bounds");
    }

    // ── Teardown ──────────────────────────────────────────────────────────────

    [TearDown]
    public void Cleanup()
    {
        // Destroy any GameObjects created during tests.
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.name == "TestBelt")
                Object.DestroyImmediate(go);
        }
    }
}
