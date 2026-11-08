#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unpacks any zip placed in Assets/DropIn into the Assets folder (paths inside the zip are relative to
/// Assets, e.g. "Resources/IntroVideo.mp4"), then deletes the zip. Used to deliver files that must arrive
/// byte-for-byte unchanged.
/// </summary>
[InitializeOnLoad]
public static class DropInInstaller
{
    const string Dir = "Assets/DropIn";

    static DropInInstaller() { EditorApplication.delayCall += Run; }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (!Directory.Exists(Dir)) return;

        var zips = Directory.GetFiles(Dir, "*.zip");
        if (zips.Length == 0) return;

        foreach (var z in zips)
        {
            try
            {
                int n = 0;
                using (var zip = ZipFile.OpenRead(z))
                {
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string rel = e.FullName.Replace('\\', '/');
                        if (rel.Contains("..")) continue;
                        string dest = Path.Combine("Assets", rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                        n++;
                    }
                }
                AssetDatabase.DeleteAsset(z.Replace('\\', '/'));
                Debug.Log("DropInInstaller: unpacked " + n + " file(s) from " + Path.GetFileName(z));
            }
            catch (System.Exception ex)
            {
                Debug.LogError("DropInInstaller failed on " + z + ": " + ex.Message);
            }
        }
        AssetDatabase.Refresh();
    }
}
#endif
