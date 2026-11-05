using UnityEngine;
using PhysMat = UnityEngine.PhysicsMaterial;
using PhysCombine = UnityEngine.PhysicsMaterialCombine;

/// <summary>
/// Goes on each thrown stinky. Gives it a Rigidbody (if the prefab doesn't have one), makes it
/// bouncy, and plays a random sound from the SoundBank whenever it hits something.
/// </summary>
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

    [Header("Cleanup / quiet")]
    [Tooltip("A stinky only makes noise for this many seconds after it is thrown.")]
    public float soundWindow = 5f;
    [Tooltip("Once a stinky has been lying still for this long it is removed (0 = never).")]
    public float restDestroyTime = 1.5f;

    // Hits slower than this are ignored for sound (new names so old saved values don't override).
    const float QuietSpeed = 2.5f;

    static PhysMat sharedMat;
    float spawnTime, restTimer;
    Rigidbody body;

    void Awake()
    {
        spawnTime = Time.time;
        Setup();
    }

    /// <summary>
    /// Safe to call more than once. Makes sure there is a Rigidbody and that every collider
    /// (including ones added after this script woke up) uses the bouncy material.
    /// </summary>
    public void Setup()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        body = rb;

        // Fast stinkies shouldn't tunnel through walls.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (sharedMat == null)
        {
            sharedMat = new PhysMat("FishBounce")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                // Maximum = it bounces whatever surface it hits.
                bounceCombine = PhysCombine.Maximum,
                frictionCombine = PhysCombine.Minimum
            };
        }

        foreach (var col in GetComponentsInChildren<Collider>())
            col.sharedMaterial = sharedMat;
    }

    void Update()
    {
        // Safety net if one ever escapes the room.
        if (transform.position.y < -100f) Destroy(gameObject);

        // Remove stinkies that have stopped moving, so nothing lies around making noise.
        if (restDestroyTime > 0f && body != null && Time.time - spawnTime > 1f)
        {
            if (body.linearVelocity.sqrMagnitude < 0.04f) restTimer += Time.deltaTime; else restTimer = 0f;
            if (restTimer > restDestroyTime) Destroy(gameObject);
        }
    }

    /// <summary>Raised on every hit; StinkyThrower uses it to know when the plush was hit.</summary>
    public static event System.Action<FishProjectile, Collision> Hit;

    // Stinkies are silent now: the krum plush makes the noise (see PushableModel).
    void OnCollisionEnter(Collision c)
    {
        Hit?.Invoke(this, c);
    }
}
