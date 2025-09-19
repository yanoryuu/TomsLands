using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using R3;

public class StreamingView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stealthCostText;
    [SerializeField] private TextMeshProUGUI stealthCooldownText;
    
    [SerializeField] private List<TextMeshProUGUI> selectedItemsPriceTexts = new List<TextMeshProUGUI>();
    [SerializeField] private Button basicStealthButton;
    [SerializeField] private Button focusedStealthButton;
    
    public List<Image> sellingItemIcons = new List<Image>();
    public List<Toggle> sellingItemToggles = new List<Toggle>();
    public List<string> sellingItemIds = new List<string>();
    private string selectedItemId;

    public Subject<Unit>   OnBasicStealthRequested   { get; } = new Subject<Unit>();
    public Subject<string> OnFocusedStealthRequested { get; } = new Subject<string>();

    public List<Subject<(string itemId,bool isSell)>> OnStreamingItemToggled { get; } = new List<Subject<(string itemId,bool isSell)>>();
    void Awake()
    {
        basicStealthButton.onClick.AddListener(() => OnBasicStealthRequested.OnNext(Unit.Default));
        focusedStealthButton.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(selectedItemId))
                OnFocusedStealthRequested.OnNext(selectedItemId);
        });

        for (int i = 0; i < sellingItemIcons.Count; i++)
        {
            sellingItemToggles[i].onValueChanged.AddListener(isOn =>
            {
                string itemId = sellingItemIds[i];
                OnStreamingItemToggled[i]?.OnNext((itemId, isOn));
            });
        }
    }
    public void SetSellingItemIcons(List<StreamingItemPlain> itemDates)
    {
        foreach (var toggle in sellingItemToggles)
        {
            toggle.gameObject.SetActive(false);
        }
        
        for (int i = 0; i < itemDates.Count; i++)
        {
            sellingItemIcons[i].sprite = itemDates[i].icon;
            sellingItemToggles[i].gameObject.SetActive(true);
            sellingItemIds[i] = itemDates[i].itemId;
        }
    }
    
    public void SetStreamingItemsPriceText(int itemIndex, int price)
    {
        if (itemIndex < 0 || itemIndex >= selectedItemsPriceTexts.Count)
        {
            Debug.LogError($"Invalid item index: {itemIndex}");
            return;
        }
        selectedItemsPriceTexts[itemIndex].text = $"Price: {price}G";
    }

    public void SetStealthMarketingCost(int cost)
        => stealthCostText.text = $"Cost: {cost}G";
    
    public void SetStealthCooldown(float cd)
        => stealthCooldownText.text = cd > 0
            ? $"Cooldown: {Mathf.CeilToInt(cd)}s"
            : "";

    /// <summary>UIから集中ステマ対象アイテムを選択したら呼ぶ</summary>
    public void SelectItem(string itemId)
    {
        selectedItemId = itemId;
        focusedStealthButton.interactable = !string.IsNullOrEmpty(itemId);
    }
}