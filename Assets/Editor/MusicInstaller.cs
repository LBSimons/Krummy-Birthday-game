#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Copies the song in Assets/Music ("audio [music].mp3") to Assets/Resources/BackgroundMusic so the game can load it in builds.</summary>
[InitializeOnLoad]
public static class MusicInstaller
{
    const string Src = "Assets/Music/audio [music].mp3";
    const string Dst = "Assets/Resources/BackgroundMusic/music.mp3";

    static MusicInstaller() { EditorApplication.delayCall += Run; }

    [MenuItem("Tools/Install Background Music")]
    public static void Run()
    {
        if (!File.Exists(Src) || File.Exists(Dst)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Dst));
        File.Copy(Src, Dst);
        AssetDatabase.Refresh();
        Debug.Log("MusicInstaller: background music installed to " + Dst);
    }
}
#endif
