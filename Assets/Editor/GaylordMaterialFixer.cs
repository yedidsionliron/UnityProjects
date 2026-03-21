using UnityEngine;
using UnityEditor;
using System.IO;

public static class GaylordMaterialFixer
{
    [MenuItem("Tools/Fix Gaylord Materials")]
    static void Fix()
    {
        const string fbxPath = "Assets/Gaylord.fbx";
        const string matFolder = "Assets/Materials/Gaylord";

        if (!AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath))
        {
            Debug.LogError("Gaylord.fbx not found at " + fbxPath);
            return;
        }

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("URP Lit shader not found — is URP installed?");
            return;
        }

        // Ensure output folder exists.
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(matFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Gaylord");

        // Extract every embedded material to its own .mat file.
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        int count = 0;
        foreach (Object asset in assets)
        {
            Material mat = asset as Material;
            if (mat == null) continue;

            string destPath = $"{matFolder}/{mat.name}.mat";

            // Only extract if not already done.
            if (!File.Exists(Path.GetFullPath(destPath)))
            {
                string error = AssetDatabase.ExtractAsset(mat, destPath);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"Failed to extract {mat.name}: {error}");
                    continue;
                }
            }
            count++;
        }

        AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        // Now fix the extracted .mat files.
        int fixed_ = 0;
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { matFolder });
        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            mat.shader = urpLit;
            mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            EditorUtility.SetDirty(mat);
            fixed_++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Gaylord: extracted {count} material(s), fixed {fixed_}.");
    }
}
