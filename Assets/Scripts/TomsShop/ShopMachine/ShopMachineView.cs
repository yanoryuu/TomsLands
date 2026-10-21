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

    [Header("設置済み一覧（複数設置） ※未配線でも動作する")]
    [SerializeField] private GameObject placementGroup;
    [SerializeField] private Transform placementListParent;

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

        EnsureFocusScope();
    }

    private UIFocusScope _focusScope;

    /// <summary>
    /// パッド操作: この画面をフォーカス単位にする（B=閉じる）。一覧はパッドの選択に自動スクロール。
    /// パネルが非アクティブのまま一覧が作られる場合に備え、遅延初期化にする。
    /// </summary>
    private UIFocusScope EnsureFocusScope()
    {
        if (_focusScope != null) return _focusScope;
        _focusScope = UIFocusScope.GetOrAdd(gameObject);
        if (_focusScope != null && !_focusScope.HasCancel && closeButton != null)
            _focusScope.SetCancelButton(closeButton);
        AddAutoFollow(catalogParent);
        AddAutoFollow(placementListParent);
        return _focusScope;
    }

    private UIFocusScope focusScope => EnsureFocusScope();

    private static void AddAutoFollow(Transform listParent)
    {
        if (listParent == null) return;
        var scroll = listParent.GetComponentInParent<ScrollRect>(true);
        if (scroll != null && scroll.GetComponent<ScrollRectAutoFollow>() == null)
            scroll.gameObject.AddComponent<ScrollRectAutoFollow>();
    }

    /// <summary>選択中の要素が listParent の何番目の行に属するか（属さなければ -1）。再構築前に呼ぶ。</summary>
    private static int SelectedRowIndex(Transform listParent)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null || listParent == null) return -1;
        for (int i = 0; i < listParent.childCount; i++)
            if (sel.transform.IsChildOf(listParent.GetChild(i))) return i;
        return -1;
    }

    /// <summary>
    /// 一覧の再構築後のパッドフォーカス。旧一覧の行を選んでいた場合は同じ位置の新しい行へ移す
    /// （購入ボタン等を操作中なら動かさない）。
    /// </summary>
    private void RefocusList(List<ShopMachineSlotUI> slots, int prevIndex)
    {
        if (focusScope == null || slots.Count == 0 || prevIndex < 0) return;
        var target = slots[Mathf.Clamp(prevIndex, 0, slots.Count - 1)].GetComponent<Selectable>();
        if (target != null) focusScope.Select(target);
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

        int prevIndex = SelectedRowIndex(catalogParent);
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

        // パッド操作: カタログ先頭を初期フォーカスに（購入ボタンは誤爆防止のため避ける）
        if (focusScope != null && slots.Count > 0)
            focusScope.FirstSelected = slots[0].GetComponent<Selectable>();
        RefocusList(slots, prevIndex);
        return slots;
    }

    public void UpdateSlotCounter(int used, int max)
    {
        if (slotCounterText == null) return;
        slotCounterText.text = $"設置枠 {used}/{max}";
        slotCounterText.color = used >= max ? new Color(1f, 0.55f, 0.35f) : Color.white;
    }

    /// <param name="purchaseCost">実際に払う額（村の割引込み）。負なら定価 machine.cost を出す。</param>
    public void ShowDetail(ShopMachineData machine, int placedCount, bool canPurchase, int removeRefund, int purchaseCost = -1)
    {
        int cost = purchaseCost >= 0 ? purchaseCost : machine.cost;
        if (detailIcon != null)
        {
            detailIcon.sprite = machine.icon;
            detailIcon.enabled = machine.icon != null;
        }
        if (detailNameText != null) detailNameText.text = machine.machineName;
        if (detailDescriptionText != null) detailDescriptionText.text = machine.description;
        if (detailEffectText != null) detailEffectText.text = machine.EffectSummary;
        if (detailCostText != null)
        {
            detailCostText.text = placedCount > 0
                ? $"{cost:N0}G　設置中×{placedCount}（撤去で {removeRefund:N0}G 返金）"
                : $"{cost:N0}G";
        }

        // 複数設置可: 購入ボタンは常に表示（枠・資金・店Lvで interactable 制御）
        if (purchaseButton != null)
        {
            purchaseButton.gameObject.SetActive(true);
            purchaseButton.interactable = canPurchase;
        }
        if (removeButton != null)
            removeButton.gameObject.SetActive(placedCount > 0);
    }

    /// <summary>
    /// 設置済み一覧を再構築する（machineSlotPrefab の行を流用）。
    /// 未配線なら空リストを返す。
    /// </summary>
    public List<ShopMachineSlotUI> PopulatePlacements(int count)
    {
        var slots = new List<ShopMachineSlotUI>();
        if (placementListParent == null || machineSlotPrefab == null)
        {
            if (placementGroup != null) placementGroup.SetActive(false);
            return slots;
        }

        int prevIndex = SelectedRowIndex(placementListParent);
        for (int i = placementListParent.childCount - 1; i >= 0; i--)
        {
            Destroy(placementListParent.GetChild(i).gameObject);
        }

        if (placementGroup != null) placementGroup.SetActive(count > 0);

        for (int i = 0; i < count; i++)
        {
            var obj = Instantiate(machineSlotPrefab, placementListParent);
            var slot = obj.GetComponent<ShopMachineSlotUI>();
            if (slot != null) slots.Add(slot);
        }
        RefocusList(slots, prevIndex);
        return slots;
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
