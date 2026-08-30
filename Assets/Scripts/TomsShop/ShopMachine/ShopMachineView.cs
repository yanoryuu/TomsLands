using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// マシンショップ（店カスタマイズ）画面の View。
/// カタログ一覧 + 選択マシンの詳細 + 購入/撤去ボタン + 設置枠カウンタ。
/// 参照は未配線（null）でも動作する。
/// </summary>
public class ShopMachineView : MonoBehaviour
{
    [Header("カタログ")]
    [SerializeField] private Transform catalogParent;
    [SerializeField] private GameObject machineSlotPrefab;

    [Header("設置枠")]
    [SerializeField] private TextMeshProUGUI slotCounterText;

    [Header("詳細")]
    [SerializeField] private Image detailIcon;
    [SerializeField] private TextMeshProUGUI detailNameText;
    [SerializeField] private TextMeshProUGUI detailDescriptionText;
    [SerializeField] private TextMeshProUGUI detailEffectText;
    [SerializeField] private TextMeshProUGUI detailCostText;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button removeButton;

    [Header("生産アイテム選択（選択式製造機のみ） ※未配線でも動作する")]
    [SerializeField] private GameObject producedItemGroup;
    [SerializeField] private TMP_Dropdown producedItemDropdown;

    [Header("その他")]
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button closeButton;

    public Subject<Unit> OnPurchaseClicked { get; } = new();
    public Subject<Unit> OnRemoveClicked { get; } = new();
    public Subject<Unit> OnCloseRequested { get; } = new();
    /// <summary>生産アイテムのドロップダウンで選択が変わった（候補リストのindex）。</summary>
    public Subject<int> OnProducedItemChanged { get; } = new();

    private void Awake()
    {
        if (purchaseButton != null)
            purchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
        if (removeButton != null)
            removeButton.onClick.AddListener(() => OnRemoveClicked.OnNext(Unit.Default));
        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        if (producedItemDropdown != null)
            producedItemDropdown.onValueChanged.AddListener(i => OnProducedItemChanged.OnNext(i));
    }

    /// <summary>
    /// 生産アイテム選択ドロップダウンを表示・再構築する。未配線（null）なら何もしない。
    /// </summary>
    public void ShowProducedItemSelector(List<string> optionLabels, int selectedIndex)
    {
        if (producedItemGroup == null || producedItemDropdown == null) return;
        producedItemGroup.SetActive(true);
        producedItemDropdown.ClearOptions();
        producedItemDropdown.AddOptions(optionLabels);
        producedItemDropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, optionLabels.Count - 1)));
        producedItemDropdown.RefreshShownValue();
    }

    public void HideProducedItemSelector()
    {
        if (producedItemGroup != null) producedItemGroup.SetActive(false);
    }

    /// <summary>カタログを再構築する。Setup と購読は Presenter 側で行う。</summary>
    public List<ShopMachineSlotUI> PopulateCatalog(int count)
    {
        var slots = new List<ShopMachineSlotUI>();
        if (catalogParent == null || machineSlotPrefab == null) return slots;

        for (int i = catalogParent.childCount - 1; i >= 0; i--)
        {
            Destroy(catalogParent.GetChild(i).gameObject);
        }

        for (int i = 0; i < count; i++)
        {
            var obj = Instantiate(machineSlotPrefab, catalogParent);
            var slot = obj.GetComponent<ShopMachineSlotUI>();
            if (slot != null) slots.Add(slot);
        }
        return slots;
    }

    public void UpdateSlotCounter(int used, int max)
    {
        if (slotCounterText == null) return;
        slotCounterText.text = $"設置枠 {used}/{max}";
        slotCounterText.color = used >= max ? new Color(1f, 0.55f, 0.35f) : Color.white;
    }

    public void ShowDetail(ShopMachineData machine, bool placed, bool canPurchase, int removeRefund)
    {
        if (detailIcon != null)
        {
            detailIcon.sprite = machine.icon;
            detailIcon.enabled = machine.icon != null;
        }
        if (detailNameText != null) detailNameText.text = machine.machineName;
        if (detailDescriptionText != null) detailDescriptionText.text = machine.description;
        if (detailEffectText != null) detailEffectText.text = machine.EffectSummary;
        if (detailCostText != null)
            detailCostText.text = placed ? $"撤去で {removeRefund:N0}G 返金" : $"{machine.cost:N0}G";

        if (purchaseButton != null)
        {
            purchaseButton.gameObject.SetActive(!placed);
            purchaseButton.interactable = canPurchase;
        }
        if (removeButton != null)
            removeButton.gameObject.SetActive(placed);
    }

    public void ClearDetail()
    {
        if (detailNameText != null) detailNameText.text = "";
        if (detailDescriptionText != null) detailDescriptionText.text = "マシンを選択してください";
        if (detailEffectText != null) detailEffectText.text = "";
        if (detailCostText != null) detailCostText.text = "";
        if (detailIcon != null) detailIcon.enabled = false;
        if (purchaseButton != null) purchaseButton.gameObject.SetActive(false);
        if (removeButton != null) removeButton.gameObject.SetActive(false);
    }

    public void ShowMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }
}
