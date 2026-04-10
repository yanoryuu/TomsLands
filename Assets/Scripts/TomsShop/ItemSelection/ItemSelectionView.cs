using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using R3;
using TMPro;

public class ItemSelectionView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform itemListParent;                   // スロットを並べる親
    [SerializeField] private GameObject itemSelectionSlotPrefab;         // スロットプレハブ
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject itemSelectionPanel;          // アイテム選択パネル
    [SerializeField] private Button armorPanelButton;
    [SerializeField] private Button weaponPanelButton;
    [SerializeField] private TextMeshProUGUI itemDescriptionText;
    [SerializeField] private Image itemIcon;

    [SerializeField] private GameObject weaponTab;
    [SerializeField] private GameObject armorTab;
    [SerializeField] private GameObject toolTab;
    public Subject<Unit> OnCloseRequested { get; } = new();
    private readonly List<GameObject> activeSlots = new();
    public Subject<Unit> OnArmorPanelRequested { get; private set; } = new();
    public Subject<Unit> OnWeaponPanelRequested { get; private set; } = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() =>OnCloseRequested.OnNext(Unit.Default));
        armorPanelButton.onClick.AddListener(() => OnArmorPanelRequested.OnNext(Unit.Default));
        weaponPanelButton.onClick.AddListener(() => OnWeaponPanelRequested.OnNext(Unit.Default));
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
        return slots;
    }

    public void SortItemTab(ItemTypeData.ItemType type)
    {
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
}
