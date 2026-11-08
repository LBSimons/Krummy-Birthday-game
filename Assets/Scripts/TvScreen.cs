using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Plays the videos from Assets/Videos on the TV's screen in the room.
/// Created by GameFlow. Videos are copied to StreamingAssets/TvVideos by the editor script TvVideoSync,
/// so new videos you drop into Assets/Videos show up automatically (also in builds).
/// </summary>
public class TvScreen : MonoBehaviour
{
    [Header("Where")]
    [Tooltip("Name of the TV object inside the room.")]
    public string tvObjectName = "tv";
    [Tooltip("Folder inside StreamingAssets that holds the videos.")]
    public string videoFolder = "TvVideos";

    [Tooltip("Tick this if the picture shows up on the wrong side of the glass (inside / at the back of the TV).")]
    public bool flipSide = false;

    [Header("Playing")]
    public bool shuffle = false;
    [Range(0f, 1f)] public float volume = 0.6f;
    [Tooltip("0 = sound is the same everywhere, 1 = gets quieter the further you are from the TV.")]
    [Range(0f, 1f)] public float soundDistanceEffect = 0.7f;
    [Tooltip("Screen picture is pushed this far (metres) out of the glass so it doesn't flicker.")]
    public float screenOffset = 0.003f;
    [Tooltip("The picture is shrunk by this fraction so it stays inside the rounded corners.")]
    [Range(0f, 0.3f)] public float screenInset = 0.05f;
    [Tooltip("Makes the screen glow (picture stays visible even in a dark room).")]
    public float glow = 1f;

    VideoPlayer vp;
    AudioSource audioSrc;
    RenderTexture rt;
    Material screenMat;
    Mesh quad;
    Transform tv;
    Vector3[] corners;             // bl, tl, tr, br in tv-local space (full screen rectangle)
    Vector3 localNormal, localRight, localUp;
    float screenW, screenH;        // metres
    readonly List<string> files = new List<string>();
    int index = -1;
    bool built, started;

    // ------------------------------------------------------------ public

    public void Begin()
    {
        if (started) return;
        started = true;
        if (!Build()) return;
        LoadFileList();
        if (files.Count == 0)
        {
            Debug.Log("TvScreen: no videos found in StreamingAssets/" + videoFolder + " (put them in Assets/Videos).");
            return;
        }
        index = shuffle ? Random.Range(0, files.Count) - 1 : -1;
        PlayNext();
    }

    public void PlayNext()
    {
        if (files.Count == 0 || vp == null) return;
        if (shuffle && files.Count > 1)
        {
            int n; do { n = Random.Range(0, files.Count); } while (n == index);
            index = n;
        }
        else index = (index + 1) % files.Count;

        vp.Stop();
        vp.url = new System.Uri(files[index]).AbsoluteUri;
        vp.Prepare();
    }

    // ------------------------------------------------------------ build the screen

    bool Build()
    {
        if (built) return true;

        var room = GameObject.Find("room");
        Transform found = null;
        if (room != null)
            foreach (var t in room.GetComponentsInChildren<Transform>(true))
                if (t.name == tvObjectName) { found = t; break; }
        if (found == null) { var go = GameObject.Find(tvObjectName); if (go != null) found = go.transform; }
        if (found == null) { Debug.LogWarning("TvScreen: couldn't find an object called '" + tvObjectName + "'."); return false; }
        tv = found;

        var mf = tv.GetComponent<MeshFilter>();
        var mr = tv.GetComponent<MeshRenderer>();
        if (mf == null || mr == null || mf.sharedMesh == null) { Debug.LogWarning("TvScreen: TV has no mesh."); return false; }
        var mesh = mf.sharedMesh;
        if (!mesh.isReadable) { Debug.LogWarning("TvScreen: TV mesh isn't Read/Write enabled (BuildFixes turns that on for models)."); return false; }

        if (!FindScreenRectangle(mesh, mr)) { Debug.LogWarning("TvScreen: couldn't locate the screen on the TV mesh."); return false; }

        // material: copy a material the TV already uses (so its shader is guaranteed to be in builds)
        Material src = mr.sharedMaterials.Length > 0 ? mr.sharedMaterials[0] : null;
        if (src == null) return false;
        screenMat = new Material(src) { name = "TV Screen" };
        screenMat.mainTexture = null;
        SetColor(screenMat, "_BaseColor", Color.white);
        SetColor(screenMat, "_Color", Color.white);
        SetFloat(screenMat, "_Smoothness", 0.15f);
        SetFloat(screenMat, "_Glossiness", 0.15f);
        SetFloat(screenMat, "_Metallic", 0f);
        SetFloat(screenMat, "_Cull", 0f);          // visible from both sides, can never disappear

        // the screen quad
        var go2 = new GameObject("TV Screen Picture");
        go2.transform.SetParent(tv, false);
        quad = new Mesh { name = "TV Screen Quad" };
        quad.MarkDynamic();
        go2.AddComponent<MeshFilter>().sharedMesh = quad;
        var qr = go2.AddComponent<MeshRenderer>();
        qr.sharedMaterial = screenMat;
        qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        qr.receiveShadows = false;
        SetQuad(16f / 9f);

        // audio from the TV
        audioSrc = go2.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = soundDistanceEffect;
        audioSrc.minDistance = 2f; audioSrc.maxDistance = 25f;
        audioSrc.volume = volume;

        // video player
        vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.source = VideoSource.Url;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.timeUpdateMode = VideoTimeUpdateMode.GameTime;      // pauses with the game
        vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
        vp.controlledAudioTrackCount = 1;
        vp.SetTargetAudioSource(0, audioSrc);
        vp.prepareCompleted += OnPrepared;
        vp.loopPointReached += _ => PlayNext();
        vp.errorReceived += OnError;

        built = true;
        return true;
    }

    // Finds the screen glass: the biggest flat face inside the TV's "screen" material slot.
    bool FindScreenRectangle(Mesh mesh, MeshRenderer mr)
    {
        var mats = mr.sharedMaterials;
        int sub = -1;
        for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
            if (mats[i] != null && mats[i].name.StartsWith("Material.023")) { sub = i; break; }
        if (sub < 0)   // fallback: the slot with the fewest triangles
        {
            int best = int.MaxValue;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                int c = (int)mesh.GetIndexCount(i);
                if (c > 0 && c < best) { best = c; sub = i; }
            }
        }
        if (sub < 0) return false;

        var verts = mesh.vertices;
        var tris = mesh.GetTriangles(sub);

        // group triangles by plane (in world space), pick the group with the biggest area
        var groups = new Dictionary<long, (float area, List<int> idx, Vector3 n)>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 a = tv.TransformPoint(verts[tris[t]]), b = tv.TransformPoint(verts[tris[t + 1]]), c = tv.TransformPoint(verts[tris[t + 2]]);
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float area = cross.magnitude * 0.5f;
            if (area < 1e-8f) continue;
            Vector3 n = cross.normalized;
            // (all triangles of one flat face share the same winding, so n is consistent within a face)
            if (Mathf.Abs(n.y) > 0.5f) continue;            // we want an upright face
            long key = ((long)Mathf.RoundToInt(n.x * 20f) & 0xFFFF) | (((long)Mathf.RoundToInt(n.z * 20f) & 0xFFFF) << 16)
                       | (((long)Mathf.RoundToInt(Vector3.Dot(n, a) / 0.01f) & 0xFFFFFF) << 32);
            if (!groups.TryGetValue(key, out var g)) g = (0f, new List<int>(), n);
            g.area += area; g.idx.Add(tris[t]); g.idx.Add(tris[t + 1]); g.idx.Add(tris[t + 2]); g.n = n;
            groups[key] = g;
        }
        if (groups.Count == 0) return false;

        (float area, List<int> idx, Vector3 n) top = default; bool have = false;
        foreach (var g in groups.Values) if (!have || g.area > top.area) { top = g; have = true; }

        Vector3 nw = top.n;

        // "Out of the TV" = away from the middle of the TV. Decide the sign from geometry, not from winding.
        Vector3 centroid = Vector3.zero;
        foreach (int vi in top.idx) centroid += tv.TransformPoint(verts[vi]);
        centroid /= top.idx.Count;
        if (Vector3.Dot(nw, centroid - mr.bounds.center) < 0f) nw = -nw;
        if (flipSide) nw = -nw;

        Vector3 up = Vector3.up;
        Vector3 right = Vector3.Cross(up, nw).normalized;
        if (right.sqrMagnitude < 0.01f) return false;
        up = Vector3.Cross(nw, right).normalized;

        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue, planeD = 0f;
        foreach (int vi in top.idx)
        {
            Vector3 w = tv.TransformPoint(verts[vi]);
            float r = Vector3.Dot(w, right), u = Vector3.Dot(w, up);
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (u < minU) minU = u; if (u > maxU) maxU = u;
            planeD += Vector3.Dot(w, nw);
        }
        planeD /= top.idx.Count;
        planeD += screenOffset;

        Vector3 Pt(float pr, float pu) => right * pr + up * pu + nw * planeD;
        Vector3 bl = Pt(minR, minU), tl = Pt(minR, maxU), tr = Pt(maxR, maxU), br = Pt(maxR, minU);
        corners = new[] { tv.InverseTransformPoint(bl), tv.InverseTransformPoint(tl), tv.InverseTransformPoint(tr), tv.InverseTransformPoint(br) };
        localNormal = tv.InverseTransformDirection(nw).normalized;
        localRight = (corners[3] - corners[0]).normalized;
        localUp = (corners[1] - corners[0]).normalized;
        screenW = maxR - minR; screenH = maxU - minU;
        Debug.Log("TvScreen: screen found, " + screenW.ToString("0.00") + " x " + screenH.ToString("0.00") + " m");
        return true;
    }

    // Fit the picture into the screen rectangle (keeps the video's shape, black bars = the dark glass).
    void SetQuad(float videoAspect)
    {
        if (corners == null || quad == null) return;
        float w = screenW * (1f - screenInset), h = screenH * (1f - screenInset);
        float fitW = w, fitH = w / videoAspect;
        if (fitH > h) { fitH = h; fitW = h * videoAspect; }
        float fx = fitW / screenW, fy = fitH / screenH;   // fractions of the full screen rectangle

        Vector3 c0 = corners[0], dR = corners[3] - corners[0], dU = corners[1] - corners[0];
        Vector3 centre = c0 + dR * 0.5f + dU * 0.5f;
        Vector3 hr = dR * 0.5f * fx, hu = dU * 0.5f * fy;
        var v = new[] { centre - hr - hu, centre - hr + hu, centre + hr + hu, centre + hr - hu };

        quad.Clear();
        quad.vertices = v;
        quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        quad.normals = new[] { localNormal, localNormal, localNormal, localNormal };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        quad.RecalculateBounds();
    }

    // ------------------------------------------------------------ playing

    void LoadFileList()
    {
        files.Clear();
        string dir = Path.Combine(Application.streamingAssetsPath, videoFolder);
        if (!Directory.Exists(dir)) return;
        string[] ok = { ".mp4", ".mov", ".webm", ".m4v", ".avi", ".wmv", ".mpg", ".mpeg", ".ogv" };
        foreach (var f in Directory.GetFiles(dir))
            if (System.Array.IndexOf(ok, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
        files.Sort(System.StringComparer.OrdinalIgnoreCase);
    }

    void OnPrepared(VideoPlayer p)
    {
        int w = (int)p.width, h = (int)p.height;
        if (w <= 0 || h <= 0) { w = 1280; h = 720; }
        // keep the texture reasonably small
        float s = Mathf.Min(1f, 1280f / w);
        int rw = Mathf.Max(16, Mathf.RoundToInt(w * s)), rh = Mathf.Max(16, Mathf.RoundToInt(h * s));

        if (rt == null || rt.width != rw || rt.height != rh)
        {
            var old = rt;
            rt = new RenderTexture(rw, rh, 0, RenderTextureFormat.ARGB32) { name = "TV Video", wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            p.targetTexture = rt;
            SetTexture(rt);
            if (old != null) { old.Release(); Destroy(old); }
        }
        SetQuad((float)w / h);
        p.Play();
    }

    void OnError(VideoPlayer p, string msg)
    {
        Debug.LogWarning("TvScreen: video problem (" + (index >= 0 && index < files.Count ? Path.GetFileName(files[index]) : "?") + "): " + msg);
        if (files.Count > 1) StartCoroutine(NextSoon());
    }

    IEnumerator NextSoon() { yield return new WaitForSecondsRealtime(1f); PlayNext(); }

    void SetTexture(Texture t)
    {
        if (screenMat.HasProperty("_BaseMap")) screenMat.SetTexture("_BaseMap", t);
        if (screenMat.HasProperty("_MainTex")) screenMat.SetTexture("_MainTex", t);
        // make the screen glow a bit like a real TV
        if (glow > 0f && screenMat.HasProperty("_EmissionMap"))
        {
            screenMat.SetTexture("_EmissionMap", t);
            screenMat.SetColor("_EmissionColor", Color.white * glow);
            screenMat.EnableKeyword("_EMISSION");
            screenMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    static void SetColor(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }
    static void SetFloat(Material m, string prop, float f) { if (m.HasProperty(prop)) m.SetFloat(prop, f); }

    void Update()
    {
        if (!started || vp == null) return;
        if (GameHud.MenuOpen) return;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame) PlayNext();
#else
        if (Input.GetKeyDown(KeyCode.N)) PlayNext();
#endif
    }

    void OnDestroy()
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (screenMat != null) Destroy(screenMat);
        if (quad != null) Destroy(quad);
    }
}
