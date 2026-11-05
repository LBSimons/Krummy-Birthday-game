#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes the built game behave like the editor.
/// In a build, convex colliders and room colliders are made from the model's mesh data at load time.
/// If a model is not "Read/Write" enabled that data is missing, so the colliders silently don't exist
/// (walls without collision, the plush falling through the floor). This turns Read/Write on for every
/// model in Assets and switches on "Prebake Collision Meshes".
/// Runs automatically whenever Unity loads or recompiles; no setup needed.
/// </summary>
[InitializeOnLoad]
public static class BuildFixes
{
    static BuildFixes()
    {
        // Wait until Unity has finished loading before touching assets.
        EditorApplication.delayCall += Apply;
    }

    static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

        if (!PlayerSettings.bakeCollisionMeshes)
        {
            PlayerSettings.bakeCollisionMeshes = true;
            Debug.Log("BuildFixes: turned on 'Prebake Collision Meshes'.");
        }

        int fixedCount = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null || imp.isReadable) continue;
            imp.isReadable = true;
            imp.SaveAndReimport();
            fixedCount++;
        }
        if (fixedCount > 0) Debug.Log("BuildFixes: turned on Read/Write for " + fixedCount + " model(s) so colliders work in builds.");
    }
}

/// <summary>Any model imported later gets Read/Write on too.</summary>
public class BuildFixesPostprocessor : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        var imp = (ModelImporter)assetImporter;
        if (!imp.isReadable) imp.isReadable = true;
    }
}
#endif
