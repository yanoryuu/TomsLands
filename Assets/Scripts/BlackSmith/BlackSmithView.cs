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
    public Subject<Unit> OnCloseRequested { get; private set; } = new();
    public Subject<BlackSmithTab> OnChangePanel { get; private set; } = new();
    
    private readonly List<ItemShopSlot> activeSlots = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() =>OnCloseRequested.OnNext(Unit.Default));
        weaponButton.onClick.AddListener(()=>OnChangePanel.OnNext(BlackSmithTab.Weapon));
        armorButton.onClick.AddListener(()=>OnChangePanel.OnNext(BlackSmithTab.Armor));
        developButton.onClick.AddListener(() => OnChangePanel.OnNext(BlackSmithTab.Development));
        specialWeaponButton.onClick.AddListener(() => OnChangePanel.OnNext(BlackSmithTab.Special));
    }
    
    /// <summary>
    /// 商品リストを表示
    /// </summary>
    public List<ItemShopSlot> PopulateItemList(List<RuntimeItemData> runtimeItems)
    {
        foreach (var slot in activeSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject); 
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
                var weaponPos = weaponTab.transform.localPosition;
                weaponTab.transform.localPosition = new Vector3(weaponPos.x,weaponPos.y+13,weaponPos.z); 
                break;
            case BlackSmithTab.Armor:
                var armorPos = armorTab.transform.localPosition;
                armorTab.transform.localPosition = new Vector3(armorPos.x,armorPos.y+13,armorPos.z);
                break;
            case BlackSmithTab.Development:
                var developPos = developmentTab.transform.localPosition;
                developmentTab.transform.localPosition = new Vector3(developPos.x,developPos.y+13,developPos.z);
                break;
            case BlackSmithTab.Special:
                var specialPos = specialTab.transform.localPosition;
                specialTab.transform.localPosition = new Vector3(specialPos.x,specialPos.y+13,specialPos.z);
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