using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// แก้ "จอกระพริบดำ/ภาพเบลอ 1 เฟรม" ตอน Cinemachine ตัดกล้อง (Cut) ใน HDRP
///
/// สาเหตุ: HDRP เก็บภาพ/ข้อมูลของเฟรมก่อนหน้า (History) ไว้ใช้กับ Motion Blur, TAA, SSAO, SSR, Auto Exposure
///   พอกล้องกระโดดจากห้องหนึ่งไปอีกห้องในเฟรมเดียว HDRP ไม่รู้ว่าเป็นการตัดภาพ
///   จึงคิดว่ากล้องพุ่งไปไกลมากใน 1 เฟรม → Motion Blur ป้ายทั้งจอ / ผสมภาพห้องเก่า → เห็นเป็นจอมืดวาบ
///   (Default Volume ของโปรเจกต์นี้เปิด Motion Blur 0.5 ไว้)
/// วิธีแก้: ฟัง CinemachineCore.CameraActivatedEvent ถ้าเป็นการ Cut ให้เรียก HDCamera.Reset()
///   = บอก HDRP ว่า "เฟรมนี้คือเฟรมแรก ทิ้ง History เก่า" แบบเดียวกับตอนตัดฉากในหนัง
/// ทำงานเองอัตโนมัติ ไม่ต้องวางในฉาก
/// </summary>
public static class CameraCutHistoryReset
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        CinemachineCore.CameraActivatedEvent.RemoveListener(OnCameraActivated);
        CinemachineCore.CameraActivatedEvent.AddListener(OnCameraActivated);
    }

    private static void OnCameraActivated(ICinemachineCamera.ActivationEventParams evt)
    {
        // เปลี่ยนแบบ Blend (ค่อยๆ เลื่อน) ไม่ต้องรีเซ็ต เพราะกล้องเคลื่อนต่อเนื่องจริง
        if (!evt.IsCut || evt.OutgoingCamera == null)
            return;

        CinemachineBrain brain = evt.Origin as CinemachineBrain;
        Camera camera = brain != null ? brain.OutputCamera : null;
        if (camera == null)
            return;

        HDCamera.GetOrCreate(camera).Reset();
    }
}
