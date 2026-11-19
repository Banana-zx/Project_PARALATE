using UnityEngine;

/// <summary>
/// ของเก็บได้ทั่วไปตาม id ใน ItemCatalog: กล่องกระสุน, ชิป X-Gear, Map Data
/// (ใช้ระบบเดียวกับ ItemPickup/ResourcePickup: ต้องมี Collider และอยู่ใน Layer "Items")
/// - กระสุน: เข้ากระเป๋าทันที ใช้รีโหลดได้เลย
/// - อย่างอื่น: เข้า RunInventory ก่อน จบด่านสำเร็จถึงเป็นของถาวร (กติกาเดียวกับ Scrap/Metal)
/// </summary>
[RequireComponent(typeof(Collider))]
public class InventoryPickup : MonoBehaviour, IInteractable
{
    [Tooltip("id ตาม ItemCatalog เช่น ammo_ak, ammo_usp, ammo_sniper, chip_fleetfoot, chip_invisible, map_hideout")]
    public string itemId = ItemCatalog.AmmoRifle;
    [Min(1)] public int amount = 1;

    [Tooltip("{0} = ชื่อไอเทม, {1} = จำนวน")]
    [TextArea] public string promptFormat = "[ E ] to collect\n{0} x{1}";

    public AudioClip pickupSound;

    public string PromptText
    {
        get
        {
            ItemDef def = ItemCatalog.Get(itemId);
            if (def != null && def.category == ItemCategory.Consumable)
                return $"[ E ] to use {def.displayName}\n{ConsumableEffect()}";

            return string.Format(promptFormat, ItemCatalog.NameOf(itemId), amount);
        }
    }

    private string ConsumableEffect()
    {
        return itemId == ItemCatalog.Medkit ? $"+{amount} HP" : $"+{amount} X-Gear charge";
    }

    // ของใช้ทันที: ใช้ไม่ได้ (เลือดเต็ม / X-Gear เต็ม) = ไม่เก็บ ของยังอยู่ที่เดิม
    private bool TryConsume(GameObject interactor, out string reason)
    {
        reason = null;

        if (itemId == ItemCatalog.Medkit)
        {
            PlayerHealth health = interactor.GetComponentInParent<PlayerHealth>();
            if (health == null)
                return false;

            if (!health.Heal(amount))
            {
                reason = "Health is full";
                return false;
            }

            return true;
        }

        if (itemId == ItemCatalog.XGearCell)
        {
            XGearSystem gear = interactor.GetComponentInParent<XGearSystem>();
            return gear != null && gear.AddCharges(amount, out reason);
        }

        return false;
    }

    public void Interact(GameObject interactor)
    {
        ItemDef def = ItemCatalog.Get(itemId);
        if (def == null)
            return;

        if (def.category == ItemCategory.Consumable)
        {
            if (!TryConsume(interactor, out string reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    HudToast.Show(reason);

                SfxLibrary.Play("ui_error");
                return;
            }

            SfxLibrary.PlayPickup();
            HudToast.Show(def.displayName + "  " + ConsumableEffect());
            Destroy(gameObject);
            return;
        }

        if (def.category == ItemCategory.Ammo)
        {
            if (PersistentInventory.Instance == null)
                return;

            PersistentInventory.Instance.Add(itemId, amount);
        }
        else
        {
            if (RunInventory.Instance == null)
                return;

            RunInventory.Instance.Add(itemId, amount);
        }

        // เสียงเก็บของเสียงเดียวกันทั้งเกม (ถ้าไม่มีไฟล์ ใช้ pickupSound ของ Prefab / เสียงเริ่มต้นของ AudioManager)
        if (!SfxLibrary.PlayPickup())
        {
            if (pickupSound != null)
                AudioManager.PlayPickup(pickupSound, transform.position);
            else
                AudioManager.Instance?.PlayItemPickupSfx();
        }

        HudToast.Show("+ " + def.displayName + " x" + amount);
        Destroy(gameObject);
    }
}
