using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// HUD อาวุธมุมขวาล่าง (แนวตั้ง) ใช้แทนทั้ง "แถบเลือกอาวุธกลางจอ" และ "กล่อง WeaponHUD เดิม":
///   - ปกติ: โชว์แถวเดียว = ช่องที่ถืออยู่ (ชื่อช่อง, ชื่ออาวุธ, กระสุนในแม็ก / กระสุนสำรอง)
///   - กด Tab ค้าง: กางเป็น 4 แถวเรียงบนลงล่าง Unarmed / Primary / Secondary / X-Gear
///     เลื่อนลูกกลิ้งเพื่อเปลี่ยน (มีเสียง + แถวที่เลือกเปลี่ยนสีและยื่นออกมา) ปล่อย Tab เพื่อยืนยัน
/// UI สร้างด้วยโค้ด (ช่อง Wheel Panel / Slot เดิมไม่ใช้แล้ว เก็บไว้เพื่อไม่ให้ค่าใน Scene เดิมหาย)
/// </summary>
public class WeaponWheelUI : MonoBehaviour
{
    [Header("References")]
    public PickupSystem pickupSystem;
    public PlayerInput playerInput;

    [Header("UI เดิม (ไม่ใช้แล้ว จะถูกซ่อน)")]
    public GameObject wheelPanel;
    public Transform slotContainer;
    public WeaponSlotUI slotPrefab;

    [Header("Behaviour")]
    [Tooltip("ชะลอเวลาตอนเปิดแถบ (1 = ปกติ, 0.2 = slow motion)")]
    [Range(0.05f, 1f)] public float timeScaleWhileOpen = 0.2f;
    [Tooltip("เก็บไว้เพื่อความเข้ากันได้ (แท็บ Unarmed มีเสมอ)")]
    public bool allowEmptySlot = true;
    [Tooltip("ค่า scroll ขั้นต่ำที่นับเป็น 1 คลิก")]
    public float scrollThreshold = 0.1f;

    [Header("Look")]
    public Color normalColor = new Color(0f, 0f, 0f, 0.7f);
    public Color selectedColor = new Color(0.25f, 0.7f, 1f, 0.95f);
    public Color emptyColor = new Color(0f, 0f, 0f, 0.35f);
    [Tooltip("สีพื้นหลังของแถวตอนไม่ได้กด Tab")]
    public Color idleColor = new Color(0f, 0f, 0f, 0.45f);
    [Tooltip("แถวที่เลือกยื่นออกมาทางซ้ายกี่พิกเซล ตอนกด Tab")]
    public float selectedShift = 28f;

    [Header("Layout (อ้างอิงจอ 1920x1080)")]
    public Vector2 screenMargin = new Vector2(40f, 40f);
    public Vector2 rowSize = new Vector2(360f, 68f);
    public float rowGap = 8f;

    private static readonly HeldSlot[] Order = { HeldSlot.Unarmed, HeldSlot.Primary, HeldSlot.Secondary, HeldSlot.XGear };
    private static readonly string[] Titles = { "UNARMED", "PRIMARY", "SECONDARY", "X-GEAR" };

    private InputAction wheelAction;
    private InputAction scrollAction;
    private int selectedIndex;
    private bool isOpen;
    private float cachedTimeScale = 1f;

    private GameObject root;
    private GameObject hint;
    private readonly Image[] rowBackgrounds = new Image[4];
    private readonly Image[] rowAccents = new Image[4];
    private readonly Image[] rowIcons = new Image[4];
    private readonly TextMeshProUGUI[] rowTitles = new TextMeshProUGUI[4];
    private readonly TextMeshProUGUI[] rowNames = new TextMeshProUGUI[4];
    private readonly TextMeshProUGUI[] rowAmmo = new TextMeshProUGUI[4];
    private readonly RectTransform[] rowRects = new RectTransform[4];

    // กระสุนของปืนที่ถืออยู่: จำค่าล่าสุดไว้ แล้วแก้ข้อความเฉพาะตอนค่าเปลี่ยน (ไม่สร้าง string ใหม่ทุกเฟรม)
    private GunAction heldGun;
    private int shownAmmo = -1;
    private int shownReserve = -1;
    private bool shownReloading;

    public bool IsOpen => isOpen;

    void Awake()
    {
        if (pickupSystem == null) pickupSystem = GetComponent<PickupSystem>();
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();

        if (playerInput != null)
        {
            wheelAction = playerInput.actions["WeaponWheel"];
            scrollAction = playerInput.actions["ScrollWeapon"];
        }

        if (wheelPanel != null)
            wheelPanel.SetActive(false);
    }

    void Start()
    {
        // กล่อง WeaponHUD เดิมของ MainHUD: ซ่อนถาวร HUD นี้แสดงแทนทั้งหมด
        AmmoUIManager oldHud = pickupSystem != null && pickupSystem.ammoUIManager != null
            ? pickupSystem.ammoUIManager
            : FindAnyObjectByType<AmmoUIManager>(FindObjectsInactive.Include);

        if (oldHud != null)
            oldHud.SetSuppressed(true);

        EnsureUI();
        RefreshAll();
    }

    void OnEnable()
    {
        if (pickupSystem != null)
        {
            pickupSystem.OnHeldSlotChanged += HandleHeldSlotChanged;
            pickupSystem.OnLoadoutChanged += RefreshAll;
        }

        XGearLoadout.OnChanged += RefreshAll;

        if (root != null)
        {
            root.SetActive(true);
            RefreshAll();
        }
    }

    void OnDisable()
    {
        if (pickupSystem != null)
        {
            pickupSystem.OnHeldSlotChanged -= HandleHeldSlotChanged;
            pickupSystem.OnLoadoutChanged -= RefreshAll;
        }

        XGearLoadout.OnChanged -= RefreshAll;

        // กัน timeScale ค้างตอน disable กลางคัน
        if (isOpen)
        {
            Time.timeScale = cachedTimeScale;
            isOpen = false;
        }

        if (root != null)
            root.SetActive(false);
    }

    void OnDestroy()
    {
        if (root != null)
            Destroy(root);
    }

    void Update()
    {
        if (pickupSystem == null)
            return;

        // โหมด Hub (Base): ไม่มีการใช้อาวุธ → ซ่อนแถบอาวุธทั้งหมด และไม่รับปุ่มเปิดแถบ/ลูกกลิ้งเลือกอาวุธ
        bool blocked = pickupSystem.WeaponsBlocked;
        if (root != null && root.activeSelf == blocked)
            root.SetActive(!blocked);

        if (blocked)
            return;

        UpdateHeldAmmo();

        if (wheelAction == null)
            return;

        if (wheelAction.WasPressedThisFrame() && !isOpen && Time.timeScale > 0f && pickupSystem.CanChangeWeapon)
            Open();

        if (!isOpen)
            return;

        HandleScroll();

        if (!wheelAction.IsPressed())
            CloseAndEquip();
    }

    void Open()
    {
        isOpen = true;
        selectedIndex = IndexOf(pickupSystem.CurrentSlot);

        EnsureUI();
        RefreshAll();

        cachedTimeScale = Time.timeScale;
        Time.timeScale = timeScaleWhileOpen;

        SfxLibrary.PlayUI("wheel_open");
    }

    void HandleScroll()
    {
        if (scrollAction == null)
            return;

        float scroll = scrollAction.ReadValue<float>();
        if (Mathf.Abs(scroll) < scrollThreshold)
            return;

        // เลื่อนลง = ไปแถวถัดไป (ล่าง) ข้ามช่องที่ยังไม่มีอาวุธ
        int direction = scroll > 0f ? -1 : 1;
        int index = selectedIndex;

        for (int i = 0; i < Order.Length; i++)
        {
            index = (index + direction + Order.Length) % Order.Length;
            if (pickupSystem.IsSlotAvailable(Order[index]))
                break;
        }

        if (index == selectedIndex)
            return;

        selectedIndex = index;
        RefreshAll();
        SfxLibrary.PlayUI("wheel_tick", true);
    }

    void CloseAndEquip()
    {
        isOpen = false;
        Time.timeScale = cachedTimeScale;

        if (pickupSystem.CanChangeWeapon)
            pickupSystem.Select(Order[selectedIndex]);

        RefreshAll();
    }

    // เปลี่ยนช่องที่ถือ (จากแถบนี้ ปุ่มลัด หรือเก็บอาวุธใหม่): มีเสียง และแถวมุมขวาล่างเปลี่ยนตาม (ไม่มีป้ายกลางจอแล้ว)
    void HandleHeldSlotChanged(HeldSlot slot)
    {
        SfxLibrary.PlayUI(slot == HeldSlot.Unarmed ? "weapon_unequip" : "weapon_switch");
        RefreshAll();
    }

    static int IndexOf(HeldSlot slot)
    {
        int index = System.Array.IndexOf(Order, slot);
        return index < 0 ? 0 : index;
    }

    string NameOfSlot(HeldSlot slot)
    {
        switch (slot)
        {
            case HeldSlot.Unarmed: return "No weapon";
            case HeldSlot.XGear:
                string active = XGearLoadout.ActiveId;
                return string.IsNullOrEmpty(active) ? "No active chip" : ItemCatalog.NameOf(active);
            default:
                GameObject item = pickupSystem.GetItem(slot);
                return item != null ? PickupSystem.DisplayNameOf(item) : "Empty";
        }
    }

    // ------------------------------------------------------------ UI

    void EnsureUI()
    {
        if (root != null)
            return;

        Canvas canvas = UIBuilder.CreateCanvas("WeaponHUD (Auto)", 60);
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        root = canvas.gameObject;

        Vector2 corner = new Vector2(1f, 0f); // ยึดมุมขวาล่างของจอ

        for (int i = 0; i < Order.Length; i++)
        {
            Image background = UIBuilder.Panel(Titles[i], root.transform, idleColor, false);
            UIBuilder.Place(background.rectTransform, corner, corner, Vector2.zero, rowSize);

            // ขีดสีด้านซ้ายของแถว
            Image accent = UIBuilder.Panel("Accent", background.transform, UIBuilder.AccentColor, false);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.rectTransform.sizeDelta = new Vector2(5f, 0f);

            Image icon = UIBuilder.Panel("Icon", background.transform, Color.white, false);
            icon.preserveAspect = true;
            UIBuilder.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(48f, 48f));

            TextMeshProUGUI title = UIBuilder.Text("Title", background.transform, Titles[i], 15f, TextAlignmentOptions.TopLeft, UIBuilder.MutedText);
            UIBuilder.Stretch(title.rectTransform, 18f, 0f, 110f, 8f);

            TextMeshProUGUI itemName = UIBuilder.Text("Name", background.transform, string.Empty, 24f, TextAlignmentOptions.BottomLeft);
            UIBuilder.Stretch(itemName.rectTransform, 18f, 8f, 110f, 0f);
            itemName.textWrappingMode = TextWrappingModes.NoWrap;
            itemName.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI ammo = UIBuilder.Text("Ammo", background.transform, string.Empty, 26f, TextAlignmentOptions.MidlineRight);
            UIBuilder.Stretch(ammo.rectTransform, 18f, 0f, 16f, 0f);

            rowBackgrounds[i] = background;
            rowAccents[i] = accent;
            rowIcons[i] = icon;
            rowTitles[i] = title;
            rowNames[i] = itemName;
            rowAmmo[i] = ammo;
            rowRects[i] = background.rectTransform;
        }

        TextMeshProUGUI hintText = UIBuilder.Text("Hint", root.transform, "Scroll to change  •  Release Tab to confirm", 16f,
            TextAlignmentOptions.BottomRight, UIBuilder.MutedText);
        float stackHeight = Order.Length * rowSize.y + (Order.Length - 1) * rowGap;
        UIBuilder.Place(hintText.rectTransform, corner, corner,
            new Vector2(-screenMargin.x, screenMargin.y + stackHeight + 8f), new Vector2(rowSize.x + 80f, 24f));
        hint = hintText.gameObject;
    }

    // จัดแถว + ข้อความทั้งหมดใหม่ (เรียกเฉพาะตอนมีอะไรเปลี่ยน ไม่ได้เรียกทุกเฟรม)
    void RefreshAll()
    {
        if (root == null || pickupSystem == null)
            return;

        int heldIndex = IndexOf(pickupSystem.CurrentSlot);
        if (!isOpen)
            selectedIndex = heldIndex;

        hint.SetActive(isOpen);

        for (int i = 0; i < Order.Length; i++)
        {
            HeldSlot slot = Order[i];
            bool available = pickupSystem.IsSlotAvailable(slot);
            bool selected = i == selectedIndex;

            // ปิด: เหลือแถวเดียวชิดมุมล่าง / เปิด: เรียง Unarmed (บนสุด) ลงมาถึง X-Gear (ล่างสุด)
            rowRects[i].gameObject.SetActive(isOpen || i == heldIndex);

            float y = isOpen ? (Order.Length - 1 - i) * (rowSize.y + rowGap) : 0f;
            float x = isOpen && selected ? selectedShift : 0f;
            rowRects[i].anchoredPosition = new Vector2(-screenMargin.x - x, screenMargin.y + y);

            rowBackgrounds[i].color = !isOpen ? idleColor : selected ? selectedColor : available ? normalColor : emptyColor;
            rowAccents[i].enabled = !isOpen || i == heldIndex; // ตอนกางออก ขีดสีบอกว่าช่องไหนคือช่องที่ถืออยู่
            rowAccents[i].color = isOpen && selected ? Color.white : UIBuilder.AccentColor;

            Color textColor = available ? UIBuilder.TextColor : UIBuilder.MutedText;
            rowNames[i].text = NameOfSlot(slot);
            rowNames[i].color = textColor;
            rowTitles[i].color = isOpen && selected ? UIBuilder.TextColor : UIBuilder.MutedText;

            GameObject item = slot == HeldSlot.Primary || slot == HeldSlot.Secondary ? pickupSystem.GetItem(slot) : null;

            // ไอคอนอาวุธ (ถ้า ItemData มี) ข้อความจะขยับหลบไปทางขวา
            WeaponInfo info = item != null ? item.GetComponent<WeaponInfo>() : null;
            Sprite sprite = info != null ? info.Icon : null;
            rowIcons[i].sprite = sprite;
            rowIcons[i].enabled = sprite != null;

            float textLeft = sprite != null ? 76f : 18f;
            rowTitles[i].rectTransform.offsetMin = new Vector2(textLeft, rowTitles[i].rectTransform.offsetMin.y);
            rowNames[i].rectTransform.offsetMin = new Vector2(textLeft, rowNames[i].rectTransform.offsetMin.y);

            GunAction gun = item != null ? item.GetComponent<GunAction>() : null;
            rowAmmo[i].text = gun != null ? AmmoLabel(gun) : string.Empty;
            rowAmmo[i].color = textColor;
        }

        // ปืนที่ถืออยู่: ให้ UpdateHeldAmmo คอยตามค่ากระสุนต่อ
        GameObject held = pickupSystem.HeldItem;
        heldGun = held != null ? held.GetComponent<GunAction>() : null;
        shownAmmo = -1;
    }

    static string AmmoLabel(GunAction gun)
    {
        return gun.IsReloading ? "RELOAD" : gun.CurrentAmmo + " / " + gun.ReserveAmmo;
    }

    void UpdateHeldAmmo()
    {
        if (heldGun == null || root == null)
            return;

        int ammo = heldGun.CurrentAmmo;
        int reserve = heldGun.ReserveAmmo;
        bool reloading = heldGun.IsReloading;

        if (ammo == shownAmmo && reserve == shownReserve && reloading == shownReloading)
            return;

        shownAmmo = ammo;
        shownReserve = reserve;
        shownReloading = reloading;

        TextMeshProUGUI label = rowAmmo[IndexOf(pickupSystem.CurrentSlot)];
        label.text = AmmoLabel(heldGun);

        // แม็กว่าง = ตัวเลขแดง เตือนให้รีโหลด
        label.color = ammo <= 0 && !reloading ? new Color(1f, 0.35f, 0.35f, 1f) : UIBuilder.TextColor;
    }
}
