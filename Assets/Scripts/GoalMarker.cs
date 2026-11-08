using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A giant red circle with red arrows around each window of the room, so players see that the goal
/// is to get the krum plush out. Created by GameFlow; drawn in code with UI images in the 3D world.
/// </summary>
public class GoalMarker : MonoBehaviour
{
    [Header("Which objects are the exits")]
    public string[] windowNames = { "window" };

    [Header("Look")]
    public Color color = new Color(0.95f, 0.05f, 0.05f, 1f);
    [Tooltip("Ring diameter as a multiple of the window size.")]
    public float ringScale = 1.4f;
    public float minRingDiameter = 2.2f;
    public float maxRingDiameter = 3.4f;
    [Tooltip("How far in front of the window (metres) the marker floats, so curtains don't hide it.")]
    public float distanceFromWindow = 0.5f;
    public float arrowSize = 1.0f;
    public string label = "GET KRUMMY OUT!";

    const float PxPerMetre = 200f;

    class Marker
    {
        public Transform root;
        public RectTransform ring;
        public List<(RectTransform rt, Vector2 dir, float baseDist)> arrows = new List<(RectTransform, Vector2, float)>();
    }

    readonly List<Marker> markers = new List<Marker>();
    Sprite ringSprite, arrowSprite;
    bool built, visible;

    public void SetVisible(bool on)
    {
        visible = on;
        if (on && !built) Build();
        foreach (var m in markers) if (m.root != null) m.root.gameObject.SetActive(on);
    }

    // ------------------------------------------------------------ build

    void Build()
    {
        built = true;
        var room = GameObject.Find("room");
        if (room == null) { Debug.LogWarning("GoalMarker: no 'room' object found."); return; }

        // middle of the room, so we know which way is "inside"
        bool first = true; Bounds roomB = new Bounds();
        foreach (var r in room.GetComponentsInChildren<Renderer>())
        { if (first) { roomB = r.bounds; first = false; } else roomB.Encapsulate(r.bounds); }

        ringSprite = MakeRingSprite(512, 0.075f);
        arrowSprite = MakeArrowSprite(256);

        foreach (var name in windowNames)
        {
            Transform win = null;
            foreach (var t in room.GetComponentsInChildren<Transform>(true))
                if (t.name == name) { win = t; break; }
            if (win == null) continue;

            var rend = win.GetComponent<Renderer>();
            Bounds wb;
            if (rend != null) wb = rend.bounds;
            else
            {
                bool f2 = true; wb = new Bounds(win.position, Vector3.one);
                foreach (var r in win.GetComponentsInChildren<Renderer>())
                { if (f2) { wb = r.bounds; f2 = false; } else wb.Encapsulate(r.bounds); }
            }
            BuildOne(wb, roomB);
        }
        Debug.Log("GoalMarker: marked " + markers.Count + " window(s).");
        foreach (var m in markers) m.root.gameObject.SetActive(visible);
    }

    void BuildOne(Bounds win, Bounds roomB)
    {
        // which wall is it on? the wall is on the side of the window that is furthest from the room centre
        Vector3 toRoom = roomB.center - win.center; toRoom.y = 0f;
        Vector3 normal = Mathf.Abs(toRoom.x) > Mathf.Abs(toRoom.z)
            ? new Vector3(Mathf.Sign(toRoom.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(toRoom.z));
        Vector3 tangent = Vector3.Cross(Vector3.up, normal);                       // along the wall
        float width = Mathf.Abs(Vector3.Dot(win.size, new Vector3(Mathf.Abs(tangent.x), 0f, Mathf.Abs(tangent.z))));
        float height = win.size.y;
        float diameter = Mathf.Clamp(Mathf.Max(width, height) * ringScale, minRingDiameter, maxRingDiameter);

        var m = new Marker();
        var go = new GameObject("Goal Marker", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        // a world canvas is seen from its back (-Z side), so its forward points out through the window
        go.transform.position = win.center + normal * distanceFromWindow;
        go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
        go.transform.localScale = Vector3.one / PxPerMetre;
        var canvas = go.AddComponent<Canvas>();     // this swaps Transform for RectTransform, so grab it afterwards
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        m.root = rt;
        rt.sizeDelta = new Vector2(1000f, 1000f);

        // the ring
        float dpx = diameter * PxPerMetre;
        m.ring = NewImage(go.transform, "Ring", ringSprite, new Vector2(dpx, dpx));

        // arrows around the ring, all pointing at its middle
        float a = arrowSize * PxPerMetre;
        float r0 = dpx * 0.5f + a * 0.65f;
        Vector2[] dirs = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };   // positions around the ring
        foreach (var d in dirs)
        {
            var arrow = NewImage(go.transform, "Arrow", arrowSprite, new Vector2(a * 0.7f, a));
            // the sprite points "up"; turn it to point towards the middle (-d)
            Vector2 pointDir = -d;
            float ang = Mathf.Atan2(pointDir.y, pointDir.x) * Mathf.Rad2Deg - 90f;
            arrow.localRotation = Quaternion.Euler(0f, 0f, ang);
            arrow.anchoredPosition = d * r0;
            m.arrows.Add((arrow, d, r0));
        }

        // label above the ring
        var tgo = new GameObject("Label", typeof(RectTransform));
        tgo.transform.SetParent(go.transform, false);
        var txt = tgo.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text = label; txt.fontSize = 70; txt.fontStyle = FontStyle.Bold;
        txt.color = color; txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow; txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;
        var ol = tgo.AddComponent<Outline>(); ol.effectColor = new Color(0f, 0f, 0f, 0.9f); ol.effectDistance = new Vector2(5f, -5f);
        var trt = txt.rectTransform;
        trt.anchoredPosition = new Vector2(0f, dpx * 0.5f + a * 1.35f);
        trt.sizeDelta = new Vector2(1200f, 120f);

        markers.Add(m);
    }

    RectTransform NewImage(Transform parent, string name, Sprite sprite, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite; img.color = Color.white; img.raycastTarget = false;
        var r = img.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size;
        return r;
    }

    // ------------------------------------------------------------ animation

    void Update()
    {
        if (!visible) return;
        float t = Time.unscaledTime;
        float pulse = 1f + 0.04f * Mathf.Sin(t * 3f);
        foreach (var m in markers)
        {
            if (m.ring != null) m.ring.localScale = Vector3.one * pulse;
            foreach (var (rt, dir, baseDist) in m.arrows)
                rt.anchoredPosition = dir * (baseDist - (0.5f + 0.5f * Mathf.Sin(t * 4f)) * 0.22f * PxPerMetre);   // arrows nod towards the window
        }
    }

    // ------------------------------------------------------------ pictures drawn in code

    Sprite MakeRingSprite(int size, float thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float outer = 0.5f, inner = 0.5f - thickness;
        float edge = 1.5f / size;
        Color fill = color, border = new Color(0.35f, 0f, 0f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / size;
                float aOuter = Mathf.Clamp01((outer - d) / edge);
                float aInner = Mathf.Clamp01((d - inner) / edge);
                float alpha = Mathf.Min(aOuter, aInner);
                // thin dark outline just inside both edges
                float rim = Mathf.Min(outer - d, d - inner);
                Color col = rim < 0.006f ? border : fill;
                px[y * size + x] = new Color(col.r, col.g, col.b, alpha);
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // An arrow pointing UP: triangle head on top, rectangular shaft below.
    Sprite MakeArrowSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        Color fill = color, border = new Color(0.35f, 0f, 0f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;      // -1..1
                float v = (y + 0.5f) / size;                // 0 (bottom) .. 1 (top)
                float inside = Inside(u, v, 0f);
                float inner = Inside(u, v, 0.045f);
                Color col = inner > 0.5f ? fill : border;
                px[y * size + x] = new Color(col.r, col.g, col.b, inside > 0.5f ? 1f : 0f);
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // 1 when (u,v) is inside the arrow shrunk by 'grow' (negative growth = outline band)
    static float Inside(float u, float v, float shrink)
    {
        // head: v from 0.5 to 1.0, width shrinks to a point at the top
        bool head = v >= 0.5f + shrink && v <= 1f - shrink * 1.6f && Mathf.Abs(u) <= (1f - v) / 0.5f * 0.98f - shrink * 1.5f;
        // shaft: v from 0.02 to 0.55, half-width 0.32
        bool shaft = v >= 0.02f + shrink && v <= 0.56f && Mathf.Abs(u) <= 0.32f - shrink * 1.5f;
        return head || shaft ? 1f : 0f;
    }
}
