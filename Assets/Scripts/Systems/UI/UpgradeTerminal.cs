using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// จุด Upgrade Corner ใน Base: กด E เพื่อเปิดหน้าต่างอัปเกรด (ใช้ระบบเดียวกับ LevelSelectTerminal:
/// ต้องมี Collider และอยู่ใน Layer "Items" ที่ PickupDetector ตรวจ)
/// </summary>
[RequireComponent(typeof(Collider))]
public class UpgradeTerminal : MonoBehaviour, IInteractable
{
    public string promptText = "[ E ] to upgrade";

    public string PromptText => UpgradeUI.IsOpen ? string.Empty : promptText;

    public void Interact(GameObject interactor)
    {
        if (!UpgradeUI.IsOpen)
            UpgradeUI.Open();
    }

    /// <summary>
    /// กันพลาด: ถ้า Scene Base ยังไม่มี UpgradeTerminal จะติดให้วัตถุชื่อ "Upgrade" อัตโนมัติ
    /// (ปกติ Tools > Paralate > Setup Content จะติดให้ใน Scene ถาวรแล้ว)
    /// </summary>
    public static void EnsureInBase()
    {
        if (SceneManager.GetActiveScene().name != SceneNames.Base)
            return;

        if (FindFirstObjectByType<UpgradeTerminal>(FindObjectsInactive.Include) != null)
            return;

        GameObject target = GameObject.Find("Upgrade");
        if (target == null || target.GetComponent<Collider>() == null)
            return;

        int itemsLayer = LayerMask.NameToLayer("Items");
        if (itemsLayer >= 0)
            target.layer = itemsLayer;

        target.AddComponent<UpgradeTerminal>();
    }
}
