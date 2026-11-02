using UnityEngine;
public class SoundBank : MonoBehaviour
{
    public static SoundBank Instance { get; private set; }
    public AudioClip[] clips;
    [Range(0f, 1f)] public float volume = 1f;
    public Vector2 pitchRange = new Vector2(0.9f, 1.1f);
    public int voices = 16;
    [Range(0f, 1f)] public float spatialBlend = 1f;

    AudioSource[] pool;
    int next;

    void Awake()
    {
        Instance = this;

        pool = new AudioSource[voices];
        for (int i = 0; i < voices; i++)
        {
            var child = new GameObject("Voice " + i);
            child.transform.SetParent(transform, false);

            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 60f;
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
