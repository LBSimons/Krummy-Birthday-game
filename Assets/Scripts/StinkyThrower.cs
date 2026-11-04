using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First-person aiming + throwing.
///  - Move the mouse to aim, left-click to throw a stinky where the crosshair points.
///  - The camera then follows the stinky around the room, and glides back to your
///    first-person view once it settles (or when you click to skip).
///  - Shows your aim image (crosshair / scope / frame) while you are aiming.
/// Put this on an empty GameObject (e.g. "Throw UI") and fill in the slots.
/// </summary>
public class StinkyThrower : MonoBehaviour
{
    enum State { Aiming, Following, Returning }

    [Header("References")]
    [Tooltip("Prefab of the small stinky model that gets thrown.")]
    public GameObject stinkyPrefab;
    [Tooltip("The first-person camera. Leave empty to use the Main Camera.")]
    public Camera cam;
    [Tooltip("The big model in the middle (gets colliders automatically if it has none).")]
    public Transform bigModel;

    [Header("Aim UI")]
    [Tooltip("Your image. Its Texture Type must be 'Sprite (2D and UI)'. Empty = simple white dot.")]
    public Sprite uiSprite;
    [Tooltip("Size on screen in pixels (at 1920x1080). 0 = use the image's own size.")]
    public Vector2 uiSize = new Vector2(128f, 128f);
    [Tooltip("Tick this if your image is a full-screen overlay/frame instead of a crosshair.")]
    public bool stretchToFullScreen = false;
    public bool hideUiWhileFollowing = true;

    [Header("Aiming")]
    [Tooltip("Degrees per mouse pixel.")]
    public float lookSensitivity = 0.1f;
    public float maxYaw = 75f;
    public float maxPitch = 45f;
    public bool invertY = false;

    [Header("Throw")]
    public float throwSpeed = 16f;
    public float spinStrength = 8f;
    public float throwCooldown = 0.15f;
    [Tooltip("How far in front of the camera the stinky appears.")]
    public float spawnDistance = 0.7f;
    [Tooltip("Oldest stinkies are removed past this number.")]
    public int maxStinkies = 100;

    [Header("Chase camera")]
    public float followDistance = 3.5f;
    public float followHeight = 1.2f;
    [Tooltip("Higher = camera position catches up faster (lower SmoothDamp time).")]
    public float followPositionSmoothTime = 0.15f;
    public float followRotationSpeed = 8f;
    [Tooltip("How slowly the camera swings round when the stinky bounces back. Lower = calmer.")]
    public float directionSmoothing = 1.5f;
    [Tooltip("Go back to first person after the stinky moves slower than this...")]
    public float settleSpeed = 0.6f;
    [Tooltip("...for this many seconds.")]
    public float settleTime = 1.0f;
    [Tooltip("Always go back after this many seconds, even if it's still bouncing.")]
    public float maxFollowTime = 10f;
    public float returnSmoothTime = 0.35f;
    public float returnRotationSpeed = 6f;
    [Tooltip("Keeps the chase camera from poking through walls.")]
    public float cameraWallPadding = 0.3f;

    State state = State.Aiming;
    Transform camT;
    Vector3 homePos;
    float baseYaw, basePitch, yawOffset, pitchOffset;

    Rigidbody followRb;
    Vector3 followDir = Vector3.forward;
    Vector3 camVel;
    float followTime, settleTimer, returnTimer, nextThrowTime;

    GameObject uiRoot;
    readonly Queue<GameObject> live = new Queue<GameObject>();
    readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    Quaternion AimRotation => Quaternion.Euler(basePitch + pitchOffset, baseYaw + yawOffset, 0f);

    // ------------------------------------------------------------ setup

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("StinkyThrower: no camera found. Assign one or tag your camera MainCamera.");
            enabled = false;
            return;
        }
        if (stinkyPrefab == null)
            Debug.LogWarning("StinkyThrower: assign the Stinky Prefab.");

        camT = cam.transform;
        homePos = camT.position;
        Vector3 e = camT.eulerAngles;
        baseYaw = e.y;
        basePitch = e.x > 180f ? e.x - 360f : e.x;

        EnsureBigModelCollider();
        BuildUi();
        LockCursor(true);
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void EnsureBigModelCollider()
    {
        if (bigModel == null || bigModel.GetComponentInChildren<Collider>() != null) return;
        foreach (var mf in bigModel.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) mf.gameObject.AddComponent<MeshCollider>();
    }

    void BuildUi()
    {
        uiRoot = new GameObject("Aim UI");
        var canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var imgGO = new GameObject("Aim Image", typeof(RectTransform));
        imgGO.transform.SetParent(uiRoot.transform, false);
        var img = imgGO.AddComponent<Image>();
        img.raycastTarget = false;

        var rt = img.rectTransform;
        if (uiSprite != null)
        {
            img.sprite = uiSprite;
            if (stretchToFullScreen)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                if (uiSize.x > 0f && uiSize.y > 0f) rt.sizeDelta = uiSize;
                else img.SetNativeSize();
            }
        }
        else
        {
            img.sprite = MakeDotSprite();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(14f, 14f);
        }
    }

    static Sprite MakeDotSprite()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var c = new Vector2(n / 2f, n / 2f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(n / 2f - d)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    void SetUi(bool on)
    {
        if (uiRoot != null) uiRoot.SetActive(on || !hideUiWhileFollowing);
    }

    // ------------------------------------------------------------ input

    void Update()
    {
        bool click = ClickPressed();

        if (EscPressed()) LockCursor(false);
        else if (click && Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor(true);   // first click just grabs the mouse
            click = false;
        }
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        if (state == State.Aiming)
        {
            if (!locked) return;
            Look();
            if (click && Time.time >= nextThrowTime) Throw();
        }
        else if (state == State.Following && click)
        {
            StartReturn();      // click while chasing = skip back to first person
        }
    }

    void Look()
    {
        Vector2 d = ReadMouseDelta() * lookSensitivity;
        yawOffset = Mathf.Clamp(yawOffset + d.x, -maxYaw, maxYaw);
        pitchOffset = Mathf.Clamp(pitchOffset + (invertY ? d.y : -d.y), -maxPitch, maxPitch);
    }

    static void LockCursor(bool on)
    {
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    static bool ClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
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

    static Vector2 ReadMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
    }

    // ------------------------------------------------------------ throwing

    void Throw()
    {
        if (stinkyPrefab == null) return;

        Vector3 dir = camT.forward;
        Vector3 pos = camT.position + dir * spawnDistance;
        GameObject go = Instantiate(stinkyPrefab, pos, Random.rotation);

        PrepareStinky(go);
        if (go.GetComponent<FishProjectile>() == null)
            go.AddComponent<FishProjectile>();      // also adds the Rigidbody + bounce + hit sounds

        var rb = go.GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;   // smooth for the chase camera
        rb.linearVelocity = dir * throwSpeed;
        rb.AddTorque(Random.insideUnitSphere * spinStrength, ForceMode.VelocityChange);

        live.Enqueue(go);
        while (live.Count > maxStinkies)
        {
            var oldest = live.Dequeue();
            if (oldest != null && oldest != go) Destroy(oldest);
        }

        nextThrowTime = Time.time + throwCooldown;
        BeginFollow(rb, dir);
    }

    // Moving stinkies need convex colliders; add them if the prefab has none.
    static void PrepareStinky(GameObject go)
    {
        var cols = go.GetComponentsInChildren<Collider>();
        if (cols.Length == 0)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null)
                    mf.gameObject.AddComponent<MeshCollider>().convex = true;
        }
        else
        {
            foreach (var c in cols)
                if (c is MeshCollider mc) mc.convex = true;
        }

        if (go.GetComponentInChildren<Collider>() == null)
        {
            // Fallback (e.g. skinned mesh): a sphere around the visuals.
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Vector3 s = go.transform.lossyScale;
            var sc = go.AddComponent<SphereCollider>();
            sc.center = go.transform.InverseTransformPoint(b.center);
            sc.radius = b.extents.magnitude * 0.6f / Mathf.Max(s.x, s.y, s.z);
        }
    }

    // ------------------------------------------------------------ camera

    void BeginFollow(Rigidbody rb, Vector3 throwDir)
    {
        followRb = rb;
        followDir = new Vector3(throwDir.x, 0f, throwDir.z);
        if (followDir.sqrMagnitude < 0.001f) followDir = Vector3.forward;
        followDir.Normalize();
        followTime = 0f;
        settleTimer = 0f;
        camVel = Vector3.zero;
        state = State.Following;
        SetUi(false);
    }

    void StartReturn()
    {
        state = State.Returning;
        returnTimer = 0f;
        camVel = Vector3.zero;
    }

    void LateUpdate()
    {
        if (camT == null) return;
        switch (state)
        {
            case State.Aiming:    camT.SetPositionAndRotation(homePos, AimRotation); break;
            case State.Following: FollowCamera(); break;
            case State.Returning: ReturnCamera(); break;
        }
    }

    void FollowCamera()
    {
        if (followRb == null) { StartReturn(); return; }

        float dt = Time.deltaTime;
        Vector3 vel = followRb.linearVelocity;
        followTime += dt;
        settleTimer = vel.magnitude < settleSpeed ? settleTimer + dt : 0f;
        if (settleTimer >= settleTime || followTime >= maxFollowTime) { StartReturn(); return; }

        // Slowly turn the "behind the stinky" direction towards where it is heading.
        Vector3 flat = new Vector3(vel.x, 0f, vel.z);
        if (flat.magnitude > 2f)
        {
            Vector3 next = Vector3.Slerp(followDir, flat.normalized, 1f - Mathf.Exp(-directionSmoothing * dt));
            if (next.sqrMagnitude > 0.001f) followDir = next.normalized;
        }

        Vector3 focus = followRb.transform.position;
        Vector3 desired = focus - followDir * followDistance + Vector3.up * followHeight;

        // Pull the camera in front of any wall/object between it and the stinky.
        Vector3 toCam = desired - focus;
        float dist = toCam.magnitude;
        if (dist > 0.01f)
        {
            Vector3 rayDir = toCam / dist;
            int n = Physics.RaycastNonAlloc(focus, rayDir, hitBuffer, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = dist;
            for (int i = 0; i < n; i++)
            {
                if (hitBuffer[i].collider.GetComponentInParent<FishProjectile>() != null) continue;
                if (hitBuffer[i].distance < best) best = hitBuffer[i].distance;
            }
            if (best < dist) desired = focus + rayDir * Mathf.Max(best - cameraWallPadding, 0.4f);
        }

        camT.position = Vector3.SmoothDamp(camT.position, desired, ref camVel, followPositionSmoothTime);

        Vector3 look = focus - camT.position;
        if (look.sqrMagnitude > 0.0001f)
            camT.rotation = Quaternion.Slerp(camT.rotation, Quaternion.LookRotation(look.normalized),
                                             1f - Mathf.Exp(-followRotationSpeed * dt));
    }

    void ReturnCamera()
    {
        float dt = Time.deltaTime;
        returnTimer += dt;

        camT.position = Vector3.SmoothDamp(camT.position, homePos, ref camVel, returnSmoothTime);
        camT.rotation = Quaternion.Slerp(camT.rotation, AimRotation, 1f - Mathf.Exp(-returnRotationSpeed * dt));

        bool close = (camT.position - homePos).sqrMagnitude < 0.0025f &&
                     Quaternion.Angle(camT.rotation, AimRotation) < 1.5f;
        if (close || returnTimer > 2.5f)
        {
            camT.SetPositionAndRotation(homePos, AimRotation);
            state = State.Aiming;
            SetUi(true);
        }
    }
}
