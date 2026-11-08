#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Copies the videos from Assets/Videos into Assets/StreamingAssets/TvVideos so the TV can play them
/// (in the editor and in builds). Runs when Unity loads / recompiles and right before every build.
/// Drop new videos into Assets/Videos and they appear on the TV automatically.
/// </summary>
[InitializeOnLoad]
public static class TvVideoSync
{
    const string Src = "Assets/Videos";
    const string Dst = "Assets/StreamingAssets/TvVideos";
    static readonly string[] Exts = { ".mp4", ".mov", ".webm", ".m4v", ".avi", ".wmv", ".mpg", ".mpeg", ".ogv" };

    static TvVideoSync() { EditorApplication.delayCall += () => Sync(false); }

    [MenuItem("Tools/Sync TV Videos")]
    static void Menu() { Sync(true); }

    public static void Sync(bool log)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (!Directory.Exists(Src)) return;

        Directory.CreateDirectory(Dst);
        int changed = 0;

        foreach (var f in Directory.GetFiles(Src, "*", SearchOption.AllDirectories))
        {
            if (System.Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
            string dest = Path.Combine(Dst, Path.GetFileName(f));
            var a = new FileInfo(f);
            var b = new FileInfo(dest);
            if (!b.Exists || b.Length != a.Length)
            {
                File.Copy(f, dest, true);
                changed++;
            }
        }

        // remove copies whose original is gone
        foreach (var f in Directory.GetFiles(Dst))
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (System.Array.IndexOf(Exts, ext) < 0) continue;
            bool exists = false;
            foreach (var s in Directory.GetFiles(Src, Path.GetFileName(f), SearchOption.AllDirectories)) { exists = true; break; }
            if (!exists) { AssetDatabase.DeleteAsset(f.Replace('\\', '/')); changed++; }
        }

        if (changed > 0)
        {
            AssetDatabase.Refresh();
            Debug.Log("TvVideoSync: " + changed + " video file(s) updated for the TV.");
        }
        else if (log) Debug.Log("TvVideoSync: TV videos already up to date.");
    }
}

public class TvVideoBuildStep : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) { TvVideoSync.Sync(false); }
}
#endif
