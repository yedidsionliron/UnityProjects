using UnityEngine;

namespace PCS
{
    /// <summary>
    /// Monitors singulator belt capacity and halts the feeder conveyor when it
    /// exceeds the threshold, preventing back-pressure pile-ups.
    ///
    /// Capacity = sum(packageLength + desiredGap) / beltLength
    ///
    /// Scene setup:
    ///   • Attach anywhere in the scene.
    ///   • Drag any GameObject from the singulator belt into Singulator Belt.
    ///   • Drag any GameObject from the feeder belt into Feeder Belt.
    /// </summary>
    public class BeltCapacitySensor : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Any GameObject belonging to the singulator belt (root or child).")]
        public GameObject singulatorBelt;

        [Tooltip("Any GameObject belonging to the feeder belt (root or child).")]
        public GameObject feederBelt;

        [Header("Control")]
        [Tooltip("Capacity fraction (0–1) at which the feeder is stopped.")]
        [Range(0f, 1f)]
        public float capacityThreshold = 0.95f;

        [Tooltip("How long (seconds) the feeder stays stopped before it can restart.")]
        public float stopDuration = 2f;

        // ── Private State ─────────────────────────────────────────────────────

        private PCSsingulator _singulator;
        private PCSConfig     _feederConfig;
        private float         _savedSpeed;
        private float         _stopTimer;
        private bool          _feederRunning = true;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            if (singulatorBelt != null)
            {
                var config = singulatorBelt.GetComponentInParent<PCSConfig>(true)
                          ?? singulatorBelt.GetComponentInChildren<PCSConfig>(true);
                if (config != null) _singulator = config.pcsS;
                if (_singulator == null)
                    _singulator = singulatorBelt.GetComponentInParent<PCSsingulator>(true)
                               ?? singulatorBelt.GetComponentInChildren<PCSsingulator>(true);
            }
            if (_singulator == null)
                Debug.LogError("BeltCapacitySensor: no PCSsingulator found on Singulator Belt. Enable Singulator Mode on its PCSConfig.", this);

            if (feederBelt != null)
                _feederConfig = feederBelt.GetComponentInParent<PCSConfig>(true)
                             ?? feederBelt.GetComponentInChildren<PCSConfig>(true);
            if (_feederConfig == null)
            {
                string diagnosis = feederBelt == null
                    ? "Feeder Belt field is not assigned."
                    : $"Feeder Belt is '{feederBelt.name}' (root: '{feederBelt.transform.root.name}'). " +
                      $"No PCSConfig found in its hierarchy. Make sure the field points to a conveyor that has PCSConfig on its root.";
                Debug.LogError($"BeltCapacitySensor: no PCSConfig found on Feeder Belt. {diagnosis}", this);
            }
            else
                _savedSpeed = _feederConfig.speed;
        }

        private void Update()
        {
            if (_singulator == null || _feederConfig == null) return;

            if (!_feederRunning)
            {
                _stopTimer -= Time.deltaTime;
                if (_stopTimer <= 0f)
                    ResumeFeeder();
                return;
            }

            if (_singulator.GetCapacity() >= capacityThreshold)
                StopFeeder();
        }

        // ── Control ───────────────────────────────────────────────────────────

        private void StopFeeder()
        {
            _feederRunning = false;
            _stopTimer     = stopDuration;
            SetFeederSpeed(0f);
            Debug.Log($"BeltCapacitySensor: capacity {_singulator.GetCapacity():P0} — feeder stopped for {stopDuration}s.", this);
        }

        private void ResumeFeeder()
        {
            _feederRunning = true;
            SetFeederSpeed(_savedSpeed);
            Debug.Log($"BeltCapacitySensor: feeder resumed at {_savedSpeed} m/s.", this);
        }

        private void SetFeederSpeed(float speed)
        {
            _feederConfig.speed = speed;
            if (_feederConfig.pcsC != null) _feederConfig.pcsC.speed = speed;
            if (_feederConfig.pcsS != null) _feederConfig.pcsS.beltSpeed = speed;
        }

        // ── Gizmos ────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_singulator == null) return;
            float cap = Application.isPlaying ? _singulator.GetCapacity() : 0f;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.5f,
                $"Capacity: {cap:P0}  Feeder: {(_feederRunning ? "running" : $"stopped {_stopTimer:F1}s")}");
        }
#endif
    }
}
