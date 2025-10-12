using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using R3;
using TMPro;

public class BlackSmithView : MonoBehaviour
{
    [SerializeField] private GameObject blackSmithContent;

    [SerializeField] private GameObject weaponTab;
    [SerializeField] private GameObject armorTab;
    [SerializeField] private GameObject developmentTab;
    [SerializeField] private GameObject specialTab;
    
    [SerializeField] private GameObject itemShopSlotPrefab;
    [SerializeField] private Button closeButton;

    [SerializeField] private Button weaponButton;
    [SerializeField] private Button armorButton;
    [SerializeField] private Button developButton;
    [SerializeField] private Button specialWeaponButton;
    
    [SerializeField] private TextMeshProUGUI itemDescriptionText;

    public Subject<(string itemId, int quantity)> OnPurchaseRequested { get; } = new();
    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnWeaponPanelRequested { get; } = new();
    public Subject<Unit> OnArmorPanelRequested { get; } = new();
    
    public Subject<Unit> OnDevelopRequested { get; } = new();
    
    public Subject<Unit> OnSpecialRequested { get; } = new();
    
    private readonly List<ItemShopSlot> activeSlots = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() =>OnCloseRequested.OnNext(Unit.Default));
        weaponButton.onClick.AddListener(()=>OnWeaponPanelRequested.OnNext(Unit.Default));
        armorButton.onClick.AddListener(()=>OnArmorPanelRequested.OnNext(Unit.Default));
        developButton.onClick.AddListener(() => OnDevelopRequested.OnNext(Unit.Default));
        specialWeaponButton.onClick.AddListener(() => OnSpecialRequested.OnNext(Unit.Default));
    }
    
    /// <summary>
    /// 商品リストを表示
    /// </summary>
    public List<ItemShopSlot> PopulateItemList(List<RuntimeItemData> runtimeItems)
    {
        // 既存スロットを削除
        foreach (var slotObj in activeSlots)
        {
            Destroy(slotObj);
        }
        activeSlots.Clear();

        List<ItemShopSlot> slots = new();        
        
        foreach (var item in runtimeItems)
        {
            var slotObj = Instantiate(itemShopSlotPrefab,blackSmithContent.transform);
            var slot = slotObj.GetComponent<ItemShopSlot>();
            slot.SetItem(
                item.ItemId,
                item.ItemIcon,
                item.CurrentPrice.Value,
                item.MaxStock.Value,
                item.Stock.Value,
                item.IsPopular.Value
            );
            slots.Add(slot);
            activeSlots.Add(slot);
        }
        return slots;
    }
    
    public void SetDescription(string description)
    {
        itemDescriptionText.text = description;
    }
    
    public void SortItemTab(BlackSmithTab type)
    {
        switch (type)
        {
            case BlackSmithTab.Weapon:
                weaponTab.transform.SetAsLastSibling();
                break;
            case BlackSmithTab.Armor:
                armorTab.transform.SetAsLastSibling();
                break;
            case BlackSmithTab.Development:
                developmentTab.transform.SetAsLastSibling();
                break;
            case BlackSmithTab.Special:
                specialTab.transform.SetAsLastSibling();
                break;
        }
    }
}

public enum BlackSmithTab{
    Weapon,
    Armor,
    Development,
    Special
}