using UnityEngine;
using PhysMat = UnityEngine.PhysicsMaterial;
using PhysCombine = UnityEngine.PhysicsMaterialCombine;

public class FishProjectile : MonoBehaviour
{
    [Header("Bounce")]
    [Range(0f, 1f)] public float bounciness = 0.85f;
    [Range(0f, 1f)] public float friction = 0.15f;

    [Header("Hit sound")]
    [Tooltip("Hits slower than this make no sound (stops buzzing when a fish is resting on the floor).")]
    public float minImpactSpeed = 0.75f;
    [Tooltip("Impact speed that plays at full volume. Slower hits are quieter.")]
    public float fullVolumeSpeed = 8f;
    [Tooltip("Minimum seconds between sounds from this one fish.")]
    public float soundCooldown = 0.05f;

    static PhysMat sharedMat;
    float lastSoundTime = -10f;

    void Awake()
    {
        // Fast fish shouldn't tunnel through walls.
        GetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (sharedMat == null)
        {
            sharedMat = new PhysMat("FishBounce")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                // Maximum = the fish bounces whatever surface it hits.
                bounceCombine = PhysCombine.Maximum,
                frictionCombine = PhysCombine.Minimum
            };
        }

        foreach (var col in GetComponentsInChildren<Collider>())
            col.sharedMaterial = sharedMat;
    }

    void Update()
    {
        // Safety net if a fish ever escapes the room.
        if (transform.position.y < -100f) Destroy(gameObject);
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (speed < minImpactSpeed) return;
        if (Time.time - lastSoundTime < soundCooldown) return;

        // Fish-vs-fish: both fish get this callback, so only the one with the
        // lower instance ID makes the noise (otherwise you'd hear it twice).
        var other = c.collider.GetComponentInParent<FishProjectile>();
        if (other != null && other.GetInstanceID() < GetInstanceID()) return;

        lastSoundTime = Time.time;

        Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        float loudness = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(speed / fullVolumeSpeed));

        if (SoundBank.Instance != null)
            SoundBank.Instance.PlayRandom(point, loudness);
    }
}
