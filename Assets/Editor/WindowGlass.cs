#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes the materials on the "window planes" objects see-through (URP Lit, transparent).
/// Runs once automatically; run again from Tools > Make Windows See-Through. Edits the material assets,
/// so it also works in builds.
/// </summary>
[InitializeOnLoad]
public static class WindowGlass
{
    const string Key = "krum_glass_v1";
    const float Alpha = 0.12f;

    static WindowGlass()
    {
        EditorApplication.delayCall += () => { if (!EditorPrefs.GetBool(Key, false)) Run(); };
    }

    [MenuItem("Tools/Make Windows See-Through")]
    public static void Run()
    {
        var done = new System.Collections.Generic.HashSet<Material>();
        int n = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!r.name.ToLower().Contains("window plane")) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !done.Add(m)) continue;
                if (!AssetDatabase.Contains(m)) continue;
                MakeGlass(m); n++;
            }
        }
        AssetDatabase.SaveAssets();
        if (n > 0) EditorPrefs.SetBool(Key, true);
        Debug.Log("WindowGlass: made " + n + " material(s) see-through" + (n == 0 ? " (no 'window planes' found in the open scene)" : "."));
    }

    static void MakeGlass(Material m)
    {
        Undo.RecordObject(m, "Glass");
        m.SetFloat("_Surface", 1f);                 // transparent
        m.SetFloat("_Blend", 0f);                   // alpha blend
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = (int)RenderQueue.Transparent;
        foreach (var prop in new[] { "_BaseColor", "_Color" })
            if (m.HasProperty(prop)) { var c = m.GetColor(prop); c.a = Alpha; m.SetColor(prop, c); }
        EditorUtility.SetDirty(m);
    }
}
#endif
