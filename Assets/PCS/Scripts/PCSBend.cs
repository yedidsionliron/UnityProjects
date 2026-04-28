using UnityEngine;

namespace PCS
{
    /// <summary>
    /// Scales a conveyor bend GameObject to match the width of a PCSConfig belt.
    /// Attach this to the bend root GameObject and assign the matching PCSConfig.
    /// </summary>
    public class PCSBend : MonoBehaviour
    {
        [Tooltip("The PCSConfig whose belt width this bend should match.")]
        public PCSConfig config;

        [Tooltip("The bend part GameObject to scale (typically the child with the mesh).")]
        public GameObject bendPart;

        /// <summary>
        /// Apply the width scale to the bend part, matching PCSConfig's belt-width formula.
        /// Call this whenever the config width changes.
        /// </summary>
        public void ApplyWidth()
        {
            if (config == null || bendPart == null) return;

            float widthScale = (config.width - 0.4f) / 1.6f;
            Vector3 s = bendPart.transform.localScale;
            s.x = widthScale;
            bendPart.transform.localScale = s;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyWidth();
        }
#endif
    }
}
