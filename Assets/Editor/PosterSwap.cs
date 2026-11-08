#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the pictures from Assets/Images onto the posters on the walls of the room.
/// Runs by itself whenever the set of pictures in Assets/Images changes (and via Tools > Swap Wall Posters).
/// Each poster gets the picture whose shape (wide / tall) fits it best, so nothing looks squashed.
/// </summary>
[InitializeOnLoad]
public static class PosterSwap
{
    const string ImagesFolder = "Assets/Images";
    const string PrefKey = "krum_poster_hash_v2";

    static PosterSwap() { EditorApplication.delayCall += () => Apply(false); }

    [MenuItem("Tools/Swap Wall Posters")]
    static void Menu() { Apply(true); }

    class Slot { public Material mat; public float aspect; public string owner; public Vector2 uvMin = Vector2.zero, uvMax = Vector2.one; }

    static void Apply(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

        // --- the pictures
        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { ImagesFolder })
                                 .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
        if (paths.Count == 0) return;
        var imgs = paths.Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p)).Where(t => t != null && t.height > 0).ToList();
        if (imgs.Count == 0) return;

        string hash = string.Join("|", paths.Select(p => p + ":" + new System.IO.FileInfo(p).Length));
        if (!force && EditorPrefs.GetString(PrefKey, "") == hash) return;

        // --- the posters in the open scene
        var room = GameObject.Find("room");
        if (room == null)
        {
            if (force) Debug.LogWarning("PosterSwap: open the 'krummy' scene first (couldn't find the 'room' object).");
            return;
        }

        var slots = new Dictionary<Material, Slot>();
        foreach (var r in room.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.name.ToLowerInvariant().StartsWith("poster")) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
            {
                var m = mats[i];
                if (m == null || slots.ContainsKey(m)) continue;
                var info = new Slot { mat = m, owner = r.name };
                SlotInfo(r.transform, mf.sharedMesh, i, info);
                slots[m] = info;
            }
        }
        if (slots.Count == 0) { if (force) Debug.LogWarning("PosterSwap: no posters found in the room."); return; }

        // --- hand out pictures: best shape match first, spread them out so all get used
        var uses = new int[imgs.Count];
        var order = slots.Values.OrderBy(s => s.mat.name).ToList();
        int n = 0;
        foreach (var s in order)
        {
            int best = 0; float bestCost = float.MaxValue;
            for (int k = 0; k < imgs.Count; k++)
            {
                float ia = (float)imgs[k].width / imgs[k].height;
                float cost = Mathf.Abs(Mathf.Log(Mathf.Max(0.05f, s.aspect) / ia)) + 0.35f * uses[k];
                if (cost < bestCost) { bestCost = cost; best = k; }
            }
            uses[best]++;
            Apply(s.mat, imgs[best], s.uvMin, s.uvMax);
            n++;
        }

        AssetDatabase.SaveAssets();
        EditorPrefs.SetString(PrefKey, hash);
        Debug.Log("PosterSwap: put " + imgs.Count + " pictures from Assets/Images onto " + n + " poster slots.");
    }

    static void Apply(Material m, Texture2D tex, Vector2 uvMin, Vector2 uvMax)
    {
        // Stretch the whole picture exactly over the poster's UV area, so nothing is cropped.
        Vector2 span = new Vector2(Mathf.Max(1e-4f, uvMax.x - uvMin.x), Mathf.Max(1e-4f, uvMax.y - uvMin.y));
        Vector2 scale = new Vector2(1f / span.x, 1f / span.y);
        Vector2 offset = new Vector2(-uvMin.x * scale.x, -uvMin.y * scale.y);
        tex.wrapMode = TextureWrapMode.Clamp;
        if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", scale); m.SetTextureOffset("_BaseMap", offset); }
        if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", scale); m.SetTextureOffset("_MainTex", offset); }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(m);
    }

    // Works out, for one poster slot: the UV rectangle it uses and its real-world width/height ratio.
    static void SlotInfo(Transform t, Mesh mesh, int sub, Slot info)
    {
        var v = mesh.vertices; var uv = mesh.uv; var tri = mesh.GetTriangles(sub);
        info.aspect = 1f;
        if (uv == null || uv.Length != v.Length || tri.Length < 3) return;

        Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
        foreach (int idx in tri)
        {
            mn = Vector2.Min(mn, uv[idx]); mx = Vector2.Max(mx, uv[idx]);
        }
        info.uvMin = mn; info.uvMax = mx;

        float bestArea = 0f;
        for (int i = 0; i < tri.Length; i += 3)
        {
            int ia = tri[i], ib = tri[i + 1], ic = tri[i + 2];
            Vector3 a = t.TransformPoint(v[ia]), b = t.TransformPoint(v[ib]), c = t.TransformPoint(v[ic]);
            Vector3 e1 = b - a, e2 = c - a;
            float area = Vector3.Cross(e1, e2).magnitude;
            Vector2 d1 = uv[ib] - uv[ia], d2 = uv[ic] - uv[ia];
            float det = d1.x * d2.y - d2.x * d1.y;
            if (area <= bestArea || Mathf.Abs(det) < 1e-9f) continue;
            Vector3 dPdu = (e1 * d2.y - e2 * d1.y) / det;
            Vector3 dPdv = (e2 * d1.x - e1 * d2.x) / det;
            float w = dPdu.magnitude * (mx.x - mn.x), h = dPdv.magnitude * (mx.y - mn.y);
            if (h < 1e-6f) continue;
            bestArea = area; info.aspect = w / h;
        }
    }
}
#endif
