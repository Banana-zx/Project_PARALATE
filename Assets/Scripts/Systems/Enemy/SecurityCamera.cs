using UnityEngine;

/// <summary>
/// ศัตรูชนิดใหม่: กล้องวงจรปิด หันกวาดซ้าย-ขวา ถ้าเห็นผู้เล่นนานพอจะเรียกศัตรูมาที่ตำแหน่งผู้เล่น
///
/// การทำงาน (State Machine 3 สถานะ):
///   Sweep  : หมุนไปหาปลายซ้าย/ขวาด้วยความเร็ว Turn Speed
///   Hold   : ถึงปลายแล้วค้าง Hold Time วินาที แล้วสลับไปอีกฝั่ง
///   Track  : เห็นผู้เล่น → หยุดกวาด หันตาม และนับเวลา Detect Time ครบแล้ว Alert
/// การ Alert ใช้ระบบเดียวกับการเรียกกำลังเสริมของศัตรู: RoomZone.BroadcastAlert ถ้าอยู่ในห้อง
/// ไม่งั้นแจ้ง AlertState ทุกตัวในรัศมี Alert Radius (AlertState.ReceiveReinforcement)
///
/// วิธีใช้: ลาก Prefab SecurityCamera ไปติดผนัง/เพดาน ให้แกน Z (ลูกศรสีน้ำเงิน) ชี้ไปทางกึ่งกลางที่ต้องการให้มอง
/// </summary>
public class SecurityCamera : MonoBehaviour
{
    private enum State { Sweep, Hold, Track }

    [Header("References")]
    [Tooltip("ส่วนที่หมุน (ว่าง = ตัววัตถุนี้เอง)")]
    public Transform head;

    [Header("Sweep (หันซ้าย-ขวา)")]
    [Tooltip("องศาที่หันไปแต่ละฝั่งจากทิศตั้งต้น")]
    [Range(0f, 170f)] public float sweepAngle = 50f;
    [Tooltip("ความเร็วการหัน (องศา/วินาที)")]
    public float turnSpeed = 25f;
    [Tooltip("เวลาที่ค้างตอนหันสุดซ้าย/ขวา (วินาที)")]
    public float holdTime = 1.5f;

    [Header("Vision")]
    public float viewRadius = 12f;
    [Range(1f, 180f)] public float viewAngle = 50f;
    [Tooltip("สิ่งที่บังสายตา (ค่าเริ่มต้น = Layer Obstacle)")]
    public LayerMask obstructionMask;
    public string playerTag = "Player";

    [Header("Alert")]
    [Tooltip("ต้องเห็นผู้เล่นต่อเนื่องกี่วินาทีจึงจะ Alert")]
    public float detectTime = 1.2f;
    [Tooltip("รัศมีเรียกศัตรู (ใช้เมื่อกล้อง/ผู้เล่นไม่ได้อยู่ใน RoomZone)")]
    public float alertRadius = 25f;
    [Tooltip("หลัง Alert แล้วต้องรอกี่วินาทีจึง Alert ซ้ำได้")]
    public float alertCooldown = 6f;
    public AudioClip alertSound;

    [Header("Vision Cone Visual")]
    public bool showVisionCone = true;
    public Color searchingColor = new Color(0.2f, 0.6f, 1f, 0.22f);
    public Color detectingColor = new Color(1f, 0.85f, 0.1f, 0.3f);
    public Color alertColor = new Color(1f, 0f, 0f, 0.38f);
    [Range(3, 60)] public int coneSegments = 24;

    private State state = State.Sweep;
    private Quaternion baseLocalRotation;
    private float currentYaw;
    private int direction = 1;
    private float holdTimer;
    private float detectTimer;
    private float nextAlertTime;
    private float alertFlashUntil;
    private float nextVisionCheck;
    private bool seesPlayer;
    private Transform player;

    private Transform coneTransform;
    private Mesh coneMesh;
    private Material coneMaterial;
    private Vector3[] coneVertices;
    private float groundY;

    public bool SeesPlayer => seesPlayer;

    private void Reset()
    {
        obstructionMask = LayerMask.GetMask("Obstacle");
    }

    private void Awake()
    {
        if (head == null)
            head = transform;

        if (obstructionMask.value == 0)
            obstructionMask = LayerMask.GetMask("Obstacle");

        baseLocalRotation = head.localRotation;
    }

    private void Start()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObject != null)
            player = playerObject.transform;

        // สุ่มจุดเริ่ม ไม่ให้กล้องหลายตัวหันพร้อมกันเป๊ะ
        currentYaw = Random.Range(-sweepAngle, sweepAngle);
        direction = Random.value < 0.5f ? -1 : 1;

        // ระดับพื้นใต้กล้อง: ใช้วาดกรวยสายตาบนพื้น
        groundY = Physics.Raycast(head.position, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y + 0.06f
            : head.position.y - 3f;

        CreateCone();
    }

    private void Update()
    {
        if (Time.time >= nextVisionCheck)
        {
            nextVisionCheck = Time.time + 0.1f;
            seesPlayer = CheckVision();
        }

        if (seesPlayer)
            UpdateTrack();
        else
            UpdateSweep();

        head.localRotation = baseLocalRotation * Quaternion.Euler(0f, currentYaw, 0f);
    }

    private void LateUpdate()
    {
        UpdateCone();
    }

    private void UpdateSweep()
    {
        if (state == State.Track)
        {
            // คลาดสายตา: กลับไปกวาดต่อ ค่อยๆ ลืม
            state = State.Sweep;
        }

        detectTimer = Mathf.Max(0f, detectTimer - Time.deltaTime * 0.5f);

        if (state == State.Hold)
        {
            holdTimer -= Time.deltaTime;
            if (holdTimer <= 0f)
            {
                direction = -direction;
                state = State.Sweep;
            }

            return;
        }

        float target = sweepAngle * direction;
        currentYaw = Mathf.MoveTowards(currentYaw, target, turnSpeed * Time.deltaTime);

        if (Mathf.Approximately(currentYaw, target))
        {
            state = State.Hold;
            holdTimer = holdTime;
            SfxLibrary.Play("camera_servo", head.position, 0.5f);
        }
    }

    private void UpdateTrack()
    {
        if (state != State.Track)
        {
            state = State.Track;
            SfxLibrary.Play("camera_detect", head.position);
        }

        // หันตามผู้เล่น (ภายในช่วงที่กล้องหมุนได้)
        Vector3 local = transform.InverseTransformPoint(player.position);
        Quaternion baseWorld = (head.parent != null ? head.parent.rotation : Quaternion.identity) * baseLocalRotation;
        Vector3 toPlayer = Quaternion.Inverse(baseWorld) * (player.position - head.position);
        float targetYaw = Mathf.Clamp(Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg, -sweepAngle, sweepAngle);
        currentYaw = Mathf.MoveTowards(currentYaw, targetYaw, turnSpeed * 2f * Time.deltaTime);

        detectTimer += Time.deltaTime;

        if (detectTimer >= detectTime && Time.time >= nextAlertTime)
            Alert();
    }

    private bool CheckVision()
    {
        if (player == null || PlayerStealth.IsInvisible)
            return false;

        Vector3 origin = head.position;
        Vector3 target = player.position + Vector3.up; // กลางลำตัว
        Vector3 flat = target - origin;
        flat.y = 0f;

        if (flat.sqrMagnitude > viewRadius * viewRadius)
            return false;

        Vector3 forward = head.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f || Vector3.Angle(forward, flat) > viewAngle * 0.5f)
            return false;

        return !Physics.Linecast(origin, target, obstructionMask, QueryTriggerInteraction.Ignore);
    }

    private void Alert()
    {
        nextAlertTime = Time.time + alertCooldown;
        alertFlashUntil = Time.time + 1.5f;
        detectTimer = 0f;

        Vector3 playerPosition = player.position;

        if (alertSound != null)
            AudioSource.PlayClipAtPoint(alertSound, head.position);
        else
            SfxLibrary.Play("camera_alert", head.position);

        // ศัตรูในห้องเดียวกับผู้เล่น (หรือห้องของกล้อง) ถ้ามี RoomZone
        RoomZone zone = RoomZone.GetAt(playerPosition) ?? RoomZone.GetAt(head.position);
        if (zone != null)
        {
            zone.BroadcastAlert(null, playerPosition);
            return;
        }

        AlertState[] enemies = FindObjectsByType<AlertState>(FindObjectsSortMode.None);
        float sqrRadius = alertRadius * alertRadius;

        for (int i = 0; i < enemies.Length; i++)
        {
            if ((enemies[i].transform.position - head.position).sqrMagnitude <= sqrRadius)
                enemies[i].ReceiveReinforcement(playerPosition);
        }
    }

    // ------------------------------------------------------------ กรวยสายตาบนพื้น (แนวเดียวกับ FieldOfView)

    private void CreateCone()
    {
        GameObject go = new GameObject("CameraVisionCone");
        go.transform.SetParent(transform, false);
        coneTransform = go.transform;

        coneMesh = new Mesh { name = "Security Camera Cone" };
        go.AddComponent<MeshFilter>().mesh = coneMesh;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("HDRP/Unlit");

        coneMaterial = new Material(shader) { renderQueue = 3000 };
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.material = coneMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        int segments = Mathf.Max(3, coneSegments);
        coneVertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        coneMesh.vertices = coneVertices;
        coneMesh.triangles = triangles;
    }

    private void UpdateCone()
    {
        if (coneTransform == null)
            return;

        if (coneTransform.gameObject.activeSelf != showVisionCone)
            coneTransform.gameObject.SetActive(showVisionCone);

        if (!showVisionCone)
            return;

        Vector3 forward = head.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return;

        // วางกรวยบนพื้นใต้กล้อง หมุนตามทิศที่กล้องหัน (เฉพาะแกน Y)
        Vector3 origin = new Vector3(head.position.x, groundY, head.position.z);
        coneTransform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward.normalized, Vector3.up));

        int segments = coneVertices.Length - 2;
        float half = viewAngle * 0.5f;
        Vector3 rayOrigin = origin + Vector3.up * 0.5f;

        coneVertices[0] = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-half, half, i / (float)segments) * Mathf.Deg2Rad;
            Vector3 localDirection = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 worldDirection = coneTransform.TransformDirection(localDirection);

            float distance = viewRadius;
            if (Physics.Raycast(rayOrigin, worldDirection, out RaycastHit hit, viewRadius, obstructionMask, QueryTriggerInteraction.Ignore))
                distance = hit.distance;

            coneVertices[i + 1] = localDirection * distance;
        }

        coneMesh.vertices = coneVertices;
        coneMesh.RecalculateBounds();

        Color color = Time.time < alertFlashUntil ? alertColor
            : seesPlayer ? Color.Lerp(detectingColor, alertColor, Mathf.Clamp01(detectTimer / Mathf.Max(0.01f, detectTime)))
            : searchingColor;

        if (coneMaterial.HasProperty("_Color")) coneMaterial.SetColor("_Color", color);
        if (coneMaterial.HasProperty("_UnlitColor")) coneMaterial.SetColor("_UnlitColor", color);
        if (coneMaterial.HasProperty("_BaseColor")) coneMaterial.SetColor("_BaseColor", color);
    }

    private void OnDestroy()
    {
        if (coneMesh != null) Destroy(coneMesh);
        if (coneMaterial != null) Destroy(coneMaterial);
    }

    private void OnDrawGizmosSelected()
    {
        Transform pivot = head != null ? head : transform;
        Quaternion baseRotation = Application.isPlaying && pivot.parent != null
            ? pivot.parent.rotation * baseLocalRotation
            : Application.isPlaying ? baseLocalRotation : pivot.rotation;

        Vector3 forward = baseRotation * Vector3.forward;
        forward.y = 0f;
        forward.Normalize();

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(pivot.position, Quaternion.Euler(0f, -sweepAngle, 0f) * forward * viewRadius);
        Gizmos.DrawRay(pivot.position, Quaternion.Euler(0f, sweepAngle, 0f) * forward * viewRadius);
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.6f);
        Gizmos.DrawWireSphere(pivot.position, alertRadius);
    }
}
