using UnityEditor;
using UnityEngine;

namespace PCS
{
    [CustomEditor(typeof(PCSBendConfig))]
    public class PCSBendInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GUILayout.Space(8);

            PCSBendConfig bend = (PCSBendConfig)target;

            if (GUILayout.Button("Build Bend"))
            {
                Undo.RecordObject(bend.gameObject, "Build Bend");
                bend.CreateBend();
            }
        }
    }
}
