using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// ลดภาระของกล้อง Minimap: ใน HDRP กล้องทุกตัวเรนเดอร์ "ทั้ง pipeline" (เงา, แสง, เอฟเฟกต์) ทุกเฟรม
/// กล้อง Minimap จึงกินแรงเกือบเท่ากล้องหลักอีกตัว ทั้งที่ภาพแผนที่ไม่จำเป็นต้องลื่นเท่าเกม
///
/// สิ่งที่ทำ:
///   1) เรนเดอร์เป็นช่วงๆ (ค่าเริ่มต้น 10 ครั้ง/วินาที): เปิด Camera แค่เฟรมที่ถึงเวลา เฟรมอื่นปิด
///      RenderTexture ยังเก็บภาพล่าสุดไว้ แผนที่บนจอจึงไม่กะพริบ
///   2) ปิดของแพงที่แผนที่มองจากด้านบนไม่ต้องใช้ (เงา, Volumetric Fog, SSAO, SSR, SSGI ฯลฯ) ผ่าน Custom Frame Settings
///   3) SetVisible(false) = ปิดกล้องสนิท (ใช้ตอน HUD ไม่ได้โชว์แผนที่)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class MinimapRenderThrottle : MonoBehaviour
{
    [Tooltip("จำนวนครั้งที่เรนเดอร์ต่อวินาที (0 = ทุกเฟรม)")]
    public float rendersPerSecond = 10f;

    private Camera targetCamera;
    private bool visible = true;
    private float nextRenderTime;

    public static MinimapRenderThrottle EnsureOn(Camera camera)
    {
        if (camera == null)
            return null;

        MinimapRenderThrottle throttle = camera.GetComponent<MinimapRenderThrottle>();
        if (throttle == null)
            throttle = camera.gameObject.AddComponent<MinimapRenderThrottle>();

        return throttle;
    }

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
        ApplyCheapFrameSettings();
    }

    public void SetVisible(bool value)
    {
        visible = value;
        nextRenderTime = 0f; // เปิดกลับมาแล้วเรนเดอร์ทันที 1 ครั้ง

        if (!value && targetCamera != null)
            targetCamera.enabled = false;
    }

    private void Update()
    {
        if (targetCamera == null)
            return;

        if (!visible)
        {
            if (targetCamera.enabled)
                targetCamera.enabled = false;

            return;
        }

        if (rendersPerSecond <= 0f)
        {
            targetCamera.enabled = true;
            return;
        }

        // เปิดกล้องเฉพาะเฟรมที่ถึงรอบ (กล้องที่ enabled ตอนจบ Update จะถูกเรนเดอร์ในเฟรมนั้น)
        bool due = Time.unscaledTime >= nextRenderTime;
        if (due)
            nextRenderTime = Time.unscaledTime + 1f / rendersPerSecond;

        if (targetCamera.enabled != due)
            targetCamera.enabled = due;
    }

    private void ApplyCheapFrameSettings()
    {
        HDAdditionalCameraData data = GetComponent<HDAdditionalCameraData>();
        if (data == null)
            return;

        data.customRenderingSettings = true;

        Disable(data, FrameSettingsField.ShadowMaps);
        Disable(data, FrameSettingsField.ContactShadows);
        Disable(data, FrameSettingsField.ScreenSpaceShadows);
        Disable(data, FrameSettingsField.Volumetrics);
        Disable(data, FrameSettingsField.ReprojectionForVolumetrics);
        Disable(data, FrameSettingsField.SSAO);
        Disable(data, FrameSettingsField.SSR);
        Disable(data, FrameSettingsField.SSGI);
        Disable(data, FrameSettingsField.SubsurfaceScattering);
        Disable(data, FrameSettingsField.Refraction);
        Disable(data, FrameSettingsField.MotionVectors);
    }

    private static void Disable(HDAdditionalCameraData data, FrameSettingsField field)
    {
        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
        data.renderingPathCustomFrameSettings.SetEnabled(field, false);
    }
}
