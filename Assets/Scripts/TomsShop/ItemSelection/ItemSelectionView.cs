using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using R3;
using TMPro;

public class ItemSelectionView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform itemListParent;
    [SerializeField] private GameObject itemSelectionSlotPrefab;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject itemSelectionPanel;
    [SerializeField] private Button armorPanelButton;
    [SerializeField] private Button weaponPanelButton;
    /// <summary>おすすめ陳列（期待収益の高い順に枠いっぱい並べる）。</summary>
    [SerializeField] private Button autoDisplayButton;
    [SerializeField] private TextMeshProUGUI itemDescriptionText;
    [SerializeField] private Image itemIcon;

    [Header("陳列パネル")]
    [SerializeField] private Transform displayListParent;
    [SerializeField] private GameObject itemDisplaySlotPrefab;

    [Header("陳列枠カウンタ（店レベル） ※未配線でも動作する")]
    [SerializeField] private TextMeshProUGUI slotCounterText;

    [SerializeField] private GameObject weaponTab;
    [SerializeField] private GameObject armorTab;
    [SerializeField] private GameObject toolTab;
    public Subject<Unit> OnCloseRequested { get; } = new();
    private readonly List<GameObject> activeSlots = new();
    public Subject<Unit> OnArmorPanelRequested { get; private set; } = new();
    public Subject<Unit> OnWeaponPanelRequested { get; private set; } = new();
    public Subject<Unit> OnAutoDisplayRequested { get; private set; } = new();

    private readonly Dictionary<string, ItemDisplaySlot> displaySlots = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        armorPanelButton.onClick.AddListener(() => OnArmorPanelRequested.OnNext(Unit.Default));
        weaponPanelButton.onClick.AddListener(() => OnWeaponPanelRequested.OnNext(Unit.Default));

        // 未配線でも落ちないようにする（他のボタンと違い、後から足した任意要素のため）
        if (autoDisplayButton != null)
            autoDisplayButton.onClick.AddListener(() => OnAutoDisplayRequested.OnNext(Unit.Default));

        UpdateHeaderNavigation();

        // パッド操作: LB/RB で武器/防具タブを切り替える（スコープはプレハブ側に設定済み）
        focusScope = itemSelectionPanel != null ? itemSelectionPanel.GetComponent<UIFocusScope>() : null;
        if (focusScope != null)
        {
            focusScope.OnButton
                .Where(b => b == ControllerButton.LB || b == ControllerButton.RB)
                .Subscribe(b => CycleTab(b == ControllerButton.RB ? 1 : -1))
                .AddTo(this);
        }
    }

    private UIFocusScope focusScope;
    private Button currentTabButton;
    private List<ItemSelectionSlot> lastSlots = new();

    /// <summary>表示中のタブボタンを左右に巡回して押す（LB/RB）。</summary>
    private void CycleTab(int dir)
    {
        var tabs = new List<Button>();
        if (weaponPanelButton != null && weaponPanelButton.gameObject.activeInHierarchy) tabs.Add(weaponPanelButton);
        if (armorPanelButton != null && armorPanelButton.gameObject.activeInHierarchy) tabs.Add(armorPanelButton);
        if (tabs.Count < 2) return;
        int index = Mathf.Max(0, tabs.IndexOf(currentTabButton));
        int next = (index + dir + tabs.Count) % tabs.Count;
        tabs[next].onClick.Invoke();
    }

    /// <summary>
    /// 一覧の先頭スロットをパッドの初期フォーカスにする。
    /// タブボタン等を操作中（選択がスロット以外の操作要素）なら選択は動かさない。
    /// </summary>
    private void FocusFirstSlot(List<ItemSelectionSlot> slots)
    {
        if (focusScope == null) return;
        var first = slots.Count > 0 && slots[0] != null ? slots[0].DefaultSelectable : null;
        focusScope.FirstSelected = first != null ? first : weaponPanelButton;

        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        bool keep = sel != null && UIFocusManager.IsUsable(sel)
            && (sel == weaponPanelButton?.gameObject || sel == armorPanelButton?.gameObject || sel == autoDisplayButton?.gameObject);
        if (!keep && focusScope.FirstSelected != null) focusScope.Select(focusScope.FirstSelected);
    }

    public void SetDescription(string description, Sprite icon)
    {
        itemDescriptionText.text = description;
        itemIcon.sprite = icon;
    }
    
    public void Initialize()
    {
        Hide();
    }

    public void Show()
    {
        itemSelectionPanel.SetActive(true);
    }

    public void Hide()
    {
        itemSelectionPanel.SetActive(false);
    }

    /// <summary>
    /// 商品リストを表示
    /// </summary>
    public List<ItemSelectionSlot> PopulateItemList(List<RuntimeItemData> runtimeItems)
    {
        // 既存スロットを削除（Content配下の全子オブジェクトを削除）
        for (int i = itemListParent.childCount - 1; i >= 0; i--)
        {
            Destroy(itemListParent.GetChild(i).gameObject);
        }
        activeSlots.Clear();

        List<ItemSelectionSlot> slots = new();        
        
        foreach (var item in runtimeItems)
        {
            var slotObj = Instantiate(itemSelectionSlotPrefab, itemListParent);
            var slot = slotObj.GetComponent<ItemSelectionSlot>();
            slot.SetItem(
                item.ItemId,
                item.ItemIcon,
                item.ItemBackground,
                item.ItemName,
                item.CurrentPrice.Value,
                item.Stock.Value
            );
            slots.Add(slot);
            activeSlots.Add(slotObj);
        }
        lastSlots = slots;
        return slots;
    }

    public void SortItemTab(ItemTypeData.ItemType type)
    {
        if (type == ItemTypeData.ItemType.Weapon) currentTabButton = weaponPanelButton;
        else if (type == ItemTypeData.ItemType.Armor) currentTabButton = armorPanelButton;
        // 一覧の購読が済んだ後（説明表示の結線後）にフォーカスする
        FocusFirstSlot(lastSlots);

        switch (type)
        {
            case ItemTypeData.ItemType.Weapon:
                weaponTab.transform.SetAsLastSibling();
                break;
            case ItemTypeData.ItemType.Armor:
                armorTab.transform.SetAsLastSibling();
                break;
            case ItemTypeData.ItemType.Tool:
                toolTab.transform.SetAsLastSibling();
                break;
        }
    }

    public ItemDisplaySlot EnsureDisplaySlot(string itemId, Sprite icon, int quantity)
    {
        if (displaySlots.TryGetValue(itemId, out var existing))
            return existing;

        var slotObj = Instantiate(itemDisplaySlotPrefab, displayListParent);
        var slot = slotObj.GetComponent<ItemDisplaySlot>();
        slot.SetItem(itemId, icon, quantity);
        displaySlots[itemId] = slot;
        UpdateHeaderNavigation();
        return slot;
    }

    public void RemoveDisplaySlot(string itemId)
    {
        if (!displaySlots.TryGetValue(itemId, out var slot)) return;
        Destroy(slot.gameObject);
        displaySlots.Remove(itemId);
        UpdateHeaderNavigation();
    }

    public void ClearDisplaySlots()
    {
        foreach (var slot in displaySlots.Values)
            Destroy(slot.gameObject);
        displaySlots.Clear();
        UpdateHeaderNavigation();
    }

    /// <summary>
    /// パッド操作: 右上の「オート陳列」「×」は自動ナビだと背後のヒント欄に吸われて到達できないため、
    /// 明示ナビにする（下 = 陳列中リストの先頭）。
    /// </summary>
    private void UpdateHeaderNavigation()
    {
        Selectable firstDisplayed = null;
        int bestIndex = int.MaxValue;
        foreach (var slot in displaySlots.Values)
        {
            if (slot == null) continue;
            var s = slot.GetComponentInChildren<Selectable>();
            int index = slot.transform.GetSiblingIndex();
            if (s != null && index < bestIndex) { bestIndex = index; firstDisplayed = s; }
        }

        if (autoDisplayButton != null)
        {
            var nav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = armorPanelButton,
                selectOnRight = closeButton,
                selectOnDown = firstDisplayed,
            };
            autoDisplayButton.navigation = nav;
        }
        if (closeButton != null)
        {
            var nav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = autoDisplayButton != null ? autoDisplayButton : armorPanelButton,
                selectOnDown = firstDisplayed,
            };
            closeButton.navigation = nav;
        }
    }

    /// <summary>「陳列 3/5」の枠カウンタ表示。未配線（null）なら何もしない。</summary>
    public void UpdateSlotCounter(int used, int max)
    {
        if (slotCounterText == null) return;
        slotCounterText.text = $"陳列 {used}/{max}";
        slotCounterText.color = used >= max ? new Color(1f, 0.55f, 0.35f) : Color.white;
    }

    /// <summary>陳列枠が足りない時の通知。カウンタを強調表示する。</summary>
    public void NotifySlotLimitReached(int used, int max)
    {
        UpdateSlotCounter(used, max);
        Debug.Log($"[ItemSelection] 陳列枠が足りない（{used}/{max}）。店を改装すると枠が増える。");
    }
}
