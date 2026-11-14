using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// อาวุธที่ผู้เล่นพกอยู่ แบ่งเป็นช่อง:
///   Primary   = อาวุธหลัก (ปืนยาว/หนัก เช่น AK, Sniper)
///   Secondary = อาวุธรอง (ปืนพก, มีด)
///   X-Gear    = แขนกล ติดตัวตลอด (ไม่ใช่ GameObject อาวุธ ดู XGearSystem)
/// + Stash = อาวุธที่ถอดเก็บเข้า Inventory (ยังอยู่กับตัวผู้เล่นในด่านนี้ แต่ไม่ได้อยู่ในช่อง)
/// "ช่องที่ถืออยู่ตอนนี้" คือ CurrentSlot (Unarmed / Primary / Secondary / XGear)
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public class PickupSystem : MonoBehaviour
{
    [Header("Pickup Settings")]
    public Transform PickUp;

    public Vector3 holdPositionOffset;
    public Vector3 holdRotationOffset;

    [Header("UI")]
    [Tooltip("ไม่บังคับ: ถ้าเว้นว่างจะหา AmmoUIManager ใน Scene เอง")]
    public AmmoUIManager ammoUIManager;

    [Header("Weapon Wheel")]
    public WeaponWheelUI weaponWheel;

    /// <summary>ช่องที่ถือเปลี่ยน (ใช้เล่นเสียง/อัปเดต HUD)</summary>
    public event Action<HeldSlot> OnHeldSlotChanged;

    /// <summary>ของในช่อง/ใน Stash เปลี่ยน (หน้า Pause ใช้รีเฟรช)</summary>
    public event Action OnLoadoutChanged;

    private GameObject primaryItem;
    private GameObject secondaryItem;
    private readonly List<GameObject> stash = new List<GameObject>();
    private HeldSlot currentSlot = HeldSlot.Unarmed;

    private PlayerInput playerInput;
    private InputAction interactAction;
    private InputAction switchWeaponAction;
    private InputAction unequipWeaponAction;
    private TopDownPlayerController movementController;
    private KnockoutSystem knockoutSystem;
    private PickupDetector pickupDetector;
    private bool searchedAmmoUI;

    public HeldSlot CurrentSlot => currentSlot;
    public GameObject HeldItem => GetItem(currentSlot);
    public bool HasEquippedWeapon => HeldItem != null;
    public bool HasKnifeEquipped => HeldItem != null && HeldItem.GetComponent<KnifeAction>() != null;
    public IReadOnlyList<GameObject> Stash => stash;

    /// <summary>เปลี่ยนอาวุธได้หรือไม่ (ห้ามตอนถูกล็อก/กำลังรัดคอ)</summary>
    public bool CanChangeWeapon =>
        (movementController == null || !movementController.IsMovementLocked) &&
        (knockoutSystem == null || !knockoutSystem.IsKnockout);

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        interactAction = playerInput.actions["Interact"];
        switchWeaponAction = playerInput.actions["SwitchWeapon"];
        unequipWeaponAction = playerInput.actions["UnequipWeapon"];
        movementController = GetComponent<TopDownPlayerController>();
        knockoutSystem = GetComponent<KnockoutSystem>();
        pickupDetector = GetComponent<PickupDetector>();

        if (weaponWheel == null)
            weaponWheel = GetComponent<WeaponWheelUI>();
    }

    void Update()
    {
        if (knockoutSystem != null && knockoutSystem.IsKnockout)
            return;

        if (movementController != null && movementController.IsMovementLocked)
        {
            // ล็อกอยู่ (อ่านโน้ต ฯลฯ) ยังกด E ได้ เพื่อปิดโน้ต แต่ห้ามตอนเกมหยุด (เมนู Esc)
            if (Time.timeScale > 0f && pickupDetector != null && interactAction.WasPressedThisFrame())
                pickupDetector.TryInteract();

            return;
        }

        if (pickupDetector != null && interactAction.WasPressedThisFrame())
            pickupDetector.TryInteract();

        // ถ้าวงล้อเปิดอยู่ ไม่ต้องรับ input สลับ/ถอดแบบเดิม
        if (weaponWheel != null && weaponWheel.IsOpen)
            return;

        if (switchWeaponAction.WasPressedThisFrame())
            CycleSlot();

        if (unequipWeaponAction.WasPressedThisFrame())
            Select(HeldSlot.Unarmed);
    }

    // ------------------------------------------------------------ ข้อมูลช่อง

    public static WeaponSlotType SlotTypeOf(GameObject item)
    {
        if (item == null)
            return WeaponSlotType.Auto;

        GunAction gun = item.GetComponent<GunAction>();
        if (gun != null)
            return gun.ResolvedSlot;

        // มีด = อาวุธรอง
        return item.GetComponent<KnifeAction>() != null ? WeaponSlotType.Secondary : WeaponSlotType.Auto;
    }

    public GameObject GetItem(HeldSlot slot)
    {
        switch (slot)
        {
            case HeldSlot.Primary: return primaryItem;
            case HeldSlot.Secondary: return secondaryItem;
            default: return null;
        }
    }

    /// <summary>ช่องนี้เลือกถือได้ไหม (Unarmed และ X-Gear เลือกได้เสมอ)</summary>
    public bool IsSlotAvailable(HeldSlot slot)
    {
        return slot == HeldSlot.Unarmed || slot == HeldSlot.XGear || GetItem(slot) != null;
    }

    public static string DisplayNameOf(GameObject weapon)
    {
        if (weapon == null)
            return "Empty";

        WeaponInfo info = weapon.GetComponent<WeaponInfo>();
        string display = info != null ? info.DisplayName : weapon.name;
        return display.Replace("(Clone)", string.Empty).Trim();
    }

    public static string DescribeWeapon(GameObject weapon)
    {
        if (weapon == null)
            return string.Empty;

        GunAction gun = weapon.GetComponent<GunAction>();
        if (gun != null)
        {
            return $"{(gun.ResolvedSlot == WeaponSlotType.Primary ? "Primary weapon" : "Secondary weapon")}\n" +
                   $"Damage {gun.EffectiveDamage:0.#}  (Headshot x{gun.HeadshotMultiplier:0.#})\n" +
                   $"Fire rate {gun.fireRate:0.##}s  |  Magazine {gun.CurrentAmmo}/{gun.MagazineSize}\n" +
                   $"Ammo: {ItemCatalog.NameOf(gun.AmmoItemId)} x{gun.ReserveAmmo}\n" +
                   $"Upgrade Lv.{WeaponUpgrades.GetLevel(gun.UpgradeKey)}";
        }

        KnifeAction knife = weapon.GetComponent<KnifeAction>();
        if (knife != null)
            return $"Secondary weapon (melee)\nDamage {knife.damage:0.#}\nFaster silent kills while held.";

        return string.Empty;
    }

    // ------------------------------------------------------------ เก็บ / ถือ / ถอด

    /// <summary>คืน true ถ้าเก็บสำเร็จ ถ้าช่องของอาวุธชนิดนั้นเต็มอยู่ อาวุธใหม่จะเข้า Inventory (Stash) แทน</summary>
    public bool PickUpObject(GameObject item)
    {
        if (item == null)
            return false;

        WeaponSlotType slotType = SlotTypeOf(item);
        if (slotType == WeaponSlotType.Auto)
            return false;

        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb != null)
            rb.isKinematic = true;

        Collider[] colliders = item.GetComponentsInChildren<Collider>();
        foreach (Collider coll in colliders)
            coll.enabled = false;

        item.transform.SetParent(PickUp);
        item.transform.localPosition = holdPositionOffset;
        item.transform.localEulerAngles = holdRotationOffset;

        KnifeAction knife = item.GetComponent<KnifeAction>();
        if (knife != null)
            knife.playerTransform = transform;

        HeldSlot slot = slotType == WeaponSlotType.Primary ? HeldSlot.Primary : HeldSlot.Secondary;

        if (GetItem(slot) == null)
        {
            SetSlotItem(slot, item);
            OnLoadoutChanged?.Invoke();
            Select(slot);
        }
        else
        {
            // ช่องเต็ม: เก็บเข้า Inventory (สลับมาใส่ได้จากเมนู Esc > Inventory)
            SetWeaponHeld(item, false);
            stash.Add(item);
            OnLoadoutChanged?.Invoke();
            HudToast.Show(DisplayNameOf(item) + " stored in Inventory");
        }

        return true;
    }

    /// <summary>เลือกช่องที่จะถือ (เรียกจากวงล้อ Tab / ปุ่มลัด) ช่องว่างจะถูกปฏิเสธ</summary>
    public bool Select(HeldSlot slot)
    {
        if (!IsSlotAvailable(slot))
            return false;

        GameObject target = GetItem(slot);

        // ถือชิ้นนี้อยู่แล้ว ไม่ต้องปิด/เปิดใหม่ (กัน reload ถูกยกเลิกและเสียง equip เล่นซ้ำ)
        if (slot == currentSlot && (target == null || target.activeSelf))
            return true;

        SetWeaponHeld(primaryItem, false);
        SetWeaponHeld(secondaryItem, false);

        currentSlot = slot;

        if (target != null)
            SetWeaponHeld(target, true);

        RefreshWeaponHud();
        OnHeldSlotChanged?.Invoke(slot);
        return true;
    }

    /// <summary>ถอดอาวุธออกจากช่อง → เข้า Inventory (Stash) โดยตรง</summary>
    public bool UnequipToInventory(HeldSlot slot)
    {
        GameObject item = GetItem(slot);
        if (item == null || !CanChangeWeapon)
            return false;

        if (currentSlot == slot)
            Select(HeldSlot.Unarmed);

        SetWeaponHeld(item, false);
        SetSlotItem(slot, null);
        stash.Add(item);

        SfxLibrary.PlayUI("weapon_unequip");
        OnLoadoutChanged?.Invoke();
        return true;
    }

    /// <summary>ใส่อาวุธจาก Inventory (Stash) กลับเข้าช่องของมัน ถ้าช่องมีของอยู่จะสลับกัน</summary>
    public bool EquipFromStash(GameObject item)
    {
        if (item == null || !stash.Contains(item) || !CanChangeWeapon)
            return false;

        HeldSlot slot = SlotTypeOf(item) == WeaponSlotType.Primary ? HeldSlot.Primary : HeldSlot.Secondary;
        GameObject previous = GetItem(slot);
        bool wasHolding = currentSlot == slot;

        if (wasHolding)
            Select(HeldSlot.Unarmed);

        stash.Remove(item);

        if (previous != null)
        {
            SetWeaponHeld(previous, false);
            stash.Add(previous);
        }

        SetSlotItem(slot, item);

        if (wasHolding)
            Select(slot);

        SfxLibrary.PlayUI("weapon_equip");
        OnLoadoutChanged?.Invoke();
        return true;
    }

    // ปุ่มลัดสลับอาวุธ (ปุ่ม 1): วนเฉพาะช่องที่มีอาวุธจริง Primary ↔ Secondary
    void CycleSlot()
    {
        if (primaryItem == null && secondaryItem == null)
            return;

        if (currentSlot == HeldSlot.Primary && secondaryItem != null)
            Select(HeldSlot.Secondary);
        else if (currentSlot == HeldSlot.Secondary && primaryItem != null)
            Select(HeldSlot.Primary);
        else
            Select(primaryItem != null ? HeldSlot.Primary : HeldSlot.Secondary);
    }

    void SetSlotItem(HeldSlot slot, GameObject item)
    {
        if (slot == HeldSlot.Primary) primaryItem = item;
        else if (slot == HeldSlot.Secondary) secondaryItem = item;
    }

    void SetWeaponHeld(GameObject item, bool held)
    {
        if (item == null)
            return;

        item.SetActive(held);

        GunAction gun = item.GetComponent<GunAction>();
        if (gun != null)
            gun.SetHeld(held);

        KnifeAction knife = item.GetComponent<KnifeAction>();
        if (knife != null)
            knife.isHeld = held;
    }

    void RefreshWeaponHud()
    {
        if (ammoUIManager == null && !searchedAmmoUI)
        {
            searchedAmmoUI = true;
            ammoUIManager = FindFirstObjectByType<AmmoUIManager>(FindObjectsInactive.Include);
        }

        if (ammoUIManager == null)
            return;

        if (currentSlot == HeldSlot.XGear)
            ammoUIManager.SetLabelOnly("X-GEAR");
        else
            ammoUIManager.SetActiveWeapon(HeldItem);
    }

    // ------------------------------------------------------------ ของเดิม (ยังมีสคริปต์อื่นเรียกใช้)

    /// <summary>อาวุธในช่องทั้งหมด เรียง Primary ก่อน Secondary</summary>
    public List<GameObject> GetOwnedWeapons()
    {
        List<GameObject> weapons = new List<GameObject>();

        if (primaryItem != null) weapons.Add(primaryItem);
        if (secondaryItem != null) weapons.Add(secondaryItem);

        return weapons;
    }

    public void EquipWeaponPublic(GameObject item)
    {
        if (item == primaryItem) Select(HeldSlot.Primary);
        else if (item == secondaryItem) Select(HeldSlot.Secondary);
    }

    public void UnequipWeaponPublic()
    {
        Select(HeldSlot.Unarmed);
    }
}
