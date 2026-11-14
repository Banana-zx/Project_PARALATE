using UnityEngine;
using UnityEngine.InputSystem;

public class KnifeAction : MonoBehaviour
{
    [Header("Knife Settings")]
    public float damage = 25f;
    public float attackRange = 2f; // ระยะมีด (หน่วยเมตร) ถ้า prefab เคย override ไว้ต้องแก้ใน Inspector ด้วย
    public float attackRate = .5f;

    [Header("Raycast Settings")]
    public Vector3 raycastOffset = new Vector3(0f, 1f, 0f); // X=ซ้ายขวา, Y=ขึ้นลง, Z=หน้าหลัง

    [Header("State")]
    public bool isHeld = false;

    [Header("Input Actions")]
    [Tooltip("ไม่บังคับ: ถ้าเว้นว่างจะใช้ action \"Fire\" จาก PlayerInput ของผู้ถือ")]
    public InputActionReference attackAction;

    [Header("Player Reference")]
    public Transform playerTransform;

    private float nextAttackTime;
    private TopDownPlayerController ownerController;
    private InputAction resolvedAttackAction;
    private Transform resolvedFor;

    // เดิมหา action ใน Awake ซึ่งตอนนั้นมีดยังวางอยู่บนพื้น (ไม่ได้อยู่ใต้ผู้เล่น) จึงหา PlayerInput ไม่เจอ
    // และผูก event ครั้งเดียวตลอดชีวิต ตอนนี้เปลี่ยนเป็น "หาใหม่เมื่อเจ้าของเปลี่ยน + เช็กปุ่มใน Update" เหมือน GunAction
    private void Update()
    {
        if (!isHeld)
            return;

        InputAction action = ResolveAttackAction();
        if (action == null || !action.WasPressedThisFrame())
            return;

        if (Time.time < nextAttackTime || IsOwnerLocked())
            return;

        nextAttackTime = Time.time + attackRate;
        Attack();
    }

    private InputAction ResolveAttackAction()
    {
        Transform owner = playerTransform != null ? playerTransform : transform.parent;

        if (resolvedAttackAction != null && resolvedFor == owner)
            return resolvedAttackAction;

        resolvedFor = owner;
        resolvedAttackAction = null;
        ownerController = null;

        // ใช้ PlayerInput ของผู้ถือก่อน (action map ของผู้เล่นเปิดใช้งานอยู่แน่นอน)
        PlayerInput playerInput = owner != null ? owner.GetComponentInParent<PlayerInput>() : null;
        if (playerInput != null && playerInput.actions != null)
            resolvedAttackAction = playerInput.actions.FindAction("Fire", false);

        if (resolvedAttackAction == null && attackAction != null && attackAction.action != null)
        {
            resolvedAttackAction = attackAction.action;
            resolvedAttackAction.Enable();
        }

        return resolvedAttackAction;
    }

    // กำลังถูกล็อก (อ่านโน้ต, รัดคอ, ตาย, เปิดเมนู ฯลฯ) ห้ามฟัน
    private bool IsOwnerLocked()
    {
        if (ownerController == null)
        {
            ownerController = playerTransform != null
                ? playerTransform.GetComponent<TopDownPlayerController>()
                : GetComponentInParent<TopDownPlayerController>();
        }

        return ownerController != null && ownerController.IsMovementLocked;
    }

    // ฟังก์ชันสำหรับการโจมตีด้วยมีด
    void Attack()
    {
        Transform attackTransform = playerTransform != null ? playerTransform : transform;

        Vector3 attackOrigin = attackTransform.position
                               + (attackTransform.right * raycastOffset.x)
                               + (attackTransform.up * raycastOffset.y)
                               + (attackTransform.forward * raycastOffset.z);

        Vector3 attackDirection = attackTransform.forward;

        Debug.DrawRay(attackOrigin, attackDirection * attackRange, Color.red, 2f);
        SfxLibrary.Play("knife_swing", attackOrigin);

        if (Physics.Raycast(attackOrigin, attackDirection, out RaycastHit hit, attackRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            EnemyHealth enemy = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage);
                SfxLibrary.Play("knife_hit", hit.point);
            }
        }
    }
}
