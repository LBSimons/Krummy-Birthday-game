using System.Collections.Generic;
using UnityEngine;
public class FishThrower : MonoBehaviour
{
    public GameObject fishPrefab;
    public Transform target;
    public Transform throwPoint;

    public float minSpeed = 8f;
    public float maxSpeed = 14f;
    [Range(0f, 45f)] public float spreadDegrees = 8f;
    public float spinStrength = 6f;
    public int maxFish = 150;

    readonly Queue<GameObject> live = new Queue<GameObject>();

    void Start()
    {
        if (target != null && target.GetComponentInChildren<Collider>() == null)
        {
            foreach (var mf in target.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>();
        }
    }

    public void ThrowFish()
    {
        if (fishPrefab == null)
        {
            Debug.LogWarning("FishThrower: assign a Fish Prefab.");
            return;
        }

        Transform origin = throwPoint != null ? throwPoint
                         : Camera.main != null ? Camera.main.transform
                         : transform;

        Vector3 aimPoint = target != null ? target.position : origin.position + origin.forward * 5f;
        Vector3 dir = (aimPoint - origin.position).normalized;

        // Random aim error.
        dir = Quaternion.AngleAxis(Random.Range(-spreadDegrees, spreadDegrees), Vector3.up)
            * Quaternion.AngleAxis(Random.Range(-spreadDegrees, spreadDegrees), origin.right)
            * dir;

        Vector3 pos = origin.position + dir * 0.6f;
        GameObject fish = Instantiate(fishPrefab, pos, Random.rotation);

        // Make sure the fish can collide and make noise even if the prefab is bare.
        if (fish.GetComponentInChildren<Collider>() == null)
        {
            Debug.LogWarning("FishThrower: fish prefab has no collider, adding a BoxCollider.");
            fish.AddComponent<BoxCollider>();
        }
        if (fish.GetComponent<FishProjectile>() == null)
            fish.AddComponent<FishProjectile>(); // also adds the Rigidbody

        var rb = fish.GetComponent<Rigidbody>();
        rb.AddForce(dir * Random.Range(minSpeed, maxSpeed), ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * spinStrength, ForceMode.VelocityChange);

        live.Enqueue(fish);
        while (live.Count > maxFish)
        {
            var oldest = live.Dequeue();
            if (oldest != null) Destroy(oldest);
        }
    }

    public void ClearFish()
    {
        while (live.Count > 0)
        {
            var f = live.Dequeue();
            if (f != null) Destroy(f);
        }
    }
}
