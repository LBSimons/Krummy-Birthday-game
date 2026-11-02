using UnityEngine;
public class RoomBuilder : MonoBehaviour
{
    public Vector3 size = new Vector3(12f, 6f, 12f);
    public float thickness = 1f;
    public Material wallMaterial;
    public bool showCeiling = true;
    const string Prefix = "RoomWall_";

    void Awake() { Build(); }

    [ContextMenu("Rebuild Room")]
    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (!child.name.StartsWith(Prefix)) continue;
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }

        float w = size.x, h = size.y, d = size.z, t = thickness;

        Slab("Floor",   new Vector3(0, -t / 2, 0),          new Vector3(w + 2 * t, t, d + 2 * t), true);
        Slab("Ceiling", new Vector3(0, h + t / 2, 0),       new Vector3(w + 2 * t, t, d + 2 * t), showCeiling);
        Slab("WallL",   new Vector3(-(w + t) / 2, h / 2, 0), new Vector3(t, h, d + 2 * t), true);
        Slab("WallR",   new Vector3((w + t) / 2, h / 2, 0),  new Vector3(t, h, d + 2 * t), true);
        Slab("WallB",   new Vector3(0, h / 2, -(d + t) / 2), new Vector3(w, h, t), true);
        Slab("WallF",   new Vector3(0, h / 2, (d + t) / 2),  new Vector3(w, h, t), true);
    }

    void Slab(string label, Vector3 localCenter, Vector3 scale, bool visible)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = Prefix + label;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localCenter;
        go.transform.localScale = scale;

        var r = go.GetComponent<Renderer>();
        if (wallMaterial != null) r.sharedMaterial = wallMaterial;
        r.enabled = visible;
    }
}
