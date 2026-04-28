using UnityEngine;

namespace PCS
{
    /// <summary>
    /// Assembles a bend (turning) conveyor from pre-modelled FBX pieces.
    /// All piece prefabs must have their origin at the arc center (see Blender guide).
    /// </summary>
    public class PCSBendConfig : MonoBehaviour
    {
        [Header("Pieces (origin at arc center)")]
        public GameObject bendBeltPrefab;
        public GameObject bendRailInnerPrefab;
        public GameObject bendRailOuterPrefab;
        public GameObject bendEndStartPrefab;
        public GameObject bendEndEndPrefab;

        [Header("Settings")]
        [Tooltip("Angle of the turn in degrees (e.g. 90 for a right-angle corner).")]
        public float bendAngle = 90f;

        [Tooltip("PCSConfig of the connected straight conveyor — used to match width and speed.")]
        public PCSConfig straightConveyor;

        [Tooltip("Belt speed in m/s. Overridden by straightConveyor.speed if assigned.")]
        public float speed = 0.6f;

        // Runtime references
        private GameObject _body;
        private PCSUVScroller _uvScroller;
        private PCSConveyor _conveyor;

        public bool ready { get; private set; }

        // Width scale formula matching PCSConfig
        float WidthScale => straightConveyor != null
            ? (straightConveyor.width - 0.4f) / 1.6f
            : 1f;

        float Speed => straightConveyor != null ? straightConveyor.speed : speed;

        public void CreateBend()
        {
            // Destroy previous build
            if (_body != null)
                DestroyImmediate(_body);

            ready = false;

            _body = new GameObject("Bend Body");
            _body.transform.parent = transform;
            _body.transform.localPosition = Vector3.zero;
            _body.transform.localRotation = Quaternion.identity;
            _body.transform.localScale = Vector3.one;
            _body.hideFlags = HideFlags.HideInHierarchy;

            float ws = WidthScale;
            float spd = Speed;

            // Belt surface
            if (bendBeltPrefab != null)
            {
                GameObject belt = Instantiate(bendBeltPrefab, _body.transform);
                belt.name = "Bend Belt";
                belt.transform.localPosition = Vector3.zero;
                belt.transform.localRotation = Quaternion.identity;
                belt.transform.localScale = new Vector3(ws, 1f, 1f);

                _uvScroller = belt.AddComponent<PCSUVScroller>();
                _uvScroller.speed = spd / 0.2f;

                // Kinematic physics parent for package interaction
                GameObject physicsParent = new GameObject("Physics");
                physicsParent.transform.parent = _body.transform;
                physicsParent.transform.localPosition = Vector3.zero;
                physicsParent.transform.localRotation = Quaternion.identity;
                physicsParent.transform.localScale = Vector3.one;
                physicsParent.hideFlags = HideFlags.HideInHierarchy;

                Rigidbody rb = physicsParent.AddComponent<Rigidbody>();
                rb.isKinematic = true;

                _conveyor = physicsParent.AddComponent<PCSConveyor>();
                _conveyor.speed = spd;

                // Copy colliders from belt to physics parent
                foreach (MeshFilter mf in belt.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    MeshCollider mc = physicsParent.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                }
            }

            // Inner rail
            if (bendRailInnerPrefab != null)
            {
                GameObject rail = Instantiate(bendRailInnerPrefab, _body.transform);
                rail.name = "Bend Rail Inner";
                rail.transform.localPosition = Vector3.zero;
                rail.transform.localRotation = Quaternion.identity;
                rail.transform.localScale = new Vector3(ws, 1f, 1f);
            }

            // Outer rail
            if (bendRailOuterPrefab != null)
            {
                GameObject rail = Instantiate(bendRailOuterPrefab, _body.transform);
                rail.name = "Bend Rail Outer";
                rail.transform.localPosition = Vector3.zero;
                rail.transform.localRotation = Quaternion.identity;
                rail.transform.localScale = new Vector3(ws, 1f, 1f);
            }

            // Start end cap (at 0°, matches the straight conveyor input end)
            if (bendEndStartPrefab != null)
            {
                GameObject cap = Instantiate(bendEndStartPrefab, _body.transform);
                cap.name = "Bend End Start";
                cap.transform.localPosition = Vector3.zero;
                cap.transform.localRotation = Quaternion.identity;
                cap.transform.localScale = new Vector3(ws, 1f, 1f);
            }

            // End cap (rotated by bendAngle around Y, matches the straight conveyor output end)
            if (bendEndEndPrefab != null)
            {
                GameObject cap = Instantiate(bendEndEndPrefab, _body.transform);
                cap.name = "Bend End End";
                cap.transform.localPosition = Vector3.zero;
                cap.transform.localRotation = Quaternion.Euler(0f, bendAngle, 0f);
                cap.transform.localScale = new Vector3(ws, 1f, 1f);
            }

            ready = true;
        }

        /// <summary>
        /// Returns the world-space Transform at the output end of the bend,
        /// so you can snap a straight conveyor to it.
        /// </summary>
        public Transform GetOutputTransform()
        {
            GameObject helper = new GameObject("_BendOutputHelper");
            helper.transform.parent = transform;
            helper.transform.localPosition = Vector3.zero;
            helper.transform.localRotation = Quaternion.Euler(0f, bendAngle, 0f);
            return helper.transform;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (ready)
                CreateBend();
        }
#endif
    }
}
