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

    AudioSource[] pool;
    int next;

    void Awake()
    {
        Instance = this;

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

        var clip = clips[Random.Range(0, clips.Length)];
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
