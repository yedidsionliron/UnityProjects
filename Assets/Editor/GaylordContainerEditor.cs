using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GaylordContainer))]
public class GaylordContainerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gc = (GaylordContainer)target;

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Rebuild Colliders", GUILayout.Height(28)))
        {
            Undo.RecordObject(gc, "Rebuild Gaylord Colliders");
            gc.RebuildColliders();
            EditorUtility.SetDirty(gc);
        }

        EditorGUILayout.Space(2);

        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("Remove Colliders"))
        {
            Undo.RegisterFullObjectHierarchyUndo(gc.gameObject, "Remove Gaylord Colliders");
            gc.DestroyColliders();
            EditorUtility.SetDirty(gc.gameObject);
        }
        GUI.backgroundColor = Color.white;
    }
}
