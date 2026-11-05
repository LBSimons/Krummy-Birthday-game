using UnityEngine;

/// <summary>
/// Holds your soundbank and plays a random clip at a position on request.
/// Put ONE of these in the scene and drag your clips into the Clips array.
/// </summary>
public class SoundBank : MonoBehaviour
{
    public static SoundBank Instance { get; private set; }

    [Tooltip("Drag all your sound clips here.")]
    public AudioClip[] clips;

    [Range(0f, 1f)] public float volume = 1f;

    [Tooltip("Random pitch range so repeated sounds don't feel identical. Set both to 1 to disable.")]
    public Vector2 pitchRange = new Vector2(0.9f, 1.1f);

    [Tooltip("How many sounds can play at the same time. The oldest is cut off when all are busy.")]
    public int voices = 16;

    [Tooltip("1 = fully 3D (louder near the hit). 0 = flat, same volume everywhere.")]
    [Range(0f, 1f)] public float flatness3D = 0.2f;

    [Tooltip("Also loads every clip inside Assets/Resources/KrumNoises, so you can just drop new sounds in that folder.")]
    public string extraClipsFolder = "KrumNoises";

    AudioSource[] pool;
    int next;
    readonly System.Collections.Generic.Queue<int> recent = new System.Collections.Generic.Queue<int>();

    void Awake()
    {
        Instance = this;

        // Merge the clips from the Resources folder with the ones assigned in the Inspector.
        if (!string.IsNullOrEmpty(extraClipsFolder))
        {
            var extra = Resources.LoadAll<AudioClip>(extraClipsFolder);
            if (extra.Length > 0)
            {
                var all = new System.Collections.Generic.List<AudioClip>();
                if (clips != null) foreach (var c in clips) if (c != null) all.Add(c);
                foreach (var c in extra) if (!all.Contains(c)) all.Add(c);
                clips = all.ToArray();
            }
            Debug.Log("SoundBank: " + (clips != null ? clips.Length : 0) + " clips loaded");
        }

        // One child object per voice, so each sound can sit at its own hit position
        // without dragging other playing sounds along with it.
        pool = new AudioSource[voices];
        for (int i = 0; i < voices; i++)
        {
            var child = new GameObject("Voice " + i);
            child.transform.SetParent(transform, false);

            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = flatness3D;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 10f;
            src.maxDistance = 100f;
            pool[i] = src;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void PlayRandom(Vector3 position, float volumeScale = 1f)
    {
        if (clips == null || clips.Length == 0 || pool == null) return;

        // random clip, but not one of the last few played
        int idx = Random.Range(0, clips.Length);
        for (int tries = 0; tries < 8 && recent.Contains(idx) && clips.Length > 12; tries++)
            idx = Random.Range(0, clips.Length);
        recent.Enqueue(idx);
        while (recent.Count > 6) recent.Dequeue();

        var clip = clips[idx];
        if (clip == null) return;

        var src = pool[next];
        next = (next + 1) % pool.Length;

        src.transform.position = position;
        src.clip = clip;
        src.volume = volume * volumeScale;
        src.pitch = Random.Range(pitchRange.x, pitchRange.y);
        src.Play();
    }
}
