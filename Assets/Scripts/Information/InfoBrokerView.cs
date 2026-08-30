using System.Collections.Generic;
using DG.Tweening;
using R3;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoBrokerView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button characterButton;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private GameObject mapTab;

    [Header("Tab Buttons")]
    [SerializeField] private Button mapButton;

    [Header("Content Panels")]
    [SerializeField] private GameObject mapPanel;

    [Header("所持金表示（情報屋表示中はCommonViewを出さないため） ※未配線でも動作する")]
    [SerializeField] private TextMeshProUGUI playerMoneyText;

    [Header("取引所（Exchange タブ） ※未配線でも動作する")]
    [SerializeField] private GameObject exchangeTab;
    [SerializeField] private Button exchangeButton;
    [SerializeField] private GameObject exchangePanel;
    [Tooltip("金融商品行（ItemShopSlot）の親")]
    [SerializeField] private Transform exchangeListParent;
    [SerializeField] private GameObject itemShopSlotPrefab;
    [SerializeField] private FinanceDetailPanel financeDetailPanel;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnCharacterClicked { get; } = new();
    public Subject<Unit> OnRefreshRequested { get; } = new();
    public Subject<InfoBrokerTab> OnChangePanel { get; } = new();

    /// <summary>取引所の詳細パネル（未配線なら null）。</summary>
    public FinanceDetailPanel FinanceDetail => financeDetailPanel;

    private readonly Dictionary<InfoBrokerTab, Vector3> initTabPos = new();

    private void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        }

        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
        }

        if (mapButton != null)
        {
            mapButton.onClick.AddListener(() => OnChangePanel.OnNext(InfoBrokerTab.Map));
        }

        if (exchangeButton != null)
        {
            exchangeButton.onClick.AddListener(() => OnChangePanel.OnNext(InfoBrokerTab.Exchange));
        }

        if (mapTab != null)
        {
            initTabPos[InfoBrokerTab.Map] = mapTab.transform.localPosition;
        }
        if (exchangeTab != null)
        {
            initTabPos[InfoBrokerTab.Exchange] = exchangeTab.transform.localPosition;
        }

        ShowPanel(InfoBrokerTab.Map);
        SortItemTab(InfoBrokerTab.Map);
    }

    public void ShowPanel(InfoBrokerTab tab)
    {
        if (mapPanel != null)
        {
            mapPanel.SetActive(tab == InfoBrokerTab.Map);
        }
        if (exchangePanel != null)
        {
            exchangePanel.SetActive(tab == InfoBrokerTab.Exchange);
        }
    }

    public void SortItemTab(InfoBrokerTab type)
    {
        MoveTab(mapTab, InfoBrokerTab.Map, type);
        MoveTab(exchangeTab, InfoBrokerTab.Exchange, type);
    }

    /// <summary>選択タブだけ少し持ち上げ、他は基準位置へ戻す（鍛冶屋と同じ挙動）。</summary>
    private void MoveTab(GameObject tab, InfoBrokerTab tabType, InfoBrokerTab selected)
    {
        if (tab == null || !initTabPos.ContainsKey(tabType)) return;

        tab.transform.DOKill();
        float baseY = initTabPos[tabType].y;
        bool isSelected = tabType == selected;
        tab.transform.DOLocalMoveY(isSelected ? baseY + 10 : baseY, isSelected ? 0.2f : 0.1f);
    }

    /// <summary>
    /// 取引所の商品行を再構築する（武具と同じ ItemShopSlot 行を共用）。
    /// Setup（SetFinance）と購読は呼び出し側（ExchangePanelController）が行う。
    /// </summary>
    public List<ItemShopSlot> PopulateFinanceRows(int count)
    {
        var slots = new List<ItemShopSlot>();
        if (exchangeListParent == null || itemShopSlotPrefab == null) return slots;

        for (int i = exchangeListParent.childCount - 1; i >= 0; i--)
        {
            Destroy(exchangeListParent.GetChild(i).gameObject);
        }

        for (int i = 0; i < count; i++)
        {
            var obj = Instantiate(itemShopSlotPrefab, exchangeListParent);
            var slot = obj.GetComponent<ItemShopSlot>();
            if (slot == null) continue;
            slots.Add(slot);

            var cg = obj.GetComponent<CanvasGroup>();
            if (cg == null) cg = obj.AddComponent<CanvasGroup>();
            float delay = Mathf.Min(i * 0.035f, 0.35f);
            cg.alpha = 0f;
            cg.DOFade(1f, 0.18f).SetDelay(delay).SetLink(obj);
            obj.transform.localScale = Vector3.one * 0.94f;
            obj.transform.DOScale(1f, 0.22f).SetDelay(delay).SetEase(Ease.OutCubic).SetLink(obj);
        }
        return slots;
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message ?? string.Empty;
    }

    /// <summary>情報屋専用の所持金表示を更新する。</summary>
    public void UpdatePlayerMoney(int money)
    {
        if (playerMoneyText != null) playerMoneyText.text = $"{money:N0}G";
    }
}

public enum InfoBrokerTab
{
    Map,
    Guess,
    Exchange
}
