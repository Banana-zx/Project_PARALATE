using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// หน้าต่าง Upgrade Corner (สร้างด้วยโค้ด) 3 แท็บ:
///   Weapon : อัปเกรดอาวุธด้วย Scrap (แสดงต้นทุน, ผลที่ได้, ปุ่ม Upgrade)
///   X Gear : 3 ช่อง Passive / Active / Map กดวงกลมแล้วมี Panel เล็กด้านขวาให้เลือก Data ใส่
///   Craft  : คราฟต์กระสุนจาก Scrap / Metal
/// ใช้ของจาก PersistentInventory (ของที่เก็บจากด่านและจบด่านสำเร็จแล้ว)
/// </summary>
public class UpgradeUI : MonoBehaviour
{
    private enum Tab { Weapon, XGear, Craft }

    private static UpgradeUI instance;

    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    private static readonly Color Red = new Color(1f, 0.35f, 0.35f, 1f);

    private Tab currentTab = Tab.Weapon;
    private readonly Button[] tabButtons = new Button[3];
    private RectTransform content;
    private RectTransform picker;
    private TextMeshProUGUI resourceLabel;
    private TopDownPlayerController lockedPlayer;
    private ItemCategory? pickerSlot;

    public static void Open()
    {
        if (instance == null)
        {
            Canvas canvas = UIBuilder.CreateCanvas("UpgradeUI (Auto)", 200);
            instance = canvas.gameObject.AddComponent<UpgradeUI>();
            instance.Build();
        }

        instance.Show();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        PersistentInventory.OnChanged -= Refresh;
    }

    private void Update()
    {
        bool cancel = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                      (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        if (cancel)
            Close();
    }

    private void Show()
    {
        gameObject.SetActive(true);

        lockedPlayer = FindFirstObjectByType<TopDownPlayerController>();
        if (lockedPlayer != null)
            lockedPlayer.SetMovementLocked(this, true);

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        PersistentInventory.OnChanged -= Refresh;
        PersistentInventory.OnChanged += Refresh;

        SfxLibrary.PlayUI("menu_open");
        SelectTab(currentTab);
    }

    public void Close()
    {
        PersistentInventory.OnChanged -= Refresh;
        gameObject.SetActive(false);

        if (lockedPlayer != null)
            lockedPlayer.SetMovementLocked(this, false);

        lockedPlayer = null;

        // กลับสู่โหมดเมาส์ของตอนเล่น (เหมือน TopDownPlayerController.Start)
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

        SfxLibrary.PlayUI("menu_close");
    }

    // ------------------------------------------------------------ โครงหน้าต่าง

    private void Build()
    {
        Image dim = UIBuilder.Panel("Dim", transform, UIBuilder.DimColor);
        UIBuilder.Stretch(dim.rectTransform);

        // หน้าต่างหลัก (ซ้าย) + ตัวละคร (ขวา) + ปุ่ม X มุมขวาบน ตามภาพร่าง
        Image window = UIBuilder.Panel("Window", transform, UIBuilder.WindowColor);
        UIBuilder.Place(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190f, 0f), new Vector2(1000f, 700f));

        RectTransform tabs = UIBuilder.Rect("Tabs", window.transform);
        tabs.anchorMin = new Vector2(0f, 1f);
        tabs.anchorMax = new Vector2(1f, 1f);
        tabs.pivot = new Vector2(0.5f, 1f);
        tabs.anchoredPosition = Vector2.zero;
        tabs.sizeDelta = new Vector2(0f, 76f);
        HorizontalLayoutGroup tabRow = UIBuilder.Horizontal(tabs, 4f, 0);
        tabRow.childForceExpandWidth = true;
        tabRow.childForceExpandHeight = true;

        string[] names = { "Weapon", "X Gear", "Craft" };
        for (int i = 0; i < names.Length; i++)
        {
            Tab tab = (Tab)i;
            tabButtons[i] = UIBuilder.Button(names[i] + "Tab", tabs, names[i], () => SelectTab(tab), 30f);
        }

        content = UIBuilder.Rect("Content", window.transform);
        UIBuilder.Stretch(content, 28f, 70f, 28f, 96f);
        UIBuilder.Vertical(content, 14f);

        resourceLabel = UIBuilder.Text("Resources", window.transform, string.Empty, 26f, TextAlignmentOptions.BottomRight);
        resourceLabel.richText = true;
        UIBuilder.Stretch(resourceLabel.rectTransform, 28f, 18f, 28f, 640f);

        // ตัวละครประจำมุมอัปเกรด
        Image npc = UIBuilder.Panel("Character", transform, UIBuilder.WindowColor, false);
        UIBuilder.Place(npc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(500f, 0f), new Vector2(340f, 700f));
        UIPortrait.Build(npc.transform, "UI/upgrade_character");

        Button close = UIBuilder.Button("Close", transform, "X", Close, 34f, new Color(0.8f, 0.15f, 0.15f, 0.9f));
        UIBuilder.Place((RectTransform)close.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(722f, 384f), new Vector2(60f, 60f));

        // Panel เล็กสำหรับเลือก Data ใส่ X-Gear (ซ้อนอยู่ด้านขวาของหน้าต่าง)
        Image pickerImage = UIBuilder.Panel("DataPicker", window.transform, new Color(0.12f, 0.14f, 0.18f, 1f));
        picker = pickerImage.rectTransform;
        UIBuilder.Place(picker, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, -10f), new Vector2(360f, 420f));
        UIBuilder.Vertical(picker, 8f, 14);
        picker.gameObject.SetActive(false);
    }

    private void SelectTab(Tab tab)
    {
        currentTab = tab;
        pickerSlot = null;

        for (int i = 0; i < tabButtons.Length; i++)
            tabButtons[i].targetGraphic.color = i == (int)tab ? UIBuilder.AccentColor : UIBuilder.ButtonColor;

        Refresh();
    }

    private void Refresh()
    {
        if (!gameObject.activeSelf)
            return;

        UIBuilder.Clear(content);

        switch (currentTab)
        {
            case Tab.Weapon: BuildWeaponTab(); break;
            case Tab.XGear: BuildXGearTab(); break;
            case Tab.Craft: BuildCraftTab(); break;
        }

        RefreshPicker();

        PersistentInventory inventory = PersistentInventory.Instance;
        int scrap = inventory != null ? inventory.GetAmount(ItemCatalog.Scrap) : 0;
        int metal = inventory != null ? inventory.GetAmount(ItemCatalog.Metal) : 0;
        resourceLabel.text = $"scrap <color=#FF5A5A>{scrap}</color> / metal <color=#FF5A5A>{metal}</color>";
    }

    // แถวมาตรฐาน: [วงกลมไอคอน + ป้ายใต้วงกลม] [ข้อความ] [ปุ่มขวา]
    private Button AddRow(string iconText, string underLabel, Color underColor, string body, Color bodyColor,
        string buttonLabel, System.Action onButton, bool buttonEnabled, System.Action onIconClick = null, Sprite icon = null)
    {
        Image row = UIBuilder.Panel("Row", content, UIBuilder.PanelColor, false);
        UIBuilder.Size(row, -1f, 150f);
        UIBuilder.Horizontal(row.rectTransform, 22f, 16);

        // วงกลม + ป้ายใต้วงกลม
        RectTransform iconColumn = UIBuilder.Rect("IconColumn", row.transform);
        UIBuilder.Size(iconColumn, 130f, 118f);

        Image circle = UIBuilder.Panel("Circle", iconColumn, new Color(1f, 1f, 1f, 0.16f), onIconClick != null);
        circle.sprite = UIBuilder.CircleSprite;
        UIBuilder.Place(circle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(84f, 84f));

        if (icon != null)
        {
            Image iconImage = UIBuilder.Panel("Icon", circle.transform, Color.white, false);
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            UIBuilder.Stretch(iconImage.rectTransform, 14f, 14f, 14f, 14f);
        }
        else
        {
            TextMeshProUGUI iconLabel = UIBuilder.Text("IconText", circle.transform, iconText, 20f, TextAlignmentOptions.Center);
            UIBuilder.Stretch(iconLabel.rectTransform);
        }

        if (onIconClick != null)
        {
            Button iconButton = circle.gameObject.AddComponent<Button>();
            iconButton.targetGraphic = circle;
            iconButton.onClick.AddListener(() => onIconClick());
            circle.gameObject.AddComponent<UIPointerRelay>().playSounds = true;
        }

        TextMeshProUGUI under = UIBuilder.Text("Under", iconColumn, underLabel, 18f, TextAlignmentOptions.Bottom, underColor);
        UIBuilder.Stretch(under.rectTransform, 0f, 0f, 0f, 88f);

        TextMeshProUGUI bodyText = UIBuilder.Text("Body", row.transform, body, 24f, TextAlignmentOptions.MidlineLeft, bodyColor);
        UIBuilder.Size(bodyText, -1f, 118f, 1f);

        if (string.IsNullOrEmpty(buttonLabel))
            return null;

        Button button = UIBuilder.Button("Action", row.transform, buttonLabel, onButton, 24f);
        button.interactable = buttonEnabled;
        UIBuilder.Size(button, 170f, 64f);
        return button;
    }

    // ------------------------------------------------------------ Weapon

    private void BuildWeaponTab()
    {
        PersistentInventory inventory = PersistentInventory.Instance;

        for (int i = 0; i < WeaponUpgrades.WeaponKeys.Length; i++)
        {
            string key = WeaponUpgrades.WeaponKeys[i];
            int level = WeaponUpgrades.GetLevel(key);
            bool maxed = level >= WeaponUpgrades.MaxLevel;
            int cost = WeaponUpgrades.NextCost(key);
            bool affordable = inventory != null && inventory.Has(ItemCatalog.Scrap, cost);

            string current = $"Now: Damage +{level * WeaponUpgrades.DamagePerLevel * 100f:0}%, Reload speed +{level * WeaponUpgrades.ReloadSpeedPerLevel * 100f:0}%";
            string body = maxed
                ? $"<b>{key}</b>   MAX LEVEL\n{current}"
                : $"<b>{key}</b>   x{cost} scrap\n+{WeaponUpgrades.ReloadSpeedPerLevel * 100f:0}% Reload speed, +{WeaponUpgrades.DamagePerLevel * 100f:0}% Damage\n<size=18><color=#FFFFFF88>{current}</color></size>";

            AddRow(key, "LV." + (level + 1), Red, body, UIBuilder.TextColor,
                maxed ? "MAX" : "upgrade",
                () =>
                {
                    if (WeaponUpgrades.TryUpgrade(key))
                    {
                        SfxLibrary.PlayUI("upgrade_success");
                        HudToast.Show(key + " upgraded to LV." + (WeaponUpgrades.GetLevel(key) + 1));
                        Refresh();
                    }
                    else
                    {
                        SfxLibrary.PlayUI("ui_error");
                    }
                },
                !maxed && affordable);
        }
    }

    // ------------------------------------------------------------ X Gear

    private static readonly ItemCategory[] GearSlots = { ItemCategory.PassiveChip, ItemCategory.ActiveChip, ItemCategory.MapData };
    private static readonly string[] GearSlotNames = { "Passive", "Active", "Map" };
    private static readonly string[] GearEmptyTexts = { "No Passive skill Data", "No Active skill Data", "No map data" };

    private void BuildXGearTab()
    {
        for (int i = 0; i < GearSlots.Length; i++)
        {
            ItemCategory slot = GearSlots[i];
            string equipped = XGearLoadout.Get(slot);
            ItemDef def = ItemCatalog.Get(equipped);

            string body = def == null
                ? GearEmptyTexts[i]
                : $"<b>{def.displayName}</b>\n<size=20>{def.description}</size>";

            AddRow(GearSlotNames[i], GearSlotNames[i], UIBuilder.MutedText, body, def == null ? Red : UIBuilder.TextColor,
                null, null, false,
                () =>
                {
                    // กดวงกลม: เปิด/ปิด Panel เลือก Data ของช่องนี้
                    pickerSlot = pickerSlot == slot ? (ItemCategory?)null : slot;
                    RefreshPicker();
                },
                def != null ? ItemCatalog.IconOf(def.id) : null);
        }
    }

    private void RefreshPicker()
    {
        bool show = currentTab == Tab.XGear && pickerSlot.HasValue;
        picker.gameObject.SetActive(show);

        if (!show)
            return;

        UIBuilder.Clear(picker);

        ItemCategory slot = pickerSlot.Value;
        int slotIndex = System.Array.IndexOf(GearSlots, slot);

        TextMeshProUGUI title = UIBuilder.Text("Title", picker, "Insert " + GearSlotNames[slotIndex] + " Data", 24f, TextAlignmentOptions.Center);
        UIBuilder.Size(title, -1f, 40f);

        PersistentInventory inventory = PersistentInventory.Instance;
        int count = 0;

        IReadOnlyList<ItemDef> all = ItemCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            ItemDef def = all[i];
            if (def.category != slot || inventory == null || !inventory.Has(def.id))
                continue;

            count++;
            Button button = UIBuilder.Button(def.id, picker, $"{def.displayName}  x{inventory.GetAmount(def.id)}", () =>
            {
                if (XGearLoadout.Equip(slot, def.id))
                {
                    SfxLibrary.PlayUI("chip_insert");
                    pickerSlot = null;
                    Refresh();
                }
            }, 20f);
            UIBuilder.Size(button, -1f, 56f);
        }

        if (count == 0)
        {
            TextMeshProUGUI empty = UIBuilder.Text("Empty", picker, "No data in inventory.\nFind chips / map data in missions.", 20f,
                TextAlignmentOptions.Center, UIBuilder.MutedText);
            UIBuilder.Size(empty, -1f, 90f);
        }

        if (!string.IsNullOrEmpty(XGearLoadout.Get(slot)))
        {
            Button remove = UIBuilder.Button("Remove", picker, "Remove (back to inventory)", () =>
            {
                XGearLoadout.Equip(slot, string.Empty);
                SfxLibrary.PlayUI("chip_remove");
                pickerSlot = null;
                Refresh();
            }, 20f, new Color(0.8f, 0.2f, 0.2f, 0.5f));
            UIBuilder.Size(remove, -1f, 56f);
        }

        Button cancel = UIBuilder.Button("Cancel", picker, "Close", () =>
        {
            pickerSlot = null;
            RefreshPicker();
        }, 20f);
        UIBuilder.Size(cancel, -1f, 48f);
    }

    // ------------------------------------------------------------ Craft

    private void BuildCraftTab()
    {
        PersistentInventory inventory = PersistentInventory.Instance;

        for (int i = 0; i < AmmoRecipe.All.Length; i++)
        {
            AmmoRecipe recipe = AmmoRecipe.All[i];
            ItemDef def = ItemCatalog.Get(recipe.ammoId);

            bool affordable = inventory != null &&
                              inventory.Has(ItemCatalog.Scrap, recipe.scrap) &&
                              inventory.Has(ItemCatalog.Metal, recipe.metal);

            string cost = (recipe.metal > 0 ? recipe.metal + "x metal  " : string.Empty) +
                          (recipe.scrap > 0 ? recipe.scrap + "x scrap" : string.Empty);
            int owned = inventory != null ? inventory.GetAmount(recipe.ammoId) : 0;

            string shortName = def.displayName.Replace(" Ammo", string.Empty);

            AddRow(shortName, shortName + " Bullet", UIBuilder.TextColor,
                $"{cost}\n<size=20>Get x{recipe.yield}   <color=#FFFFFF88>(have {owned})</color></size>", UIBuilder.TextColor,
                "craft",
                () =>
                {
                    if (recipe.TryCraft())
                    {
                        SfxLibrary.PlayUI("craft_success");
                        HudToast.Show("+ " + def.displayName + " x" + recipe.yield);
                    }
                    else
                    {
                        SfxLibrary.PlayUI("ui_error");
                    }
                },
                affordable, null, ItemCatalog.IconOf(recipe.ammoId));
        }
    }
}

/// <summary>ช่องภาพตัวละคร (Portrait): ใช้ Sprite จาก Resources ถ้ามี ไม่งั้นวาดเงาคนแบบง่ายแทน</summary>
public static class UIPortrait
{
    public static void Build(Transform parent, string resourcePath)
    {
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null)
        {
            Image image = UIBuilder.Panel("Portrait", parent, Color.white, false);
            image.sprite = sprite;
            image.preserveAspect = true;
            UIBuilder.Stretch(image.rectTransform, 12f, 12f, 12f, 12f);
            return;
        }

        Color silhouette = new Color(1f, 1f, 1f, 0.14f);

        Image head = UIBuilder.Panel("Head", parent, silhouette, false);
        head.sprite = UIBuilder.CircleSprite;
        UIBuilder.Place(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(130f, 130f));

        Image body = UIBuilder.Panel("Body", parent, silhouette, false);
        UIBuilder.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 95f), new Vector2(190f, 330f));

        TextMeshProUGUI label = UIBuilder.Text("Label", parent, "PORTRAIT", 18f, TextAlignmentOptions.Bottom, UIBuilder.MutedText);
        UIBuilder.Stretch(label.rectTransform, 0f, 14f, 0f, 0f);
    }
}
