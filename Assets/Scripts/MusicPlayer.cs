using UnityEngine;

/// <summary>Looping background song during gameplay. Loads the first clip found in Resources/BackgroundMusic.</summary>
public class MusicPlayer : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.35f;
    public float fadeSeconds = 1.5f;
    public AudioClip clip;                      // optional: assign by hand to override

    AudioSource src;
    bool wantPlay;

    void Awake()
    {
        if (clip == null)
        {
            var all = Resources.LoadAll<AudioClip>("BackgroundMusic");
            if (all.Length > 0) clip = all[0];
        }
        src = gameObject.AddComponent<AudioSource>();
        src.clip = clip; src.loop = true; src.playOnAwake = false;
        src.spatialBlend = 0f; src.volume = 0f;
        if (clip == null) Debug.LogWarning("MusicPlayer: no clip found in Resources/BackgroundMusic.");
    }

    public void Play()
    {
        wantPlay = true;
        if (clip != null && !src.isPlaying) src.Play();
    }

    public void Stop() { wantPlay = false; }

    void Update()
    {
        if (src == null || clip == null) return;
        float target = wantPlay ? volume : 0f;
        src.volume = Mathf.MoveTowards(src.volume, target, Time.unscaledDeltaTime * volume / Mathf.Max(0.01f, fadeSeconds));
        if (!wantPlay && src.volume <= 0f && src.isPlaying) src.Stop();
    }
}
