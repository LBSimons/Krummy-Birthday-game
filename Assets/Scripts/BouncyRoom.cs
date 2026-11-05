using UnityEngine;
using PhysMat = UnityEngine.PhysicsMaterial;
using PhysCombine = UnityEngine.PhysicsMaterialCombine;

/// <summary>
/// Makes the room's walls/floor/furniture bouncy for a few seconds after every throw (and every
/// hit on the plush), then eases back to normal. Stops the plush getting wedged in a corner.
/// Created automatically the first time a stinky is thrown.
/// </summary>
public class BouncyRoom : MonoBehaviour
{
    [Range(0f, 1f)] public float peakBounciness = 0.3f;
    [Tooltip("Seconds it stays at full bounce after a throw.")]
    public float holdTime = 0.5f;
    [Tooltip("Seconds it takes to calm down afterwards.")]
    public float fadeTime = 1.2f;

    static BouncyRoom instance;
    PhysMat mat;
    float pulseTime = -100f;

    public static void Pulse()
    {
        if (instance == null)
        {
            instance = FindFirstObjectByType<BouncyRoom>();
            if (instance == null) instance = new GameObject("Bouncy Room").AddComponent<BouncyRoom>();
        }
        instance.pulseTime = Time.time;
    }

    void Awake()
    {
        instance = this;
        mat = new PhysMat("BouncyRoom")
        {
            bounciness = 0f,
            dynamicFriction = 0.3f,
            staticFriction = 0.3f,
            bounceCombine = PhysCombine.Maximum,   // beats the plush's own material
            frictionCombine = PhysCombine.Minimum
        };

        // Every solid, non-moving collider = the room. Skip the plush and stinkies.
        foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c.isTrigger || c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<FishProjectile>() != null) continue;
            if (c.GetComponentInParent<PushableModel>() != null) continue;
            c.sharedMaterial = mat;
        }
    }

    void Update()
    {
        float t = Time.time - pulseTime - holdTime;
        float k = t <= 0f ? 1f : Mathf.Clamp01(1f - t / fadeTime);
        mat.bounciness = peakBounciness * k;
    }
}
