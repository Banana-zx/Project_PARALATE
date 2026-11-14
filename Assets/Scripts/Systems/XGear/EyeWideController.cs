using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Eye Wide: กด T เพื่อสลับเป็นมุมมองบุคคลที่หนึ่งสำหรับสำรวจแผนที่ (ยืนอยู่กับที่ หมุนมองด้วยเมาส์)
/// เลื่อนลูกกลิ้งเพื่อซูมเข้า/ออก (มีเสียงกล้องซูม) กด T หรือ Esc เพื่อออก
/// ใช้กล้อง Cinemachine ที่สร้างเองตอนรัน Priority 1900: สูงกว่ากล้องห้อง/ท่อลม แต่ต่ำกว่ากล้องเล็งปืน (2000)
/// </summary>
[DisallowMultipleComponent]
public class EyeWideController : MonoBehaviour
{
    [Header("Input")]
    public Key toggleKey = Key.T;

    [Header("Camera")]
    public Vector3 eyeOffset = new Vector3(0f, 1.55f, 0.15f);
    public int cameraPriority = 1900;
    public float mouseSensitivity = 0.12f;
    public float minPitch = -70f;
    public float maxPitch = 70f;

    [Header("Zoom")]
    public float defaultFov = 60f;
    public float minFov = 12f;
    public float maxFov = 75f;
    [Tooltip("องศา FOV ที่เปลี่ยนต่อการเลื่อนลูกกลิ้ง 1 คลิก")]
    public float zoomStep = 6f;
    public float zoomSmooth = 12f;

    private TopDownPlayerController controller;
    private PickupSystem pickup;
    private WeaponWheelUI wheel;
    private CinemachineCamera eyeCamera;
    private bool active;
    private float yaw;
    private float pitch;
    private float targetFov;

    public bool IsActive => active;

    private void Awake()
    {
        controller = GetComponent<TopDownPlayerController>();
        pickup = GetComponent<PickupSystem>();
        wheel = GetComponent<WeaponWheelUI>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (active)
        {
            if (keyboard[toggleKey].wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
            {
                Exit();
                return;
            }

            HandleLook();
            HandleZoom();
            return;
        }

        if (keyboard[toggleKey].wasPressedThisFrame && CanEnter())
            Enter();
    }

    private bool CanEnter()
    {
        if (controller == null || Time.timeScale <= 0f)
            return false;

        // ห้ามใช้ตอนถูกล็อก (เมนู/โน้ต/รัดคอ/ตาย) ปีน ข้ามสิ่งกีดขวาง อยู่ในท่อ หรือกำลังเล็งปืน
        if (controller.IsMovementLocked || controller.IsClimbing || controller.IsVaulting ||
            controller.isInVentMode || controller.IsAiming)
            return false;

        return wheel == null || !wheel.IsOpen;
    }

    private void Enter()
    {
        EnsureCamera();

        active = true;
        yaw = transform.eulerAngles.y;
        pitch = 0f;
        targetFov = defaultFov;
        eyeCamera.Lens.FieldOfView = defaultFov;
        UpdateCameraPose();

        eyeCamera.Priority.Value = cameraPriority;
        controller.SetMovementLocked(this, true);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        SfxLibrary.Play("eyewide_on");
        HudToast.Show("EYE WIDE  —  Scroll to zoom  •  T to exit", 2.5f);
    }

    private void Exit()
    {
        if (!active)
            return;

        active = false;

        if (eyeCamera != null)
            eyeCamera.Priority.Value = 0;

        if (controller != null)
            controller.SetMovementLocked(this, false);

        // กลับสู่โหมดเมาส์ของตอนเล่น (เหมือน TopDownPlayerController.Start)
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

        SfxLibrary.Play("eyewide_off");
    }

    private void HandleLook()
    {
        Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

        // ซูมมากแล้วหมุนช้าลงตามสัดส่วน จะได้เล็งดูของไกลๆ ได้นิ่ง
        float zoomFactor = Mathf.Clamp(eyeCamera.Lens.FieldOfView / defaultFov, 0.2f, 1.25f);

        yaw += delta.x * mouseSensitivity * zoomFactor;
        pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity * zoomFactor, minPitch, maxPitch);

        UpdateCameraPose();
    }

    private void HandleZoom()
    {
        float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        if (Mathf.Abs(scroll) > 0.01f)
        {
            float previous = targetFov;
            targetFov = Mathf.Clamp(targetFov - Mathf.Sign(scroll) * zoomStep, minFov, maxFov);

            // เสียงกล้องซูม: เล่นเฉพาะเมื่อซูมได้จริง (ไม่ดังตอนสุดระยะ)
            if (!Mathf.Approximately(previous, targetFov))
                SfxLibrary.Play(scroll > 0f ? "zoom_in" : "zoom_out");
        }

        eyeCamera.Lens.FieldOfView = Mathf.Lerp(eyeCamera.Lens.FieldOfView, targetFov, zoomSmooth * Time.unscaledDeltaTime);
    }

    private void UpdateCameraPose()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
        eyeCamera.transform.SetPositionAndRotation(transform.position + yawOnly * eyeOffset, rotation);
    }

    private void EnsureCamera()
    {
        if (eyeCamera != null)
            return;

        GameObject go = new GameObject("EyeWideCamera");
        go.transform.SetParent(transform, false);
        eyeCamera = go.AddComponent<CinemachineCamera>();
        eyeCamera.Priority.Value = 0;
        eyeCamera.Lens.FieldOfView = defaultFov;
        eyeCamera.Lens.NearClipPlane = 0.05f;
    }

    private void OnDisable()
    {
        Exit();
    }
}
