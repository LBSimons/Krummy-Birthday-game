#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Relink Room Textures
/// Re-attaches the room's textures to the (already converted) URP materials by matching names.
/// Safe to run more than once: it only fills in empty texture slots.
/// </summary>
public static class RelinkRoomTextures
{
    const string TextureFolder = "Assets/krum_bedroom/Room Textures";
    const string MaterialFolder = "Assets/krum_bedroom";

    [MenuItem("Tools/Relink Room Textures")]
    static void Relink()
    {
        var textures = new Dictionary<string, Texture2D>();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            if (tex != null) textures[tex.name] = tex;
        }
        if (textures.Count == 0)
        {
            Debug.LogError("Relink Room Textures: no textures found in " + TextureFolder);
            return;
        }

        int linked = 0, already = 0;
        var unmatched = new StringBuilder();

        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            bool hasBase = mat.HasProperty("_BaseMap");
            if (hasBase && mat.GetTexture("_BaseMap") != null) { already++; continue; }

            Texture2D tex = Find(textures, mat.name);
            if (tex == null) { unmatched.AppendLine("  " + path); continue; }

            if (hasBase) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            EditorUtility.SetDirty(mat);
            linked++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Relink Room Textures: linked " + linked + ", already had a texture " + already +
                  ".\nMaterials with no matching texture (normal for plain-colour ones):\n" + unmatched);
    }

    static Texture2D Find(Dictionary<string, Texture2D> textures, string name)
    {
        string[] candidates =
        {
            name,
            name + " Base Color",
            name + "_textures",
            name + "_textures Base Color",
            name + " Base Color.001",
        };
        foreach (var c in candidates)
            if (textures.TryGetValue(c, out var t)) return t;
        return null;
    }
}
#endif
