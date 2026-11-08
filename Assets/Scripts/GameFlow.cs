using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Title card + win screen. Created automatically by StinkyThrower, nothing to set up.
/// You win when the krum plush gets out of the room.
/// </summary>
public class GameFlow : MonoBehaviour
{
    [Header("Text")]
    public string title = "KRUMMY'S BIRTHDAY GAME";
    public string subtitle = "Throw stinkies at the krum plush.\nKnock her out of the room to win!";
    public string startPrompt = "Click to start";
    public string winTitle = "YOU WIN!";
    public string winSubtitle = "The krum plush escaped the room!";
    public string restartPrompt = "Click to play again";

    [Header("Your own art (optional, Texture Type = Sprite)")]
    [Tooltip("Full-screen image for the title card. Empty = built-in text card.")]
    public Sprite titleImage;
    [Tooltip("Full-screen image for the win screen. Empty = built-in text card.")]
    public Sprite winImage;

    [Header("Intro video (fade to black -> video -> title card)")]
    [Tooltip("Leave empty to use the file Assets/Resources/IntroVideo.mp4 (just replace that file with the real video). If neither exists, the intro is skipped.")]
    public VideoClip introVideo;
    public float fadeToBlackTime = 1.0f;
    public float fadeToTitleTime = 0.8f;
    [Tooltip("Click to skip the video.")]
    public bool clickSkipsVideo = true;

    [Header("Winning")]
    [Tooltip("The plush must be this many metres outside the room's edge to count as escaped.")]
    public float escapeMargin = 0.4f;
    [Tooltip("Seconds to watch her fly away before the win screen appears.")]
    public float winDelay = 1.5f;
    [Tooltip("Only objects smaller than this (diagonal, metres) can be opened as exits, so walls and floor always stay solid.")]
    public float maxExitSize = 4f;
    [Tooltip("Names containing these words get their colliders switched off, so the doors and windows are real exits.")]
    public string[] openingNames = { "window", "door", "glass" };

    [Header("Ending (fade to black -> video -> fast credits -> game closes)")]
    [Tooltip("Leave empty to use Assets/Resources/EndingVideo.mp4. If that is missing too, the intro video is used as a placeholder.")]
    public VideoClip endingVideo;
    [Tooltip("Credits scroll speed in pixels per second (1080p layout). Higher = faster.")]
    public float creditsSpeed = 320f;
    [Tooltip("Lines starting with # are big headings. Empty line = gap.")]
    public string[] credits =
    {
        "# KRUMMY ESCAPES", "a birthday game", "",
        "# Starring", "Scuffroll the Krum plush", "",
        "# Throwing arm", "You", "",
        "# Intro video", "Made by a very good friend", "",
        "# Special thanks", "Everyone who threw a stinky", "The window", "",
        "# Happy birthday!"
    };

    enum Phase { Intro, Title, Playing, Won }
    Phase phase;
    GameObject titleRoot, winRoot;
    Bounds roomBounds;
    bool haveBounds;
    PushableModel plush;
    float escapedAt = -1f;
    int graceFrames;
    CanvasGroup blackGroup, videoGroup, titleGroup;
    RawImage videoImage;
    AspectRatioFitter videoFit;
    bool skipRequested;
    bool endingStarted;
    CanvasGroup creditsGroup;
    RectTransform creditsText;
    Text theEndText;
    GameHud hud;
    TvScreen tvScreen;
    GoalMarker goal;
    MusicPlayer music;

    void Awake()
    {
        BuildBounds();
        OpenExits();
        BuildScreens();
        hud = gameObject.AddComponent<GameHud>();
        tvScreen = gameObject.AddComponent<TvScreen>();
        goal = gameObject.AddComponent<GoalMarker>();
        music = gameObject.AddComponent<MusicPlayer>();

        StinkyThrower.Paused = true;
        if (introVideo == null) introVideo = Resources.Load<VideoClip>("IntroVideo");
        if (introVideo != null)
        {
            phase = Phase.Intro;
            ShowCursor(false);
            StartCoroutine(IntroRoutine());
        }
        else
        {
            phase = Phase.Title;
            titleRoot.SetActive(true);
            ShowCursor(true);
        }
    }

    void Start()
    {
        // No physics while the intro / title card is up (nothing can fall or wander off).
        plush = FindFirstObjectByType<PushableModel>();
        if (plush != null && plush.Body != null && phase != Phase.Playing)
        {
            plush.Body.linearVelocity = Vector3.zero;
            plush.Body.angularVelocity = Vector3.zero;
            plush.Body.isKinematic = true;
        }
    }

    void OnDestroy() { StinkyThrower.Paused = false; }

    void ReleasePlush()
    {
        if (plush == null) plush = FindFirstObjectByType<PushableModel>();
        if (plush != null && plush.Body != null) plush.Body.isKinematic = false;
    }

    // ------------------------------------------------------------ room edges

    void BuildBounds()
    {
        var room = GameObject.Find("room");
        if (room == null) { Debug.LogWarning("GameFlow: no object called 'room' found, so winning is switched off."); return; }

        bool first = true;
        foreach (var r in room.GetComponentsInChildren<Renderer>())
        {
            if (first) { roomBounds = r.bounds; first = false; }
            else roomBounds.Encapsulate(r.bounds);
        }
        haveBounds = !first;
        if (haveBounds) Debug.Log("GameFlow: room edges = " + roomBounds);
    }

    // Doors and windows become real openings the plush can fly through.
    void OpenExits()
    {
        var room = GameObject.Find("room");
        if (room == null || openingNames == null) return;
        int active = 0;
        foreach (var c in room.GetComponentsInChildren<Collider>())
        {
            // Walls, floor and ceiling are big; a door or window is small. Never open the big ones.
            if (c.bounds.size.magnitude > maxExitSize) { if (c.enabled) active++; continue; }
            string n = c.gameObject.name.ToLowerInvariant();
            foreach (var key in openingNames)
                if (!string.IsNullOrEmpty(key) && n.Contains(key.ToLowerInvariant()))
                {
                    c.enabled = false;
                    Debug.Log("GameFlow: opened exit -> " + c.gameObject.name);
                    break;
                }
            if (c.enabled) active++;
        }
        Debug.Log("GameFlow: room has " + active + " solid colliders");
    }

    // ------------------------------------------------------------ loop

    void Update()
    {
        switch (phase)
        {
            case Phase.Intro:
                if (clickSkipsVideo && ClickPressed()) skipRequested = true;
                break;

            case Phase.Title:
                if (ClickPressed()) { StartGame(); }
                break;

            case Phase.Playing:
                if (graceFrames > 0) { graceFrames--; if (graceFrames == 0) StinkyThrower.Paused = false; }
                CheckEscape();
                break;

            case Phase.Won:
                if (!endingStarted && Time.time - escapedAt > winDelay)
                {
                    endingStarted = true;
                    StartCoroutine(EndingRoutine());
                }
                break;
        }
    }

    void StartGame()
    {
        titleRoot.SetActive(false);
        ReleasePlush();
        hud.SetVisible(true);
        tvScreen.Begin();                // the TV switches on when the game starts
        goal.SetVisible(true);           // red circle + arrows on the window
        music.Play();
        phase = Phase.Playing;
        graceFrames = 2;                 // lets this click pass before throwing is allowed
        ShowCursor(false);
    }

    void CheckEscape()
    {
        if (!haveBounds) return;
        if (plush == null) plush = FindFirstObjectByType<PushableModel>();
        if (plush == null || plush.Body == null) return;

        Vector3 p = plush.Body.worldCenterOfMass;
        float m = escapeMargin;
        bool outside = p.x < roomBounds.min.x - m || p.x > roomBounds.max.x + m ||
                       p.z < roomBounds.min.z - m || p.z > roomBounds.max.z + m ||
                       p.y > roomBounds.max.y + m;
        if (!outside) return;

        phase = Phase.Won;
        music.Stop();
        goal.SetVisible(false);
        hud.SetVisible(false);
        escapedAt = Time.time;
        StinkyThrower.Paused = true;     // no more throwing; the camera keeps watching her go
    }

    // Put everything back the way it started (no scene reload needed).
    void Restart()
    {
        foreach (var fp in FindObjectsByType<FishProjectile>(FindObjectsSortMode.None))
            Destroy(fp.gameObject);

        ReleasePlush();
        if (plush != null) plush.ResetToStart();
        var thrower = FindFirstObjectByType<StinkyThrower>();
        if (thrower != null) thrower.ResetView();

        winRoot.SetActive(false);
        endingStarted = false;
        hud.SetVisible(true);
        goal.SetVisible(true);
        music.Play();
        phase = Phase.Playing;
        graceFrames = 2;                 // this click must not also throw a stinky
        ShowCursor(false);
    }

    // ------------------------------------------------------------ ending

    IEnumerator EndingRoutine()
    {
        ShowCursor(false);
        yield return Fade(blackGroup, 0f, 1f, fadeToBlackTime);

        var clip = endingVideo != null ? endingVideo : Resources.Load<VideoClip>("EndingVideo");
        if (clip == null) clip = introVideo;           // placeholder until the real ending video exists
        if (clip != null) yield return PlayClip(clip);

        // credits
        videoGroup.gameObject.SetActive(false);
        creditsGroup.gameObject.SetActive(true);
        creditsGroup.alpha = 1f;
        theEndText.gameObject.SetActive(false);
        blackGroup.alpha = 0f;
        music.Play();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(creditsText);
        float h = creditsText.rect.height;
        float y = -1080f;
        while (y < h)
        {
            y += creditsSpeed * Time.unscaledDeltaTime;
            creditsText.anchoredPosition = new Vector2(0f, y);
            yield return null;
        }

        theEndText.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(3f);
        music.Stop();
        yield return Fade(blackGroup, 0f, 1f, 1.2f);
        creditsGroup.gameObject.SetActive(false);
        Debug.Log("GameFlow: the end. Closing the game.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Plays a clip full screen on the video screen. Click skips it.
    IEnumerator PlayClip(VideoClip clip)
    {
        skipRequested = false;
        var vp = gameObject.AddComponent<VideoPlayer>();
        var rt = new RenderTexture(Mathf.Max(16, (int)clip.width), Mathf.Max(16, (int)clip.height), 0);
        vp.playOnAwake = false; vp.isLooping = false; vp.clip = clip;
        vp.renderMode = VideoRenderMode.RenderTexture; vp.targetTexture = rt;
        vp.audioOutputMode = VideoAudioOutputMode.Direct;
        bool ended = false, failed = false;
        vp.loopPointReached += _ => ended = true;
        vp.errorReceived += (_, msg) => { Debug.LogWarning("GameFlow: ending video problem: " + msg); if (!vp.isPlaying) failed = true; };
        vp.Prepare();

        float wait = 0f;
        while (!vp.isPrepared && !failed && wait < 8f) { wait += Time.unscaledDeltaTime; yield return null; }

        if (vp.isPrepared && !failed)
        {
            videoImage.texture = rt;
            videoFit.aspectRatio = (float)clip.width / Mathf.Max(1f, (float)clip.height);
            videoGroup.alpha = 1f;
            videoGroup.gameObject.SetActive(true);
            blackGroup.alpha = 0f;
            vp.Play();
            float t = 0f, stoppedFor = 0f;
            yield return null;
            while (!failed && t < 1800f)
            {
                t += Time.unscaledDeltaTime;
                if (t > 1f && ClickPressed()) break;
                if (t > 1f)
                {
                    stoppedFor = vp.isPlaying ? 0f : stoppedFor + Time.unscaledDeltaTime;
                    if (ended || stoppedFor > 0.75f) break;
                }
                yield return null;
            }
        }
        vp.Stop();
        videoGroup.alpha = 0f;
        Destroy(vp);
        rt.Release();
        Destroy(rt);
    }

    // ------------------------------------------------------------ intro video

    IEnumerator IntroRoutine()
    {
        // 1) fade the game to black
        yield return Fade(blackGroup, 0f, 1f, fadeToBlackTime);

        // 2) get the video ready
        var vp = gameObject.AddComponent<VideoPlayer>();
        var rt = new RenderTexture(Mathf.Max(16, (int)introVideo.width), Mathf.Max(16, (int)introVideo.height), 0);
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.clip = introVideo;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = rt;
        vp.audioOutputMode = VideoAudioOutputMode.Direct;

        bool ended = false, failed = false;
        vp.loopPointReached += _ => ended = true;
        vp.errorReceived += (_, msg) => { Debug.LogWarning("GameFlow: intro video problem: " + msg); if (!vp.isPlaying) failed = true; };
        vp.Prepare();

        float wait = 0f;
        while (!vp.isPrepared && !failed && wait < 8f && !skipRequested) { wait += Time.unscaledDeltaTime; yield return null; }

        // 3) play it
        if (vp.isPrepared && !failed && !skipRequested)
        {
            videoImage.texture = rt;
            videoFit.aspectRatio = (float)introVideo.width / Mathf.Max(1f, (float)introVideo.height);
            videoGroup.alpha = 1f;
            videoGroup.gameObject.SetActive(true);
            blackGroup.alpha = 0f;           // the video screen is black around the picture anyway
            vp.Play();

            // Don't trust the clip's reported length (Unity can report it wrongly): just play until the
            // player says it is finished, or it has really stopped.
            float t = 0f, stoppedFor = 0f;
            const float hardLimit = 600f;
            yield return null;
            while (!failed && !skipRequested && t < hardLimit)
            {
                t += Time.unscaledDeltaTime;
                if (t > 1f)
                {
                    stoppedFor = vp.isPlaying ? 0f : stoppedFor + Time.unscaledDeltaTime;
                    if (ended || stoppedFor > 0.75f) break;
                }
                yield return null;
            }
            Debug.Log("GameFlow: intro video finished after " + t.ToString("0.0") + "s (clip says " + introVideo.length.ToString("0.0") + "s, player says " + vp.length.ToString("0.0") + "s, ended event: " + ended + ", skipped: " + skipRequested + ")");
        }
        vp.Stop();

        // 4) reveal the title card
        titleRoot.SetActive(true);
        titleGroup.alpha = 1f;
        if (!videoGroup.gameObject.activeSelf) { videoGroup.gameObject.SetActive(true); videoGroup.alpha = 1f; }
        blackGroup.alpha = 0f;
        yield return Fade(videoGroup, 1f, 0f, fadeToTitleTime);
        videoGroup.gameObject.SetActive(false);

        Destroy(vp);
        rt.Release();
        Destroy(rt);

        phase = Phase.Title;
        ShowCursor(true);
    }

    static IEnumerator Fade(CanvasGroup g, float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.01f, seconds)));
            yield return null;
        }
        g.alpha = to;
    }

    // ------------------------------------------------------------ screens

    void BuildScreens()
    {
        var canvasGO = new GameObject("Screens Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        titleRoot = MakeScreen(canvasGO.transform, "Title Card", titleImage,
                               title, subtitle, startPrompt, new Color(0.04f, 0.10f, 0.16f, 0.92f), new Color(0.55f, 0.85f, 1f));
        winRoot = MakeScreen(canvasGO.transform, "Win Screen", winImage,
                             winTitle, winSubtitle, restartPrompt, new Color(0.10f, 0.05f, 0.14f, 0.88f), new Color(1f, 0.85f, 0.4f));
        titleRoot.SetActive(false);
        winRoot.SetActive(false);
        titleGroup = titleRoot.AddComponent<CanvasGroup>();

        // Video screen: black with the picture fitted inside (drawn above the title card).
        var vRoot = new GameObject("Intro Video Screen", typeof(RectTransform));
        vRoot.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)vRoot.transform);
        var vBg = vRoot.AddComponent<Image>(); vBg.color = Color.black; vBg.raycastTarget = false;
        videoGroup = vRoot.AddComponent<CanvasGroup>();
        videoGroup.blocksRaycasts = false;
        var picGO = new GameObject("Picture", typeof(RectTransform));
        picGO.transform.SetParent(vRoot.transform, false);
        Stretch((RectTransform)picGO.transform);
        videoImage = picGO.AddComponent<RawImage>(); videoImage.raycastTarget = false;
        videoFit = picGO.AddComponent<AspectRatioFitter>();
        videoFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        videoFit.aspectRatio = 16f / 9f;
        vRoot.SetActive(false);

        // Credits screen (black, text scrolls up).
        var cRoot = new GameObject("Credits Screen", typeof(RectTransform));
        cRoot.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)cRoot.transform);
        var cBg = cRoot.AddComponent<Image>(); cBg.color = Color.black; cBg.raycastTarget = false;
        creditsGroup = cRoot.AddComponent<CanvasGroup>(); creditsGroup.blocksRaycasts = false;
        var ctGO = new GameObject("Credits Text", typeof(RectTransform));
        ctGO.transform.SetParent(cRoot.transform, false);
        var ct = ctGO.AddComponent<Text>();
        ct.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ct.supportRichText = true; ct.alignment = TextAnchor.UpperCenter; ct.color = Color.white;
        ct.fontSize = 56; ct.lineSpacing = 1.2f; ct.raycastTarget = false;
        var sb = new System.Text.StringBuilder();
        foreach (var line in credits)
        {
            if (line.StartsWith("#")) sb.Append("<size=88><b><color=#FFD966>").Append(line.TrimStart('#', ' ')).Append("</color></b></size>\n");
            else sb.Append(line).Append('\n');
        }
        ct.text = sb.ToString();
        creditsText = ct.rectTransform;
        creditsText.anchorMin = new Vector2(0.5f, 1f); creditsText.anchorMax = new Vector2(0.5f, 1f);
        creditsText.pivot = new Vector2(0.5f, 1f);
        creditsText.sizeDelta = new Vector2(1400f, 100f);
        var fit = ctGO.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var endGO = new GameObject("The End", typeof(RectTransform));
        endGO.transform.SetParent(cRoot.transform, false);
        theEndText = endGO.AddComponent<Text>();
        theEndText.font = ct.font; theEndText.text = "THE END"; theEndText.fontSize = 150; theEndText.fontStyle = FontStyle.Bold;
        theEndText.color = new Color(1f, 0.85f, 0.4f); theEndText.alignment = TextAnchor.MiddleCenter; theEndText.raycastTarget = false;
        Stretch(theEndText.rectTransform);
        endGO.SetActive(false);
        cRoot.SetActive(false);

        // Plain black sheet used for the fade-to-black (on top of everything).
        var bRoot = new GameObject("Fade Black", typeof(RectTransform));
        bRoot.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)bRoot.transform);
        var bImg = bRoot.AddComponent<Image>(); bImg.color = Color.black; bImg.raycastTarget = false;
        blackGroup = bRoot.AddComponent<CanvasGroup>();
        blackGroup.alpha = 0f;
        blackGroup.blocksRaycasts = false;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    static GameObject MakeScreen(Transform parent, string name, Sprite art, string big, string small, string prompt,
                                 Color bg, Color accent)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rt = (RectTransform)root.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var bgImg = root.AddComponent<Image>();
        bgImg.color = art != null ? Color.white : bg;
        bgImg.sprite = art;
        bgImg.raycastTarget = false;
        if (art != null) bgImg.preserveAspect = false;

        if (art == null)
        {
            MakeText(root.transform, big, 120, accent, new Vector2(0f, 150f), FontStyle.Bold);
            MakeText(root.transform, small, 46, Color.white, new Vector2(0f, -20f), FontStyle.Normal);
        }
        var p = MakeText(root.transform, prompt, 48, accent, new Vector2(0f, -300f), FontStyle.Italic);
        p.gameObject.AddComponent<Blink>();
        return root;
    }

    static Text MakeText(Transform parent, string text, int size, Color color, Vector2 pos, FontStyle style)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.8f);
        o.effectDistance = new Vector2(4f, -4f);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(1700f, 300f);
        return t;
    }

    class Blink : MonoBehaviour
    {
        Text t;
        void Awake() { t = GetComponent<Text>(); }
        void Update()
        {
            var c = t.color;
            c.a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            t.color = c;
        }
    }

    // ------------------------------------------------------------ input

    static void ShowCursor(bool on)
    {
        Cursor.visible = on;
        Cursor.lockState = CursorLockMode.None;
    }

    static bool ClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    static bool RPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }
}
