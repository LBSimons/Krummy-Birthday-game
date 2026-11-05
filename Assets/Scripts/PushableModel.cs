using System;
using UnityEngine;

/// <summary>
/// Makes the big model a physics object: stinkies knock it around the room.
/// StinkyThrower adds this to the big model automatically, so you don't have to.
/// </summary>
public class PushableModel : MonoBehaviour
{
    [Header("Physics")]
    [Tooltip("Heavier = harder to knock around. A stinky weighs 1.")]
    public float mass = 4f;
    public float linearDamping = 0.9f;
    public float angularDamping = 2f;
    [Tooltip("1 = normal physics. Higher = every hit shoves it extra hard.")]
    public float knockbackMultiplier = 1.5f;
    [Tooltip("Tick to stop it tipping over (it can still spin around).")]
    public bool keepUpright = false;

    [Header("Sound (the plush makes the noise, not the stinkies)")]
    public bool makeSounds = true;
    [Tooltip("Hits slower than this are silent.")]
    public float soundMinSpeed = 1.5f;
    [Tooltip("Impact speed that plays at full volume.")]
    public float soundFullSpeed = 9f;
    [Tooltip("Minimum seconds between two sounds.")]
    public float soundCooldown = 0.25f;

    [Header("Anti-stuck (corners)")]
    [Tooltip("When the plush is further than this from where it started (metres, sideways), each hit also kicks it back towards the middle of the room.")]
    public float cornerRadius = 1.8f;
    [Tooltip("Strength of that kick (speed in m/s given to the plush).")]
    public float cornerKick = 4f;
    [Tooltip("Upward part of the kick, so it hops out of the corner.")]
    public float cornerLift = 1.5f;
    [Tooltip("Extra: also kick it if it was hit but is barely moving this long afterwards (seconds). 0 = off.")]
    public float stuckCheckDelay = 0.6f;

    [Header("Safety")]
    [Tooltip("If it ever falls out of the world, put it back where it started.")]
    public float respawnBelowY = -30f;

    public Rigidbody Body { get; private set; }

    /// <summary>Raised whenever a stinky hits the model.</summary>
    public event Action<Collision> HitByStinky;

    float lastSoundTime = -10f;
    Vector3 startPos;
    Quaternion startRot;

    void Awake()
    {
        startPos = transform.position;
        startRot = transform.rotation;

        // Moving objects need convex colliders.
        var cols = GetComponentsInChildren<Collider>();
        if (cols.Length == 0)
        {
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null)
                    mf.gameObject.AddComponent<MeshCollider>().convex = true;
        }
        else
        {
            foreach (var c in cols)
                if (c is MeshCollider mc) mc.convex = true;
        }

        Body = GetComponent<Rigidbody>();
        if (Body == null) Body = gameObject.AddComponent<Rigidbody>();
        Body.isKinematic = false;
        Body.useGravity = true;
        Body.mass = mass;
        Body.linearDamping = linearDamping;
        Body.angularDamping = angularDamping;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        if (keepUpright)
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    float lastHitTime = -100f, stillTimer;

    /// <summary>Puts the plush back where it started, standing still.</summary>
    public void ResetToStart()
    {
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.position = startPos;
        Body.rotation = startRot;
        transform.SetPositionAndRotation(startPos, startRot);
        lastHitTime = -100f;
        stillTimer = 0f;
    }

    /// <summary>Stands the plush upright again (lifted a little so she can't stay wedged).</summary>
    public void StandUp()
    {
        Vector3 pos = Body.worldCenterOfMass;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;

        // If she is far off to one side, also slide her a bit back towards the middle.
        Vector3 toCentre = startPos - transform.position; toCentre.y = 0f;
        Vector3 newPos = transform.position + Vector3.up * 0.6f;
        if (toCentre.magnitude > 1.2f) newPos += toCentre.normalized * Mathf.Min(0.8f, toCentre.magnitude * 0.3f);

        Body.position = newPos;
        Body.rotation = startRot;
        transform.SetPositionAndRotation(newPos, startRot);
        Body.WakeUp();
        lastHitTime = -100f;
        stillTimer = 0f;
    }

    void FixedUpdate()
    {
        // Watchdog: for a few seconds after any hit, if the plush stops moving away from the
        // middle of the room, pop it back out. Works even if the hit callbacks are missed.
        if (Time.time - lastHitTime < 6f)
        {
            bool still = Body.linearVelocity.magnitude < 0.6f;
            stillTimer = still ? stillTimer + Time.fixedDeltaTime : 0f;
            if (stillTimer > 0.4f) { Unstick(true); stillTimer = 0f; }
        }

        if (transform.position.y < respawnBelowY)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = startPos;
            Body.rotation = startRot;
        }
    }

    void CheckStuck()
    {
        // Hit a moment ago and still hardly moving while far from the middle = wedged.
        if (Body.linearVelocity.magnitude < 0.8f) Unstick(true);
    }

    void Unstick(bool force)
    {
        Vector3 flat = startPos - Body.worldCenterOfMass; flat.y = 0f;
        float dist = flat.magnitude;
        if (dist < (force ? cornerRadius * 0.6f : cornerRadius)) return;   // only when really off to the side                       // already in the middle
        Vector3 kick = flat.normalized * cornerKick + Vector3.up * cornerLift;
        Body.AddForce(kick, ForceMode.VelocityChange);
        
    }

    void PlaySound(Collision c)
    {
        if (!makeSounds || SoundBank.Instance == null) return;
        float speed = c.relativeVelocity.magnitude;
        if (speed < soundMinSpeed || Time.time - lastSoundTime < soundCooldown) return;
        lastSoundTime = Time.time;
        float loud = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(speed / soundFullSpeed));
        SoundBank.Instance.PlayRandom(Body != null ? Body.worldCenterOfMass : transform.position, loud);
    }

    void OnCollisionEnter(Collision c)
    {
        PlaySound(c);

        if (c.collider.GetComponentInParent<FishProjectile>() == null) return;

        // Extra shove on top of the normal physics.
        if (knockbackMultiplier > 1f && c.contactCount > 0)
        {
            float stinkyMass = c.rigidbody != null ? c.rigidbody.mass : 1f;
            Vector3 push = c.GetContact(0).normal;   // points from the stinky towards the model
            Body.AddForceAtPosition(push * (c.relativeVelocity.magnitude * stinkyMass * (knockbackMultiplier - 1f)),
                                    c.GetContact(0).point, ForceMode.Impulse);
        }

        lastHitTime = Time.time;   // (no instant kick any more: it would fight the plush flying out of the room)
        if (stuckCheckDelay > 0f) { CancelInvoke(nameof(CheckStuck)); Invoke(nameof(CheckStuck), stuckCheckDelay); }

        HitByStinky?.Invoke(c);
    }
}
