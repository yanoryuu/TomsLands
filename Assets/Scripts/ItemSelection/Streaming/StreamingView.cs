using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using R3;

public class StreamingView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stealthCostText;
    [SerializeField] private TextMeshProUGUI stealthCooldownText;
    
    [SerializeField] private List<TextMeshProUGUI> selectedItemsPriceTexts;
    [SerializeField] private Button basicStealthButton;
    [SerializeField] private Button focusedStealthButton;

    string selectedItemId;

    public Subject<Unit>   OnBasicStealthRequested   { get; } = new Subject<Unit>();
    public Subject<string> OnFocusedStealthRequested { get; } = new Subject<string>();

    void Awake()
    {
        basicStealthButton.onClick.AddListener(() => OnBasicStealthRequested.OnNext(Unit.Default));
        focusedStealthButton.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(selectedItemId))
                OnFocusedStealthRequested.OnNext(selectedItemId);
        });
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
        selectedItemId           = itemId;
        focusedStealthButton.interactable = !string.IsNullOrEmpty(itemId);
    }

    public void ShowStreamingUI()
    {
        gameObject.SetActive(true);
    }
}