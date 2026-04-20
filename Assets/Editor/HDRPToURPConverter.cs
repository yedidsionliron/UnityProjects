using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > HDRP to URP
///   1. Convert Materials   – re-shaders every material under Assets/UnityWarehouseSceneHDRP
///   2. Remove Missing Scripts (Scene)   – strips null-MonoBehaviour slots from loaded scene
///   3. Remove Missing Scripts (Prefabs) – strips null-MonoBehaviour slots from prefab assets
/// </summary>
public static class HDRPToURPConverter
{
    private const string Folder = "Assets/UnityWarehouseSceneHDRP";

    // ── 1. Material conversion ──────────────────────────────────────────────

    [MenuItem("Tools/HDRP to URP/1 – Convert Materials")]
    public static void ConvertMaterials()
    {
        Shader urpLit   = Shader.Find("Universal Render Pipeline/Lit");
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");

        if (urpLit == null)
        {
            Debug.LogError("[HDRPtoURP] Universal Render Pipeline/Lit not found. Is URP installed?");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { Folder });
        int converted = 0;

        foreach (string guid in guids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(guid);
            Material mat   = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            // ── Read raw .mat file to extract saved properties ─────────────
            string raw      = File.ReadAllText(matPath);
            var textures    = ParseTexEnvs(raw);
            var colors      = ParseColors(raw);
            var floats      = ParseFloats(raw);

            string shaderName = mat.shader != null ? mat.shader.name : "";
            bool isUnlit      = shaderName.Contains("nlit");
            mat.shader        = isUnlit ? urpUnlit : urpLit;

            // Base color + texture
            Texture baseMap = First(textures, "_BaseColorMap", "_MainTex", "_AlbedoMap", "_Albedo_Map");
            Color   baseCol = First(colors,   "_BaseColor", "_Color");
            Debug.Log($"[HDRPtoURP] {mat.name}: textures={textures.Count} base={baseMap?.name ?? "null"} color={baseCol}");
            if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", baseCol);

            // Normal map
            Texture normalMap = First(textures, "_NormalMap", "_Normal_Map", "_BumpMap");
            if (normalMap != null)
            {
                mat.SetTexture("_BumpMap", normalMap);
                mat.EnableKeyword("_NORMALMAP");
                float ns = First(floats, 1f, "_NormalScale", "_Normal_Scale", "_BumpScale");
                mat.SetFloat("_BumpScale", ns);
            }

            // Metallic / Smoothness
            mat.SetFloat("_Metallic",   Mathf.Clamp01(First(floats, 0f,   "_Metallic", "_Meallic")));
            mat.SetFloat("_Smoothness", Mathf.Clamp01(First(floats, 0.5f,
                "_Base_Smoothness_Max", "_Smoothness_Max", "_Smoothness", "_SmoothnessRemapMax")));

            // Occlusion
            Texture aoMap = First(textures, "_OcclusionMap", "_MaskMap",
                "_Ambient_Occlusion_Map", "_AmbientOccluison_Map", "_AmbientOcclusion_Map");
            if (aoMap != null) mat.SetTexture("_OcclusionMap", aoMap);

            // Emission
            Color   emCol = First(colors,   "_EmissiveColor", "_EmissiveColorLDR");
            Texture emTex = First(textures, "_EmissiveColorMap");
            if (emCol != Color.black || emTex != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emCol);
                if (emTex != null) mat.SetTexture("_EmissionMap", emTex);
            }

            EditorUtility.SetDirty(mat);
            converted++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HDRPtoURP] Materials: {converted} converted.");
    }

    // ── YAML parsers ────────────────────────────────────────────────────────

    /// Parses m_TexEnvs block.  Returns map of propName → Texture asset.
    static Dictionary<string, Texture> ParseTexEnvs(string yaml)
    {
        var result = new Dictionary<string, Texture>();

        // Match blocks like:
        //   - _BaseColorMap:
        //       m_Texture: {fileID: 2800000, guid: abc123, type: 3}
        var blockRx = new Regex(
            @"-\s+(\w+):\s*\n\s+m_Texture:\s*\{[^}]*guid:\s*([0-9a-f]+)",
            RegexOptions.Multiline);

        foreach (Match m in blockRx.Matches(yaml))
        {
            string propName = m.Groups[1].Value;
            string texGuid  = m.Groups[2].Value;
            if (string.IsNullOrEmpty(texGuid) || texGuid == "0000000000000000000000000000000000000000") continue;

            string texPath = AssetDatabase.GUIDToAssetPath(texGuid);
            if (string.IsNullOrEmpty(texPath)) continue;

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
            if (tex != null) result[propName] = tex;
        }
        return result;
    }

    /// Parses m_Colors block.  Returns map of propName → Color.
    static Dictionary<string, Color> ParseColors(string yaml)
    {
        var result = new Dictionary<string, Color>();

        // Match lines like:  - _BaseColor: {r: 1, g: 0.5, b: 0.2, a: 1}
        var rx = new Regex(
            @"-\s+(\w+):\s*\{r:\s*([\d.]+),\s*g:\s*([\d.]+),\s*b:\s*([\d.]+),\s*a:\s*([\d.]+)\}");

        foreach (Match m in rx.Matches(yaml))
        {
            if (!float.TryParse(m.Groups[2].Value, out float r)) continue;
            if (!float.TryParse(m.Groups[3].Value, out float g)) continue;
            if (!float.TryParse(m.Groups[4].Value, out float b)) continue;
            if (!float.TryParse(m.Groups[5].Value, out float a)) continue;
            result[m.Groups[1].Value] = new Color(r, g, b, a);
        }
        return result;
    }

    /// Parses m_Floats block.  Returns map of propName → float.
    static Dictionary<string, float> ParseFloats(string yaml)
    {
        var result = new Dictionary<string, float>();

        // Match lines like:  - _Metallic: 0.5
        var rx = new Regex(@"-\s+(\w+):\s*([\d.\-]+)");

        foreach (Match m in rx.Matches(yaml))
        {
            if (float.TryParse(m.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                result[m.Groups[1].Value] = v;
        }
        return result;
    }

    // ── 4. Re-link textures by name matching ───────────────────────────────

    [MenuItem("Tools/HDRP to URP/4 – Re-link Textures by Name")]
    public static void RelinkTextures()
    {
        // Build lookup: normalised-name → texture asset path
        string[] texGuids = AssetDatabase.FindAssets("t:Texture", new[] { Folder });
        // key = lowercase name without extension, value = asset path
        var texByName = new Dictionary<string, string>();
        foreach (var tg in texGuids)
        {
            string p    = AssetDatabase.GUIDToAssetPath(tg);
            string name = Path.GetFileNameWithoutExtension(p).ToLower().Replace(" ", "_");
            texByName[name] = p;
        }

        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { Folder });
        int linked = 0;

        foreach (var mg in matGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(mg);
            Material mat   = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            // Only touch URP/Lit materials (already converted)
            if (!mat.shader.name.StartsWith("Universal Render Pipeline")) continue;

            string baseName = Path.GetFileNameWithoutExtension(matPath).ToLower();
            bool dirty = false;

            // _BaseMap – exact name, or strip trailing _shadow/_glass/_mirror qualifiers
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null)
            {
                Texture t = FindTex(texByName, baseName, "", "_a");
                if (t == null) t = FindTex(texByName, StripQualifier(baseName), "", "_a");
                if (t != null) { mat.SetTexture("_BaseMap", t); dirty = true; }
            }

            // _BumpMap – suffix _n or _normal
            if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") == null)
            {
                Texture t = FindTex(texByName, baseName, "_n", "_normal");
                if (t == null) t = FindTex(texByName, StripQualifier(baseName), "_n", "_normal");
                if (t != null)
                {
                    mat.SetTexture("_BumpMap", t);
                    mat.EnableKeyword("_NORMALMAP");
                    dirty = true;
                }
            }

            // _EmissionMap – suffix _e
            if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") == null)
            {
                Texture t = FindTex(texByName, baseName, "_e");
                if (t != null)
                {
                    mat.SetTexture("_EmissionMap", t);
                    mat.SetColor("_EmissionColor", Color.white);
                    mat.EnableKeyword("_EMISSION");
                    dirty = true;
                }
            }

            // _OcclusionMap – suffix _ao, _sao
            if (mat.HasProperty("_OcclusionMap") && mat.GetTexture("_OcclusionMap") == null)
            {
                Texture t = FindTex(texByName, baseName, "_ao", "_sao", "_msao");
                if (t == null) t = FindTex(texByName, StripQualifier(baseName), "_ao", "_sao", "_msao");
                if (t != null) { mat.SetTexture("_OcclusionMap", t); dirty = true; }
            }

            if (dirty) { EditorUtility.SetDirty(mat); linked++; }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HDRPtoURP] Re-linked textures on {linked} materials.");
    }

    // Return a texture whose filename = baseName + suffix (tries each suffix in order)
    static Texture FindTex(Dictionary<string, string> lookup, string baseName, params string[] suffixes)
    {
        foreach (var s in suffixes)
        {
            string key = baseName + s;
            if (lookup.TryGetValue(key, out string path))
                return AssetDatabase.LoadAssetAtPath<Texture>(path);
        }
        return null;
    }

    // "forklift_light" → "forklift",  "worker_body" → "worker_body" (no change if no known qualifier)
    static readonly string[] Qualifiers = { "_light", "_shadow", "_glass", "_mirror", "_patolamp",
                                            "_meter", "_bottom", "_link", "_beam", "_guard", "_pillar",
                                            "_001", "_007", "_rail", "_sus", "_rubber", "_bollard" };
    static string StripQualifier(string name)
    {
        foreach (var q in Qualifiers)
            if (name.EndsWith(q)) return name.Substring(0, name.Length - q.Length);
        return name;
    }

    // ── 6. Fix transparent materials (shadows, glass) ───────────────────────

    [MenuItem("Tools/HDRP to URP/6 – Fix Transparent Materials")]
    public static void FixTransparentMaterials()
    {
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        Shader urpLit   = Shader.Find("Universal Render Pipeline/Lit");
        int count = 0;

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { Folder });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path).ToLower();
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            bool isShadow = name.Contains("shadow");
            bool isGlass  = name.Contains("glass") || name.Contains("mirror");
            bool isSkybox = name.Contains("skybox") || name.Contains("outside") || name.Contains("refrection");

            if (!isShadow && !isGlass && !isSkybox) continue;

            if (isSkybox)
            {
                // Leave skybox alone – Unity handles these separately
                continue;
            }

            if (isShadow)
            {
                mat.shader = urpUnlit;
                MakeTransparent(mat);
                mat.SetColor("_BaseColor", new Color(0, 0, 0, 0.45f));
                // Keep whatever texture was already assigned as base
            }

            if (isGlass)
            {
                mat.shader = urpLit;
                MakeTransparent(mat);
                mat.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f, 0.15f));
                mat.SetFloat("_Smoothness", 0.95f);
                mat.SetFloat("_Metallic",   0f);
            }

            EditorUtility.SetDirty(mat);
            count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HDRPtoURP] Fixed {count} transparent materials.");
    }

    static void MakeTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend",   0f);   // Alpha blend
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite",    0);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
    }

    // ── 5. Fix light intensities (HDRP physical → URP) ─────────────────────

    [MenuItem("Tools/HDRP to URP/5 – Fix Light Intensities")]
    public static void FixLightIntensities()
    {
        int count = 0;
        foreach (var light in Object.FindObjectsByType<Light>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.intensity <= 10f) continue; // already reasonable

            float oldIntensity = light.intensity;
            switch (light.type)
            {
                case LightType.Directional:
                    light.intensity = 1f;   // standard URP sun
                    break;
                case LightType.Point:
                case LightType.Spot:
                    light.intensity = 5f;   // bright indoor fixture
                    break;
                case LightType.Rectangle:
                    light.intensity = 3f;
                    break;
                default:
                    light.intensity = 1f;
                    break;
            }
            Debug.Log($"[HDRPtoURP] Light '{light.name}': {oldIntensity:F0} → {light.intensity}");
            EditorUtility.SetDirty(light.gameObject);
            count++;
        }
        Debug.Log($"[HDRPtoURP] Fixed {count} lights.");
    }

    // ── 7. Strip HDRP-only objects from all scene YAML files ────────────────

    [MenuItem("Tools/HDRP to URP/7 – Strip HDRP Scene Objects (All Scenes)")]
    public static void StripHDRPSceneObjects()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { Folder });
        int scenesFixed = 0;

        foreach (var sg in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(sg);
            string text      = File.ReadAllText(scenePath);
            string original  = text;

            text = StripHDRPFromSceneText(text);

            if (text == original) continue;

            File.WriteAllText(scenePath, text);
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
            scenesFixed++;
            Debug.Log($"[HDRPtoURP] Cleaned: {scenePath}");
        }

        Debug.Log($"[HDRPtoURP] Scene YAML cleanup done: {scenesFixed} scenes modified.");
    }

    static string StripHDRPFromSceneText(string text)
    {
        // Step 1 – find fileIDs of all HDRP MonoScript anchors
        var hdrpScriptIds = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in Regex.Matches(text,
            @"--- !u!\d+ &(\d+)\r?\nMonoScript:.*?m_AssemblyName: Unity\.RenderPipelines\.HighDefinition\.[^\r\n]+",
            RegexOptions.Singleline))
            hdrpScriptIds.Add(m.Groups[1].Value);

        if (hdrpScriptIds.Count == 0) return text;

        // Step 2 – find fileIDs of MonoBehaviours that reference those scripts
        //          and the GameObject fileIDs they live on
        var mbIds = new System.Collections.Generic.HashSet<string>();
        var goIds = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in Regex.Matches(text,
            @"--- !u!114 &(\d+)\r?\nMonoBehaviour:.*?m_GameObject: \{fileID: (\d+)\}.*?m_Script: \{fileID: (\d+)\}",
            RegexOptions.Singleline))
        {
            if (hdrpScriptIds.Contains(m.Groups[3].Value))
            {
                mbIds.Add(m.Groups[1].Value);
                goIds.Add(m.Groups[2].Value);
            }
        }

        // Step 3 – find Transform fileIDs for those GameObjects
        var transformIds = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in Regex.Matches(text,
            @"--- !u!4 &(\d+)\r?\nTransform:.*?m_GameObject: \{fileID: (\d+)\}",
            RegexOptions.Singleline))
        {
            if (goIds.Contains(m.Groups[2].Value))
                transformIds.Add(m.Groups[1].Value);
        }

        // Step 4 – remove each identified block (MonoScript, GameObject, MonoBehaviour, Transform)
        var removeIds = new System.Collections.Generic.HashSet<string>(hdrpScriptIds);
        removeIds.UnionWith(mbIds);
        removeIds.UnionWith(goIds);
        removeIds.UnionWith(transformIds);

        // Remove YAML blocks whose anchor is in removeIds
        text = Regex.Replace(text,
            @"--- !u!\d+ &(\d+)\r?\n(?:(?!--- !u!)[\s\S])*",
            m => removeIds.Contains(m.Groups[1].Value) ? "" : m.Value);

        // Step 5 – remove component list entries pointing to removed fileIDs
        foreach (var id in removeIds)
            text = text.Replace($"  - component: {{fileID: {id}}}\n", "")
                       .Replace($"  - component: {{fileID: {id}}}\r\n", "");

        // Step 6 – remove root transform list entries pointing to removed fileIDs
        foreach (var id in transformIds)
            text = text.Replace($"  - {{fileID: {id}}}\n", "")
                       .Replace($"  - {{fileID: {id}}}\r\n", "");

        return text;
    }

    // ── 8. Fix shadow-casting lights ────────────────────────────────────────

    [MenuItem("Tools/HDRP to URP/8 – Fix Light Shadows")]
    public static void FixLightShadows()
    {
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int disabled = 0;

        foreach (var l in lights)
        {
            if (l.shadows == LightShadows.None) continue;
            l.shadows = LightShadows.None;
            EditorUtility.SetDirty(l.gameObject);
            disabled++;
        }

        Debug.Log($"[HDRPtoURP] Shadows disabled on {disabled} lights.");
    }

    // ── 9. Disable shadows in scene YAML files directly ───────────────────────

    [MenuItem("Tools/HDRP to URP/9 – Disable Light Shadows in Scene Files")]
    public static void DisableShadowsInSceneFiles()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { Folder });
        int scenesFixed = 0, lightsFixed = 0;

        foreach (var sg in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(sg);
            string text      = File.ReadAllText(scenePath);
            string original  = text;

            // Unity 6 format: m_Shadows is a nested block, m_Type: 1=Hard 2=Soft 0=None
            int before = Regex.Matches(text, @"    m_Type: [12]").Count;
            text = Regex.Replace(text, @"(    m_Type:) [12]", "${1} 0");
            lightsFixed += before;

            if (text == original) continue;

            File.WriteAllText(scenePath, text);
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
            scenesFixed++;
            Debug.Log($"[HDRPtoURP] Shadow-disabled in: {scenePath}");
        }

        Debug.Log($"[HDRPtoURP] Disabled shadows on {lightsFixed} lights across {scenesFixed} scene files.");
    }

    // ── 2. Remove missing scripts from loaded scene ─────────────────────────

    [MenuItem("Tools/HDRP to URP/2 – Remove Missing Scripts (Scene)")]
    public static void RemoveMissingScriptsScene()
    {
        int removed = 0;

        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (n > 0) { removed += n; EditorUtility.SetDirty(t.gameObject); }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[HDRPtoURP] Scene: removed {removed} missing-script slots.");
    }

    // ── 3. Remove missing scripts from prefab assets ────────────────────────

    [MenuItem("Tools/HDRP to URP/3 – Remove Missing Scripts (Prefabs)")]
    public static void RemoveMissingScriptsPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { Folder });
        int removed = 0, prefabsEdited = 0;

        foreach (string guid in guids)
        {
            string path     = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool dirty      = false;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (n > 0) { removed += n; dirty = true; }
            }

            if (dirty) { PrefabUtility.SaveAsPrefabAsset(root, path); prefabsEdited++; }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HDRPtoURP] Prefabs: removed {removed} missing-script slots across {prefabsEdited} prefabs.");
    }

    // ── Lookup helpers ───────────────────────────────────────────────────────

    static Texture First(Dictionary<string, Texture> d, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var v)) return v;
        return null;
    }

    static Color First(Dictionary<string, Color> d, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var v)) return v;
        return Color.white;
    }

    static float First(Dictionary<string, float> d, float def, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var v)) return v;
        return def;
    }
}
