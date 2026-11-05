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
    enum State { Aiming, Following, Returning, FreeCam }

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
    [Tooltip("Each stinky disappears this many seconds after being thrown. 0 = never.")]
    public float stinkyLifetime = 8f;

    [Header("Free cam")]
    public float freeCamSpeed = 3f;
    public float freeCamFastMultiplier = 3f;
    public float freeCamLookSpeed = 0.15f;

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

    /// <summary>Set by GameFlow while the title card / win screen is up.</summary>
    public static bool Paused;

    State state = State.Aiming;
    Transform camT;
    Vector3 homePos, initHomePos;
    float initYaw, initPitch;
    float baseYaw, basePitch, yawOffset, pitchOffset;

    float freeYaw, freePitch;
    public bool IsFreeCam => state == State.FreeCam;

    Rigidbody followRb;
    bool followingModel;
    Vector3 followDir = Vector3.forward;
    Vector3 camVel;
    float followTime, settleTimer, returnTimer, nextThrowTime;

    GameObject uiRoot;
    PushableModel pushable;
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
        initHomePos = homePos; initYaw = baseYaw; initPitch = basePitch;

        if (FindFirstObjectByType<GameFlow>() == null) new GameObject("Game Flow").AddComponent<GameFlow>();

        SetupBigModel();
        BuildUi();
        if (!Paused) LockCursor(true);
    }

    /// <summary>Back to the starting view (used by the win screen's play-again).</summary>
    public void ResetView()
    {
        homePos = initHomePos; baseYaw = initYaw; basePitch = initPitch;
        yawOffset = pitchOffset = 0f;
        followRb = null; followingModel = false;
        state = State.Aiming;
        SetUi(true);
        if (camT != null) camT.SetPositionAndRotation(homePos, AimRotation);
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void SetupBigModel()
    {
        // The slot may hold the prefab from the Project window instead of the model in the scene.
        // If so, find the live one in the scene by name.
        if (bigModel == null || !bigModel.gameObject.scene.IsValid())
        {
            var go = GameObject.Find(bigModel != null ? bigModel.name : "Scuffroll");
            bigModel = go != null ? go.transform : null;
        }
        if (bigModel == null)
        {
            Debug.LogWarning("StinkyThrower: couldn't find the big model in the scene. Drag it into the Big Model slot from the Hierarchy.");
            return;
        }

        // Make it a physics object so stinkies can knock it around.
        pushable = bigModel.GetComponent<PushableModel>();
        if (pushable == null) pushable = bigModel.gameObject.AddComponent<PushableModel>();
        pushable.HitByStinky += OnModelHit;
        FishProjectile.Hit += OnStinkyHit;
        Debug.Log("StinkyThrower: following target ready -> " + bigModel.name);
    }

    // Backup check from the stinky's side, in case the model's own callback is missed.
    void OnStinkyHit(FishProjectile fp, Collision c)
    {
        if (pushable == null || pushable.Body == null) return;
        if (c.rigidbody == pushable.Body || c.collider.GetComponentInParent<PushableModel>() != null)
            OnModelHit(c);
    }

    void OnDestroy()
    {
        if (pushable != null) pushable.HitByStinky -= OnModelHit;
        FishProjectile.Hit -= OnStinkyHit;
    }

    // When a stinky hits the big model, the camera follows the model until it settles down.
    void OnModelHit(Collision c)
    {
        BouncyRoom.Pulse();
        if (pushable == null || pushable.Body == null) return;
        if (state == State.FreeCam) return;   // player is looking around freely

        if (state == State.Following && followingModel)
        {
            settleTimer = 0f;                                   // hit again: keep following
            followTime = Mathf.Min(followTime, maxFollowTime * 0.5f);
            return;
        }
        // Stinky reached the plush: switch the camera from the stinky to the plush.
        Vector3 toModel = pushable.Body.worldCenterOfMass - camT.position;
        if (state == State.Following) { followDir = new Vector3(toModel.x, 0f, toModel.z).normalized; }
        BeginFollow(pushable.Body, toModel, true, state == State.Following);
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
        if (Paused || GameHud.MenuOpen) return;   // title card, win screen or settings menu is showing

        if (state == State.FreeCam) { FreeCamUpdate(); return; }

        bool click = ClickPressed();

        if (click && Cursor.lockState == CursorLockMode.None) LockCursor(true);
        if (click && GameHud.PointerOverUi) click = false;   // that click was on a button

        if (state == State.Aiming)
        {
            Look();
            if (click && Time.time >= nextThrowTime) Throw();
        }
        else if (state == State.Following && click)
        {
            StartReturn();      // click while chasing = skip back to first person
        }
    }

    public void ToggleFreeCam()
    {
        if (state == State.FreeCam)
        {
            StartReturn();                     // fly back to the player's spot
            return;
        }
        followRb = null; followingModel = false;
        Vector3 e = camT.eulerAngles;
        freeYaw = e.y;
        freePitch = e.x > 180f ? e.x - 360f : e.x;
        state = State.FreeCam;
        SetUi(false);
    }

    void FreeCamUpdate()
    {
        float dt = Time.unscaledDeltaTime;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current; var k = Keyboard.current;
        if (m != null && m.rightButton.isPressed)
        {
            Vector2 d = m.delta.ReadValue() * freeCamLookSpeed;
            freeYaw += d.x; freePitch = Mathf.Clamp(freePitch - d.y, -89f, 89f);
        }
        Vector3 mv = Vector3.zero; bool fast = false;
        if (k != null)
        {
            if (k.wKey.isPressed) mv += Vector3.forward;
            if (k.sKey.isPressed) mv += Vector3.back;
            if (k.dKey.isPressed) mv += Vector3.right;
            if (k.aKey.isPressed) mv += Vector3.left;
            if (k.eKey.isPressed) mv += Vector3.up;
            if (k.qKey.isPressed) mv += Vector3.down;
            fast = k.leftShiftKey.isPressed;
        }
#else
        if (Input.GetMouseButton(1))
        {
            freeYaw += Input.GetAxis("Mouse X") * 3f; freePitch = Mathf.Clamp(freePitch - Input.GetAxis("Mouse Y") * 3f, -89f, 89f);
        }
        Vector3 mv = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) mv += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) mv += Vector3.back;
        if (Input.GetKey(KeyCode.D)) mv += Vector3.right;
        if (Input.GetKey(KeyCode.A)) mv += Vector3.left;
        if (Input.GetKey(KeyCode.E)) mv += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) mv += Vector3.down;
        bool fast = Input.GetKey(KeyCode.LeftShift);
#endif
        Quaternion rot = Quaternion.Euler(freePitch, freeYaw, 0f);
        float speed = freeCamSpeed * (fast ? freeCamFastMultiplier : 1f);
        Vector3 world = rot * new Vector3(mv.x, 0f, mv.z) + Vector3.up * mv.y;
        camT.SetPositionAndRotation(camT.position + world * speed * dt, rot);
    }

    // The pointer is always live: where the mouse is on the screen decides where you aim,
    // so you never have to click first to "get" the pointer.
    void Look()
    {
        Vector2 p = ReadMousePosition();
        if (Screen.width <= 0 || Screen.height <= 0) return;
        float nx = Mathf.Clamp01(p.x / Screen.width) * 2f - 1f;    // -1 .. 1
        float ny = Mathf.Clamp01(p.y / Screen.height) * 2f - 1f;
        float targetYaw = nx * maxYaw;
        float targetPitch = (invertY ? ny : -ny) * maxPitch;
        float k = 1f - Mathf.Exp(-20f * Time.deltaTime);          // tiny smoothing
        yawOffset = Mathf.Lerp(yawOffset, targetYaw, k);
        pitchOffset = Mathf.Lerp(pitchOffset, targetPitch, k);
    }

    static Vector2 ReadMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
        return Input.mousePosition;
#endif
    }

    static void LockCursor(bool on)
    {
        Cursor.lockState = on ? CursorLockMode.Confined : CursorLockMode.None;
        Cursor.visible = !on ? true : false;
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
        var proj = go.GetComponent<FishProjectile>();
        if (proj == null) proj = go.AddComponent<FishProjectile>();
        proj.Setup();   // makes sure there is a Rigidbody and that every collider is bouncy

        var rb = go.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;   // smooth for the chase camera
        rb.linearVelocity = dir * throwSpeed;
        rb.AddTorque(Random.insideUnitSphere * spinStrength, ForceMode.VelocityChange);

        if (stinkyLifetime > 0f) Destroy(go, stinkyLifetime);

        // Camera chases the stinky first; it switches to the plush when the stinky hits it.
        if (state == State.Aiming)
            BeginFollow(rb, dir, false, false);

        live.Enqueue(go);
        while (live.Count > 0 && live.Peek() == null) live.Dequeue();   // forget ones that already expired
        while (live.Count > maxStinkies)
        {
            var oldest = live.Dequeue();
            if (oldest != null && oldest != go) Destroy(oldest);
        }

        BouncyRoom.Pulse();   // room gets bouncy for a few seconds so the plush can't get stuck

        nextThrowTime = Time.time + throwCooldown;
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

    void BeginFollow(Rigidbody rb, Vector3 throwDir, bool isModel, bool keepDir)
    {
        followRb = rb;
        followingModel = isModel;
        if (!keepDir)
        {
            followDir = new Vector3(throwDir.x, 0f, throwDir.z);
            if (followDir.sqrMagnitude < 0.001f) followDir = Vector3.forward;
            followDir.Normalize();
            camVel = Vector3.zero;
        }
        followTime = 0f;
        settleTimer = 0f;
        state = State.Following;
        SetUi(false);
    }

    void StartReturn()
    {
        // The plush has settled: the player's camera moves to where the plush is
        // (the spot the chase camera is at) and looks at it.
        if (followingModel && followRb != null)
        {
            Vector3 look = followRb.worldCenterOfMass - camT.position;
            if (look.sqrMagnitude > 0.01f)
            {
                Vector3 e = Quaternion.LookRotation(look.normalized).eulerAngles;
                homePos = camT.position;
                baseYaw = e.y;
                basePitch = e.x > 180f ? e.x - 360f : e.x;
                yawOffset = pitchOffset = 0f;
            }
        }
        followingModel = false;
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
            case State.FreeCam:   break;
        }
    }

    void FollowCamera()
    {
        if (followRb == null) { StartReturn(); return; }

        float dt = Time.deltaTime;
        Vector3 vel = followRb.linearVelocity;
        followTime += dt;
        bool slow = vel.magnitude < settleSpeed && followRb.angularVelocity.magnitude < settleSpeed;
        settleTimer = (followTime > 0.8f && slow) ? settleTimer + dt : 0f;
        if (settleTimer >= settleTime || followTime >= maxFollowTime) { StartReturn(); return; }

        // Slowly turn the "behind the stinky" direction towards where it is heading.
        Vector3 flat = new Vector3(vel.x, 0f, vel.z);
        if (flat.magnitude > 2f)
        {
            Vector3 next = Vector3.Slerp(followDir, flat.normalized, 1f - Mathf.Exp(-directionSmoothing * dt));
            if (next.sqrMagnitude > 0.001f) followDir = next.normalized;
        }

        Vector3 focus = followRb.worldCenterOfMass;
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
                if (hitBuffer[i].collider.attachedRigidbody == followRb) continue;   // the model itself
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
