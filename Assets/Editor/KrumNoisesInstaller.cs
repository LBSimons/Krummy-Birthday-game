#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time installer: unpacks Assets/KrumNoisesPack.zip into Assets/Resources/KrumNoises
/// (SoundBank loads that folder automatically), then deletes the zip.
/// </summary>
[InitializeOnLoad]
public static class KrumNoisesInstaller
{
    const string ZipPath = "Assets/KrumNoisesPack.zip";
    const string OutDir = "Assets/Resources/KrumNoises";

    static KrumNoisesInstaller()
    {
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (!File.Exists(ZipPath)) return;

        try
        {
            Directory.CreateDirectory(OutDir);
            int n = 0;
            using (var zip = ZipFile.OpenRead(ZipPath))
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    string dest = Path.Combine(OutDir, Path.GetFileName(e.Name));
                    e.ExtractToFile(dest, true);
                    n++;
                }
            }
            AssetDatabase.DeleteAsset(ZipPath);
            AssetDatabase.Refresh();
            Debug.Log("KrumNoisesInstaller: unpacked " + n + " new sounds into " + OutDir);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("KrumNoisesInstaller failed: " + ex.Message + "  (you can unzip Assets/KrumNoisesPack.zip into Assets/Resources/KrumNoises by hand)");
        }
    }
}
#endif
