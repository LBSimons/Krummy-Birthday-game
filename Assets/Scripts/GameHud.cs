using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// In-game buttons (stand her up, reset her, free cam, settings) + the pause / settings menu.
/// Created by GameFlow. Everything is drawn in code, nothing to set up.
/// </summary>
public class GameHud : MonoBehaviour
{
    class Btn
    {
        public RectTransform rt;
        public Image bg;
        public Text label;
        public Action onClick;
        public bool inMenu;
    }

    public static bool MenuOpen { get; private set; }
    static GameHud instance;

    /// <summary>True while the mouse is over one of our buttons (so a click there must not throw a stinky).</summary>
    public static bool PointerOverUi => instance != null && instance.OverAnyButton();

    readonly List<Btn> buttons = new List<Btn>();
    GameObject hudRoot, menuRoot;
    RectTransform sliderTrack, sliderFill;
    Text volumeLabel, fullscreenLabel, freeCamLabel, hintLabel;
    bool visible, dragging;
    float volume = 1f;
    PushableModel plush;
    StinkyThrower thrower;
    bool freeCamOn;

    static readonly Color Normal = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color Hover = new Color(0.15f, 0.35f, 0.5f, 0.85f);
    static readonly Color Accent = new Color(0.55f, 0.85f, 1f);

    // ------------------------------------------------------------ public

    public void SetVisible(bool on)
    {
        visible = on;
        if (hudRoot != null) hudRoot.SetActive(on);
        if (!on) CloseMenu();
    }

    void Awake()
    {
        instance = this;
        volume = PlayerPrefs.GetFloat("krum_volume", 1f);
        AudioListener.volume = volume;
        Build();
        SetVisible(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        MenuOpen = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    // ------------------------------------------------------------ actions

    void Find()
    {
        if (plush == null) plush = FindFirstObjectByType<PushableModel>();
        if (thrower == null) thrower = FindFirstObjectByType<StinkyThrower>();
    }

    void StandUp()
    {
        Find();
        if (plush != null) plush.StandUp();
    }

    void ResetHer()
    {
        Find();
        foreach (var fp in FindObjectsByType<FishProjectile>(FindObjectsSortMode.None)) Destroy(fp.gameObject);
        if (plush != null) plush.ResetToStart();
        if (thrower != null) { thrower.ResetView(); }
        freeCamOn = false;
        RefreshLabels();
    }

    void ToggleFreeCam()
    {
        Find();
        if (thrower == null) return;
        thrower.ToggleFreeCam();
        freeCamOn = thrower.IsFreeCam;
        RefreshLabels();
    }

    void OpenMenu()
    {
        MenuOpen = true;
        menuRoot.SetActive(true);
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Cursor.visible = true;
    }

    void CloseMenu()
    {
        if (!MenuOpen && (menuRoot == null || !menuRoot.activeSelf)) return;
        MenuOpen = false;
        dragging = false;
        if (menuRoot != null) menuRoot.SetActive(false);
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    void ToggleMenu() { if (MenuOpen) CloseMenu(); else OpenMenu(); }

    void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        RefreshLabels();
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void RefreshLabels()
    {
        if (freeCamLabel != null) freeCamLabel.text = freeCamOn ? "Free cam: ON  (F)" : "Free cam  (F)";
        if (volumeLabel != null) volumeLabel.text = "Volume  " + Mathf.RoundToInt(volume * 100f) + "%";
        if (fullscreenLabel != null) fullscreenLabel.text = Screen.fullScreen ? "Fullscreen: ON" : "Fullscreen: OFF";
        if (hintLabel != null)
            hintLabel.text = freeCamOn ? "Free cam: hold right mouse to look, WASD to move, Q/E down/up, Shift = fast" : "";
        if (sliderFill != null) sliderFill.anchorMax = new Vector2(volume, 1f);
    }

    // ------------------------------------------------------------ loop

    void Update()
    {
        if (!visible) return;
        Find();
        if (thrower != null) freeCamOn = thrower.IsFreeCam;

        if (EscPressed()) ToggleMenu();

        if (!MenuOpen)
        {
            if (KeyPressed(0)) StandUp();
            if (KeyPressed(1)) ResetHer();
            if (KeyPressed(2)) ToggleFreeCam();
        }

        Vector2 mp = MousePos();
        bool click = ClickPressed();

        // hover colours + click handling
        foreach (var b in buttons)
        {
            if (!b.rt.gameObject.activeInHierarchy || MenuOpen != b.inMenu) continue;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(b.rt, mp, null);
            b.bg.color = over ? Hover : Normal;
            if (over && click) b.onClick?.Invoke();
        }

        // volume slider
        if (MenuOpen && sliderTrack != null)
        {
            bool held = MouseHeld();
            if (click && RectTransformUtility.RectangleContainsScreenPoint(sliderTrack, mp, null)) dragging = true;
            if (!held) dragging = false;
            if (dragging)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(sliderTrack, mp, null, out var local);
                Rect r = sliderTrack.rect;
                volume = Mathf.Clamp01(Mathf.InverseLerp(r.xMin, r.xMax, local.x));
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat("krum_volume", volume);
                RefreshLabels();
            }
        }

        // cursor: visible over buttons, in the menu, or in free cam
        Cursor.visible = MenuOpen || freeCamOn || OverAnyButton();
    }

    bool OverAnyButton()
    {
        if (!visible) return false;
        Vector2 mp = MousePos();
        foreach (var b in buttons)
            if (b.rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(b.rt, mp, null))
                return true;
        return false;
    }

    // ------------------------------------------------------------ building the UI

    void Build()
    {
        var canvasGO = new GameObject("HUD Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // --- corner buttons
        hudRoot = new GameObject("HUD Buttons", typeof(RectTransform));
        hudRoot.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)hudRoot.transform);

        float y = -30f;
        AddCornerButton("Stand her up  (U)", ref y, StandUp);
        AddCornerButton("Reset her  (R)", ref y, ResetHer);
        var fc = AddCornerButton("Free cam  (F)", ref y, ToggleFreeCam);
        freeCamLabel = fc.label;
        AddCornerButton("Settings  (Esc)", ref y, ToggleMenu);

        hintLabel = MakeText(hudRoot.transform, "", 30, Color.white, FontStyle.Italic);
        var hrt = hintLabel.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 40f);
        hrt.sizeDelta = new Vector2(1600f, 60f);

        // --- settings menu
        menuRoot = new GameObject("Settings Menu", typeof(RectTransform));
        menuRoot.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)menuRoot.transform);
        var dim = menuRoot.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(menuRoot.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(760f, 700f);
        var pimg = panel.AddComponent<Image>();
        pimg.color = new Color(0.06f, 0.12f, 0.18f, 0.97f);

        var head = MakeText(panel.transform, "SETTINGS  (game paused)", 52, Accent, FontStyle.Bold);
        Place(head.rectTransform, 0f, 290f, 700f, 80f);

        volumeLabel = MakeText(panel.transform, "Volume", 40, Color.white, FontStyle.Normal);
        Place(volumeLabel.rectTransform, 0f, 190f, 700f, 60f);

        // slider
        var track = new GameObject("Slider", typeof(RectTransform));
        track.transform.SetParent(panel.transform, false);
        sliderTrack = (RectTransform)track.transform;
        Place(sliderTrack, 0f, 120f, 560f, 28f);
        var timg = track.AddComponent<Image>();
        timg.color = new Color(1f, 1f, 1f, 0.2f);
        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(track.transform, false);
        sliderFill = (RectTransform)fill.transform;
        sliderFill.anchorMin = Vector2.zero;
        sliderFill.anchorMax = new Vector2(volume, 1f);
        sliderFill.offsetMin = sliderFill.offsetMax = Vector2.zero;
        fill.AddComponent<Image>().color = Accent;

        AddMenuButton(panel.transform, "Fullscreen", 20f, ToggleFullscreen, out fullscreenLabel);
        AddMenuButton(panel.transform, "Resume", -80f, ToggleMenu, out _);
        AddMenuButton(panel.transform, "Reset everything", -180f, () => { ResetHer(); CloseMenu(); }, out _);
        AddMenuButton(panel.transform, "Quit game", -280f, QuitGame, out _);

        menuRoot.SetActive(false);
        RefreshLabels();
    }

    Btn AddCornerButton(string text, ref float y, Action act)
    {
        var b = NewButton(hudRoot.transform, text, 34, false);
        var r = b.rt;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 1f);
        r.anchoredPosition = new Vector2(-30f, y);
        r.sizeDelta = new Vector2(360f, 64f);
        b.onClick = act;
        y -= 76f;
        return b;
    }

    void AddMenuButton(Transform parent, string text, float y, Action act, out Text label)
    {
        var b = NewButton(parent, text, 38, true);
        var r = b.rt;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(520f, 76f);
        b.onClick = act;
        label = b.label;
    }

    Btn NewButton(Transform parent, string text, int size, bool menu)
    {
        var go = new GameObject("Button " + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var b = new Btn { rt = (RectTransform)go.transform, inMenu = menu };
        b.bg = go.AddComponent<Image>();
        b.bg.color = Normal;
        b.label = MakeText(go.transform, text, size, Color.white, FontStyle.Bold);
        Stretch(b.label.rectTransform);
        buttons.Add(b);
        return b;
    }

    static Text MakeText(Transform parent, string text, int size, Color color, FontStyle style)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    // ------------------------------------------------------------ input

    static Vector2 MousePos()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    static bool ClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    static bool MouseHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    static bool EscPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // 0 = U (stand up), 1 = R (reset), 2 = F (free cam)
    static bool KeyPressed(int which)
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return false;
        return which == 0 ? k.uKey.wasPressedThisFrame : which == 1 ? k.rKey.wasPressedThisFrame : k.fKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(which == 0 ? KeyCode.U : which == 1 ? KeyCode.R : KeyCode.F);
#endif
    }
}
