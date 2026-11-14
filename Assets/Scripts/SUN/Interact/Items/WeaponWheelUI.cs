using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// แถบเลือกอาวุธ: กด Tab ค้าง → ขึ้น 4 แท็บคงที่ (Unarmed / Primary / Secondary / X-Gear)
/// เลื่อนลูกกลิ้งเพื่อเปลี่ยน ปล่อย Tab เพื่อยืนยัน
/// ทุกครั้งที่เลื่อน: มีเสียง + แท็บที่เลือกขยายและเปลี่ยนสี / ยืนยันแล้ว: มีเสียง + ป้ายชื่ออาวุธกลางจอ
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
    public Color normalColor = new Color(0f, 0f, 0f, 0.6f);
    public Color selectedColor = new Color(0.25f, 0.7f, 1f, 0.95f);
    public Color emptyColor = new Color(0f, 0f, 0f, 0.25f);
    public float selectedScale = 1.18f;

    private static readonly HeldSlot[] Order = { HeldSlot.Unarmed, HeldSlot.Primary, HeldSlot.Secondary, HeldSlot.XGear };
    private static readonly string[] Titles = { "UNARMED", "PRIMARY", "SECONDARY", "X-GEAR" };

    private InputAction wheelAction;
    private InputAction scrollAction;
    private int selectedIndex;
    private bool isOpen;
    private float cachedTimeScale = 1f;

    private GameObject root;
    private readonly Image[] tabBackgrounds = new Image[4];
    private readonly TextMeshProUGUI[] tabNames = new TextMeshProUGUI[4];
    private readonly RectTransform[] tabRects = new RectTransform[4];

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

    void OnEnable()
    {
        if (pickupSystem != null)
            pickupSystem.OnHeldSlotChanged += HandleHeldSlotChanged;
    }

    void OnDisable()
    {
        if (pickupSystem != null)
            pickupSystem.OnHeldSlotChanged -= HandleHeldSlotChanged;

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
        if (wheelAction == null || pickupSystem == null)
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
        selectedIndex = System.Array.IndexOf(Order, pickupSystem.CurrentSlot);
        if (selectedIndex < 0) selectedIndex = 0;

        EnsureUI();
        RefreshTabs();
        root.SetActive(true);

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

        // เลื่อนลง = ไปแท็บถัดไป (ขวา) ข้ามช่องที่ยังไม่มีอาวุธ
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
        RefreshTabs();
        SfxLibrary.PlayUI("wheel_tick", true);
    }

    void CloseAndEquip()
    {
        isOpen = false;

        if (root != null)
            root.SetActive(false);

        Time.timeScale = cachedTimeScale;

        if (pickupSystem.CanChangeWeapon)
            pickupSystem.Select(Order[selectedIndex]);
    }

    // เปลี่ยนช่องที่ถือ (จากแถบนี้ ปุ่มลัด หรือเก็บอาวุธใหม่): เสียง + ป้ายบอกกลางจอ
    void HandleHeldSlotChanged(HeldSlot slot)
    {
        int index = System.Array.IndexOf(Order, slot);
        string title = index >= 0 ? Titles[index] : slot.ToString();

        SfxLibrary.PlayUI(slot == HeldSlot.Unarmed ? "weapon_unequip" : "weapon_switch");
        HudToast.Show(slot == HeldSlot.Unarmed ? title : title + "  —  " + NameOfSlot(slot), 1.2f);
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

        Canvas canvas = UIBuilder.CreateCanvas("WeaponTabs (Auto)", 60);
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        root = canvas.gameObject;

        RectTransform row = UIBuilder.Rect("Row", root.transform);
        UIBuilder.Place(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1040f, 150f));

        const float width = 240f;
        const float gap = 20f;
        float startX = -((width + gap) * (Order.Length - 1)) * 0.5f;

        for (int i = 0; i < Order.Length; i++)
        {
            Image background = UIBuilder.Panel(Titles[i], row, normalColor, false);
            UIBuilder.Place(background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(startX + i * (width + gap), 0f), new Vector2(width, 120f));

            TextMeshProUGUI title = UIBuilder.Text("Title", background.transform, Titles[i], 20f, TextAlignmentOptions.Top, UIBuilder.MutedText);
            UIBuilder.Stretch(title.rectTransform, 8f, 8f, 8f, 12f);

            TextMeshProUGUI itemName = UIBuilder.Text("Name", background.transform, string.Empty, 26f, TextAlignmentOptions.Bottom);
            UIBuilder.Stretch(itemName.rectTransform, 8f, 18f, 8f, 8f);

            tabBackgrounds[i] = background;
            tabNames[i] = itemName;
            tabRects[i] = background.rectTransform;
        }

        TextMeshProUGUI hint = UIBuilder.Text("Hint", root.transform, "Scroll to change  •  Release Tab to confirm", 20f,
            TextAlignmentOptions.Center, UIBuilder.MutedText);
        UIBuilder.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(800f, 32f));
    }

    void RefreshTabs()
    {
        for (int i = 0; i < Order.Length; i++)
        {
            bool available = pickupSystem.IsSlotAvailable(Order[i]);
            bool selected = i == selectedIndex;

            tabBackgrounds[i].color = selected ? selectedColor : available ? normalColor : emptyColor;
            tabRects[i].localScale = selected ? Vector3.one * selectedScale : Vector3.one;
            tabNames[i].text = NameOfSlot(Order[i]);
            tabNames[i].color = available ? UIBuilder.TextColor : UIBuilder.MutedText;
        }
    }
}
