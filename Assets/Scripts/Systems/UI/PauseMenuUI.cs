using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// เมนูตอนกด Esc ในเกม (สร้างด้วยโค้ด ทุก Scene ที่มีผู้เล่น): Portrait ซ้าย / เนื้อหากลาง / เมนูขวา
///   Equipment : 3 ช่อง Primary / Secondary / X-Gear — ชี้เมาส์มีกล่องอธิบายด้านขวา, คลิกขวา = ถอดเข้า Inventory
///   Inventory : ตารางช่องของ (กระสุน, Scrap, Metal, ชิป, อาวุธที่ถอดเก็บ) — คลิกซ้ายอาวุธ = ใส่กลับเข้าช่อง
///   Setting   : Fullscreen + เสียง Main / Music / SFX
///   Exit      : ถามยืนยัน Yes / No
/// เปิดเมนู = หยุดเวลา (Time.timeScale = 0) และล็อกผู้เล่นด้วยระบบล็อกแบบมีเจ้าของ
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    private enum Page { Equipment, Inventory, Setting, Exit }

    private static readonly string[] PageNames = { "equipment", "inventory", "setting", "exit" };

    private TopDownPlayerController player;
    private PickupSystem pickup;

    private GameObject root;
    private RectTransform content;
    private readonly Button[] navButtons = new Button[4];
    private Image tooltip;
    private TextMeshProUGUI tooltipText;

    private Page currentPage = Page.Equipment;
    private bool isOpen;
    private bool lockedLastFrame;
    private float timeScaleBeforePause = 1f;

    public bool IsOpen => isOpen;

    public static void EnsureInScene(TopDownPlayerController player)
    {
        if (player == null || FindFirstObjectByType<PauseMenuUI>() != null)
            return;

        GameObject go = new GameObject("PauseMenu (Auto)");
        PauseMenuUI menu = go.AddComponent<PauseMenuUI>();
        menu.player = player;
        menu.pickup = player.GetComponent<PickupSystem>();
    }

    private void OnEnable()
    {
        PersistentInventory.OnChanged += RefreshIfOpen;
        RunInventory.OnChanged += RefreshIfOpen;
    }

    private void OnDisable()
    {
        PersistentInventory.OnChanged -= RefreshIfOpen;
        RunInventory.OnChanged -= RefreshIfOpen;

        if (isOpen)
            Close();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

        if (!escape)
            return;

        if (isOpen)
        {
            Close();
            return;
        }

        // เปิดได้เฉพาะตอนผู้เล่นไม่ได้ถูกล็อกโดยอย่างอื่น (โน้ต / หน้าเลือกด่าน / Upgrade / Eye Wide / ตาย)
        // เช็กค่าของ "เฟรมก่อน" ด้วย เพราะหน้าต่างอื่นใช้ Esc ปิดตัวเองในเฟรมเดียวกันนี้
        if (player == null || player.IsMovementLocked || lockedLastFrame)
            return;

        if (LevelManager.IsTransitionRunning)
            return;

        Open();
    }

    private void LateUpdate()
    {
        lockedLastFrame = player != null && player.IsMovementLocked && !isOpen;
    }

    public void Open()
    {
        if (isOpen)
            return;

        EnsureUI();
        isOpen = true;
        root.SetActive(true);

        timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        player.SetMovementLocked(this, true);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        SfxLibrary.PlayUI("menu_open");
        SelectPage(Page.Equipment);
    }

    public void Close()
    {
        if (!isOpen)
            return;

        isOpen = false;

        if (root != null)
            root.SetActive(false);

        Time.timeScale = timeScaleBeforePause;

        if (player != null)
            player.SetMovementLocked(this, false);

        // กลับสู่โหมดเมาส์ของตอนเล่น (เหมือน TopDownPlayerController.Start)
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

        if (AudioManager.Instance != null)
            AudioManager.Instance.SaveSettings();

        SfxLibrary.PlayUI("menu_close");
    }

    private void OnDestroy()
    {
        if (root != null)
            Destroy(root);
    }

    // ------------------------------------------------------------ โครงหน้าต่าง

    private void EnsureUI()
    {
        if (root != null)
            return;

        Canvas canvas = UIBuilder.CreateCanvas("PauseMenuCanvas (Auto)", 300);
        root = canvas.gameObject;

        Image dim = UIBuilder.Panel("Dim", root.transform, UIBuilder.DimColor);
        UIBuilder.Stretch(dim.rectTransform);

        // Portrait ซ้าย
        Image portrait = UIBuilder.Panel("Portrait", root.transform, UIBuilder.WindowColor, false);
        UIBuilder.Place(portrait.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-610f, 0f), new Vector2(340f, 720f));
        UIPortrait.Build(portrait.transform, "UI/player_portrait");

        // เนื้อหากลาง
        Image window = UIBuilder.Panel("Content", root.transform, UIBuilder.WindowColor);
        UIBuilder.Place(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30f, 0f), new Vector2(780f, 720f));
        content = UIBuilder.Stretch(UIBuilder.Rect("Inner", window.transform), 30f, 30f, 30f, 30f);

        // เมนูขวา (แนวตั้ง มีเส้นคั่นซ้าย)
        Image nav = UIBuilder.Panel("Nav", root.transform, UIBuilder.WindowColor);
        UIBuilder.Place(nav.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(530f, 0f), new Vector2(300f, 720f));

        Image divider = UIBuilder.Panel("Divider", nav.transform, new Color(1f, 1f, 1f, 0.5f), false);
        divider.rectTransform.anchorMin = new Vector2(0f, 0f);
        divider.rectTransform.anchorMax = new Vector2(0f, 1f);
        divider.rectTransform.offsetMin = Vector2.zero;
        divider.rectTransform.offsetMax = new Vector2(2f, 0f);

        RectTransform list = UIBuilder.Stretch(UIBuilder.Rect("List", nav.transform), 14f, 20f, 14f, 20f);
        UIBuilder.Vertical(list, 6f);

        for (int i = 0; i < PageNames.Length; i++)
        {
            Page page = (Page)i;
            navButtons[i] = UIBuilder.Button(PageNames[i], list, PageNames[i], () => SelectPage(page), 30f, new Color(1f, 1f, 1f, 0.02f));
            UIBuilder.LabelOf(navButtons[i]).alignment = TextAlignmentOptions.MidlineLeft;
            UIBuilder.Size(navButtons[i], -1f, 70f);
        }

        TextMeshProUGUI hint = UIBuilder.Text("Hint", nav.transform, "Esc to resume", 18f, TextAlignmentOptions.Bottom, UIBuilder.MutedText);
        UIBuilder.Stretch(hint.rectTransform, 0f, 16f, 0f, 0f);

        // กล่องอธิบาย (Tooltip) สี่เหลี่ยมเล็กด้านขวาของเนื้อหา
        tooltip = UIBuilder.Panel("Tooltip", window.transform, new Color(0.13f, 0.15f, 0.2f, 1f), false);
        UIBuilder.Place(tooltip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(330f, 260f));
        tooltipText = UIBuilder.Text("Text", tooltip.transform, string.Empty, 20f, TextAlignmentOptions.TopLeft);
        tooltipText.richText = true;
        UIBuilder.Stretch(tooltipText.rectTransform, 14f, 12f, 14f, 12f);
        tooltip.gameObject.SetActive(false);

        root.SetActive(false);
    }

    private void SelectPage(Page page)
    {
        currentPage = page;

        for (int i = 0; i < navButtons.Length; i++)
        {
            bool selected = i == (int)page;
            navButtons[i].targetGraphic.color = selected ? new Color(0.25f, 0.7f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.02f);
            UIBuilder.LabelOf(navButtons[i]).fontStyle = selected ? FontStyles.Underline | FontStyles.Bold : FontStyles.Normal;
        }

        Rebuild();
    }

    private void RefreshIfOpen()
    {
        if (isOpen)
            Rebuild();
    }

    private void Rebuild()
    {
        HideTooltip();
        UIBuilder.Clear(content);

        switch (currentPage)
        {
            case Page.Equipment: BuildEquipment(); break;
            case Page.Inventory: BuildInventory(); break;
            case Page.Setting: BuildSetting(); break;
            case Page.Exit: BuildExit(); break;
        }
    }

    private void ShowTooltip(string title, string body)
    {
        tooltipText.text = $"<b>{title}</b>\n<size=18>{body}</size>";
        tooltip.gameObject.SetActive(true);
        tooltip.transform.SetAsLastSibling();
    }

    private void HideTooltip()
    {
        if (tooltip != null)
            tooltip.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------ Equipment

    private void BuildEquipment()
    {
        AddHeader("Equipment");

        AddEquipmentSlot("primary", HeldSlot.Primary, -150f,
            "Primary weapon", "Large / heavy weapons only (rifles, sniper rifles).");
        AddEquipmentSlot("second", HeldSlot.Secondary, -300f,
            "Secondary weapon", "Sidearms: pistols, throwables, knives.");
        AddEquipmentSlot("X-GEAR", HeldSlot.XGear, -450f, "X-Gear", string.Empty);

        TextMeshProUGUI hint = UIBuilder.Text("Hint", content, "Hover for details  •  Right click to unequip (goes to Inventory)", 18f,
            TextAlignmentOptions.BottomLeft, UIBuilder.MutedText);
        UIBuilder.Stretch(hint.rectTransform);
    }

    private void AddEquipmentSlot(string label, HeldSlot slot, float y, string typeName, string typeDescription)
    {
        GameObject item = pickup != null ? pickup.GetItem(slot) : null;
        bool isGear = slot == HeldSlot.XGear;

        string itemName = isGear ? "Always equipped" : item != null ? PickupSystem.DisplayNameOf(item) : "- empty -";

        Image box = UIBuilder.Panel(label, content, UIBuilder.PanelColor);
        UIBuilder.Place(box.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y + 70f), new Vector2(340f, 120f));

        Image outline = UIBuilder.Panel("Accent", box.transform, isGear ? UIBuilder.AccentColor : new Color(1f, 1f, 1f, 0.5f), false);
        outline.rectTransform.anchorMin = new Vector2(0f, 0f);
        outline.rectTransform.anchorMax = new Vector2(0f, 1f);
        outline.rectTransform.offsetMin = Vector2.zero;
        outline.rectTransform.offsetMax = new Vector2(4f, 0f);

        TextMeshProUGUI title = UIBuilder.Text("Title", box.transform, label, 28f, TextAlignmentOptions.TopLeft);
        UIBuilder.Stretch(title.rectTransform, 18f, 10f, 10f, 12f);

        TextMeshProUGUI nameLabel = UIBuilder.Text("Item", box.transform, itemName, 22f, TextAlignmentOptions.BottomLeft,
            item != null || isGear ? UIBuilder.AccentColor : UIBuilder.MutedText);
        UIBuilder.Stretch(nameLabel.rectTransform, 18f, 14f, 10f, 10f);

        UIPointerRelay relay = box.gameObject.AddComponent<UIPointerRelay>();
        relay.onExit = HideTooltip;
        relay.onEnter = () =>
        {
            if (isGear)
            {
                ShowTooltip("X-Gear",
                    "A mechanical arm that goes with you on every mission.\n\n" +
                    "Passive: " + ItemCatalog.NameOf(XGearLoadout.PassiveId) + "\n" +
                    "Active: " + ItemCatalog.NameOf(XGearLoadout.ActiveId) + "\n" +
                    "Map: " + ItemCatalog.NameOf(XGearLoadout.MapId) + "\n\n" +
                    "Change data at the Upgrade Corner in Base.");
            }
            else if (item != null)
            {
                ShowTooltip(PickupSystem.DisplayNameOf(item), PickupSystem.DescribeWeapon(item));
            }
            else
            {
                ShowTooltip(typeName, typeDescription + "\n\nEmpty. Pick up a weapon or equip one from Inventory.");
            }
        };

        if (!isGear && item != null)
        {
            relay.onRightClick = () =>
            {
                if (pickup.UnequipToInventory(slot))
                {
                    HudToast.Show(itemName + " moved to Inventory");
                    Rebuild();
                }
            };
        }
    }

    // ------------------------------------------------------------ Inventory

    private const int GridColumns = 5;
    private const int GridRows = 6;

    private void BuildInventory()
    {
        AddHeader("Inventory");

        RectTransform grid = UIBuilder.Rect("Grid", content);
        UIBuilder.Place(grid, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -70f), new Vector2(380f, 460f));

        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(72f, 72f);
        layout.spacing = new Vector2(4f, 4f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = GridColumns;

        int used = 0;

        // 1) อาวุธที่ถอดเก็บไว้ (คลิกซ้ายเพื่อใส่กลับ)
        if (pickup != null)
        {
            IReadOnlyList<GameObject> stash = pickup.Stash;
            for (int i = 0; i < stash.Count && used < GridColumns * GridRows; i++)
            {
                GameObject weapon = stash[i];
                if (weapon == null)
                    continue;

                string weaponName = PickupSystem.DisplayNameOf(weapon);
                AddCell(grid, weaponName, "W", new Color(1f, 0.6f, 0.3f), null, weaponName,
                    PickupSystem.DescribeWeapon(weapon) + "\n\nLeft click to equip.",
                    () =>
                    {
                        if (pickup.EquipFromStash(weapon))
                            Rebuild();
                    });
                used++;
            }
        }

        // 2) ไอเทมตามลำดับใน ItemCatalog: ของถาวร + ของที่เก็บในรอบนี้ (ยังไม่ถาวรจนกว่าจะจบด่าน)
        PersistentInventory persistent = PersistentInventory.Instance;
        RunInventory run = RunInventory.Instance;
        IReadOnlyList<ItemDef> all = ItemCatalog.All;

        // ให้ id ที่ไม่อยู่ใน Catalog (ResourceData ใหม่) ถูกลงทะเบียนก่อนวน
        if (persistent != null)
            foreach (KeyValuePair<string, int> pair in persistent.Snapshot()) ItemCatalog.Get(pair.Key);
        if (run != null)
            foreach (KeyValuePair<string, int> pair in run.Snapshot()) ItemCatalog.Get(pair.Key);

        for (int i = 0; i < all.Count && used < GridColumns * GridRows; i++)
        {
            ItemDef def = all[i];
            int owned = persistent != null ? persistent.GetAmount(def.id) : 0;
            int pending = run != null ? run.GetAmount(def.id) : 0;

            if (owned + pending <= 0)
                continue;

            string amount = pending > 0 ? $"{owned}<color=#7CFF8A>+{pending}</color>" : owned.ToString();
            string body = def.description + $"\n\nOwned: {owned}" +
                          (pending > 0 ? $"\nThis run: +{pending} (kept when you finish the mission)" : string.Empty);

            AddCell(grid, amount, ShortLabel(def), def.color, ItemCatalog.IconOf(def.id), def.displayName, body, null);
            used++;
        }

        // 3) ช่องว่างที่เหลือ ให้เห็นเป็นตาราง
        for (; used < GridColumns * GridRows; used++)
            UIBuilder.Panel("Empty", grid, new Color(1f, 1f, 1f, 0.04f), false);

        TextMeshProUGUI hint = UIBuilder.Text("Hint", content, "Hover for details  •  Left click a weapon to equip", 18f,
            TextAlignmentOptions.BottomLeft, UIBuilder.MutedText);
        UIBuilder.Stretch(hint.rectTransform);
    }

    private static string ShortLabel(ItemDef def)
    {
        switch (def.category)
        {
            case ItemCategory.Ammo: return def.displayName.Replace(" Ammo", string.Empty);
            case ItemCategory.PassiveChip: return "P-Chip";
            case ItemCategory.ActiveChip: return "A-Chip";
            case ItemCategory.MapData: return "Map";
            default: return def.displayName;
        }
    }

    private void AddCell(Transform grid, string amount, string label, Color color, Sprite icon,
        string tooltipTitle, string tooltipBody, System.Action onClick)
    {
        Image cell = UIBuilder.Panel("Cell", grid, new Color(1f, 1f, 1f, 0.10f));

        if (icon != null)
        {
            Image iconImage = UIBuilder.Panel("Icon", cell.transform, Color.white, false);
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            UIBuilder.Stretch(iconImage.rectTransform, 8f, 16f, 8f, 6f);
        }
        else
        {
            TextMeshProUGUI name = UIBuilder.Text("Label", cell.transform, label, 15f, TextAlignmentOptions.Center, color);
            UIBuilder.Stretch(name.rectTransform, 2f, 16f, 2f, 2f);
        }

        TextMeshProUGUI count = UIBuilder.Text("Amount", cell.transform, amount, 15f, TextAlignmentOptions.BottomRight);
        count.richText = true;
        count.textWrappingMode = TextWrappingModes.NoWrap;
        count.overflowMode = TextOverflowModes.Ellipsis;
        UIBuilder.Stretch(count.rectTransform, 2f, 1f, 4f, 2f);

        UIPointerRelay relay = cell.gameObject.AddComponent<UIPointerRelay>();
        relay.onEnter = () => ShowTooltip(tooltipTitle, tooltipBody);
        relay.onExit = HideTooltip;

        if (onClick != null)
        {
            Button button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = cell;
            button.onClick.AddListener(() => onClick());
            relay.playSounds = true;
        }
    }

    // ------------------------------------------------------------ Setting

    private void BuildSetting()
    {
        AddHeader("Setting");

        // Fullscreen: ปุ่มที่ทำหน้าที่เป็น Checkbox
        TextMeshProUGUI fullscreenLabel = UIBuilder.Text("FullscreenLabel", content, "FullScreen", 26f);
        UIBuilder.Place(fullscreenLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -90f), new Vector2(220f, 44f));

        Button check = null;
        check = UIBuilder.Button("FullscreenCheck", content, Screen.fullScreen ? "X" : string.Empty, () =>
        {
            bool value = !Screen.fullScreen;
            Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.fullScreen = value;
            UIBuilder.LabelOf(check).text = value ? "X" : string.Empty;
        }, 26f, new Color(1f, 1f, 1f, 0.18f));
        UIBuilder.Place((RectTransform)check.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(240f, -90f), new Vector2(44f, 44f));

        TextMeshProUGUI soundHeader = UIBuilder.Text("SoundHeader", content, "Sound", 26f);
        UIBuilder.Place(soundHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -170f), new Vector2(220f, 44f));

        AudioManager audio = AudioManager.Instance;

        if (audio != null)
        {
            AddVolumeRow("Main", -230f, audio.MasterVolume, audio.SetMasterVolume);
            AddVolumeRow("Music", -290f, audio.MusicVolume, audio.SetMusicVolume);
            AddVolumeRow("SFX", -350f, audio.SfxVolume, audio.SetSfxVolume);
        }
        else
        {
            // เริ่ม Play จากด่านโดยตรง (ไม่มี AudioManager จาก MainMenu): คุมได้แค่เสียงรวม
            AddVolumeRow("Main", -230f, AudioListener.volume, value => AudioListener.volume = value);

            TextMeshProUGUI note = UIBuilder.Text("Note", content,
                "Music / SFX sliders need the AudioManager\n(start the game from MainMenu).", 18f,
                TextAlignmentOptions.TopLeft, UIBuilder.MutedText);
            UIBuilder.Place(note.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -290f), new Vector2(560f, 60f));
        }
    }

    private void AddVolumeRow(string label, float y, float value, System.Action<float> onChanged)
    {
        TextMeshProUGUI text = UIBuilder.Text(label, content, label, 24f);
        UIBuilder.Place(text.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(140f, 40f));

        Slider slider = UIBuilder.Slider(label + "Slider", content, value, onChanged);
        UIBuilder.Place((RectTransform)slider.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, y), new Vector2(380f, 40f));
    }

    // ------------------------------------------------------------ Exit

    private void BuildExit()
    {
        TextMeshProUGUI question = UIBuilder.Text("Question", content, "exit game?", 34f, TextAlignmentOptions.Center);
        UIBuilder.Place(question.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(600f, 60f));

        TextMeshProUGUI note = UIBuilder.Text("Note", content, "Return to the main menu. Items collected this run will be lost.", 18f,
            TextAlignmentOptions.Center, UIBuilder.MutedText);
        UIBuilder.Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -205f), new Vector2(640f, 40f));

        Button yes = UIBuilder.Button("Yes", content, "YES", ExitToMainMenu, 28f, new Color(0.8f, 0.2f, 0.2f, 0.6f));
        UIBuilder.Place((RectTransform)yes.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -270f), new Vector2(300f, 64f));

        Button no = UIBuilder.Button("No", content, "NO", () => SelectPage(Page.Equipment), 28f);
        UIBuilder.Place((RectTransform)no.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -350f), new Vector2(300f, 64f));
    }

    private void ExitToMainMenu()
    {
        Close(); // คืน timeScale ก่อนเปลี่ยน Scene

        if (LevelManager.Instance != null)
            LevelManager.Instance.LoadMainMenu();
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
    }

    private void AddHeader(string title)
    {
        TextMeshProUGUI header = UIBuilder.Text("Header", content, title, 34f, TextAlignmentOptions.TopLeft);
        UIBuilder.Stretch(header.rectTransform);
    }
}
