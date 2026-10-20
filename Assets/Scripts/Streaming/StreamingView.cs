using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using R3;

// 未使用（2026-10-20 確認）: 旧ステマ機能の名残。どのシーン・コードからも使われていない（StreamingView はビルド外の Information.unity にだけある）。
// 廃止済みの TomsModel.Trust を前提にしているので、使うなら作り直すこと。
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

    public Subject<Unit>   OnBasicStealthRequested   { get; } = new();
    public Subject<string> OnFocusedStealthRequested { get; } = new();

    public List<Subject<(string itemId,bool isSell)>> OnStreamingItemToggled { get; } = new();

    private void Awake()
    {
        basicStealthButton.onClick.AddListener(() => OnBasicStealthRequested.OnNext(Unit.Default));
        focusedStealthButton.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(selectedItemId))
                OnFocusedStealthRequested.OnNext(selectedItemId);
        });

        for (int i = 0; i < sellingItemIcons.Count; i++)
        {
            // for のループ変数はラムダ間で共有されるため、スロットごとにコピーして捕まえる
            int index = i;
            sellingItemToggles[index].onValueChanged.AddListener(isOn =>
            {
                if (index >= sellingItemIds.Count || index >= OnStreamingItemToggled.Count) return;
                string itemId = sellingItemIds[index];
                OnStreamingItemToggled[index]?.OnNext((itemId, isOn));
            });
        }
    }
    public void SetSellingItemIcons(List<StreamingItemPlain> itemDates)
    {
        foreach (var toggle in sellingItemToggles)
            toggle.gameObject.SetActive(false);

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
        selectedItemsPriceTexts[itemIndex].text = $"Price: {price:N0}G";
    }

    public void SetStealthMarketingCost(int cost)
        => stealthCostText.text = $"Cost: {cost:N0}G";
    
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