using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// マシンショップ（店カスタマイズ）画面の View。
/// カタログ一覧 + 選択マシンの詳細 + 購入/強化/修理/撤去ボタン + 設置枠カウンタ。
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

    [Header("強化・修理（設置済みのマシン） ※未配線でも動作する")]
    [SerializeField] private Button upgradeButton;
    [SerializeField] private TextMeshProUGUI upgradeButtonText;
    [SerializeField] private Button repairButton;
    [SerializeField] private TextMeshProUGUI repairButtonText;

    [Header("生産アイテム選択（選択式製造機のみ） ※未配線でも動作する")]
    [SerializeField] private GameObject producedItemGroup;
    [SerializeField] private TMP_Dropdown producedItemDropdown;

    [Header("設置済み一覧（複数設置） ※未配線でも動作する")]
    [SerializeField] private GameObject placementGroup;
    [SerializeField] private Transform placementListParent;
    [Tooltip("設置済み一覧を出している間だけカタログ（スクロール枠）の高さを詰め、一覧と重ならないようにする。上端は動かさない。空なら何もしない")]
    [SerializeField] private RectTransform catalogArea;
    [SerializeField] private float catalogHeightWithPlacements = 450f;

    [Header("その他")]
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button closeButton;

    [Header("「店」画面のタブ（改装／設備） ※未配線でも動作する")]
    [SerializeField] private ShopFacilityTabsView tabs;

    /// <summary>改装タブへ切り替えたい（タブのクリック・LB/RB）。</summary>
    public Observable<Unit> OnUpgradeTabRequested => tabs != null ? tabs.OnOtherTabRequested : Observable.Empty<Unit>();

    public Subject<Unit> OnPurchaseClicked { get; } = new();
    public Subject<Unit> OnRemoveClicked { get; } = new();
    public Subject<Unit> OnUpgradeClicked { get; } = new();
    public Subject<Unit> OnRepairClicked { get; } = new();
    public Subject<Unit> OnCloseRequested { get; } = new();
    /// <summary>生産アイテムのドロップダウンで選択が変わった（候補リストのindex）。</summary>
    public Subject<int> OnProducedItemChanged { get; } = new();

    private void Awake()
    {
        if (purchaseButton != null)
            purchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
        if (removeButton != null)
            removeButton.onClick.AddListener(() => OnRemoveClicked.OnNext(Unit.Default));
        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(() => OnUpgradeClicked.OnNext(Unit.Default));
        if (repairButton != null)
            repairButton.onClick.AddListener(() => OnRepairClicked.OnNext(Unit.Default));
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

    private Vector2? catalogBaseSize;
    private Vector2 catalogBasePos;

    /// <summary>設置済み一覧の有無に合わせてカタログの高さを切り替える（上端は固定）。</summary>
    private void FitCatalogArea(bool hasPlacements)
    {
        if (catalogArea == null) return;
        if (catalogBaseSize == null)
        {
            catalogBaseSize = catalogArea.sizeDelta;
            catalogBasePos = catalogArea.anchoredPosition;
        }
        Vector2 size = catalogBaseSize.Value;
        float height = hasPlacements ? Mathf.Min(size.y, catalogHeightWithPlacements) : size.y;
        float top = catalogBasePos.y + size.y * (1f - catalogArea.pivot.y);
        catalogArea.sizeDelta = new Vector2(size.x, height);
        catalogArea.anchoredPosition = new Vector2(catalogBasePos.x, top - height * (1f - catalogArea.pivot.y));
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

        // #3 画面を開いて最初のカタログだけ札を順に出す（買った・置いた直後の作り直しでは動かさない）
        if (staggerNextCatalog && catalogParent.gameObject.activeInHierarchy)
        {
            staggerNextCatalog = false;
            var rows = new List<RectTransform>();
            foreach (var s in slots) if (s != null && s.transform is RectTransform rt) rows.Add(rt);
            UIMotion.Stagger(rows, 0.05f);
        }
        return slots;
    }

    private bool staggerNextCatalog = true;

    private void OnEnable() => staggerNextCatalog = true;

    public void UpdateSlotCounter(int used, int max)
    {
        if (slotCounterText == null) return;
        slotCounterText.text = $"設置枠 {used}/{max}";
        slotCounterText.color = used >= max ? new Color(1f, 0.55f, 0.35f) : Color.white;
    }

    private float? upgradeBaseX;

    /// <summary>
    /// 選択中のマシンの詳細を出す。置いていないマシンは購入ボタン、置いてあるマシンは強化・修理・撤去のボタン
    /// （同じマシンは 1 台までなので、置いてあれば購入ボタンは出さない）。
    /// </summary>
    public void ShowDetail(ShopMachineDetailInfo info)
    {
        var machine = info.Machine;
        if (detailIcon != null)
        {
            detailIcon.sprite = machine.icon;
            detailIcon.preserveAspect = true;
            detailIcon.enabled = machine.icon != null;
        }
        if (detailNameText != null) detailNameText.text = info.Placed ? $"{machine.machineName}  Lv{info.Level}" : machine.machineName;
        if (detailDescriptionText != null) detailDescriptionText.text = machine.description;
        if (detailEffectText != null) detailEffectText.text = info.EffectLine;
        if (detailCostText != null)
        {
            // 置く前は値段、置いた後は「強化の次の効果」と「壊れやすさ」を1行ずつ、折り返さずに出す（長いときは枠の自動縮小に任せる）
            detailCostText.textWrappingMode = TextWrappingModes.NoWrap;
            detailCostText.text = info.CostLine;
        }

        if (purchaseButton != null)
        {
            purchaseButton.gameObject.SetActive(!info.Placed);
            purchaseButton.interactable = info.CanPurchase;
        }
        if (removeButton != null)
            removeButton.gameObject.SetActive(info.Placed);

        if (upgradeButton != null)
        {
            upgradeButton.gameObject.SetActive(info.Placed);
            upgradeButton.interactable = info.CanUpgrade;
            if (upgradeButtonText != null) upgradeButtonText.text = info.UpgradeLabel;
        }
        if (repairButton != null)
        {
            repairButton.gameObject.SetActive(info.Placed && info.ShowRepair);
            repairButton.interactable = info.CanRepair;
            if (repairButtonText != null) repairButtonText.text = info.RepairLabel;
        }
        CenterUpgradeButton(info.Placed && info.ShowRepair && repairButton != null);
    }

    /// <summary>修理ボタンを出さないときは強化ボタンを中央へ寄せる（並べるときは配置どおりの左側）。</summary>
    private void CenterUpgradeButton(bool besideRepair)
    {
        if (upgradeButton == null || upgradeButton.transform is not RectTransform rt) return;
        upgradeBaseX ??= rt.anchoredPosition.x;
        rt.anchoredPosition = new Vector2(besideRepair ? upgradeBaseX.Value : 0f, rt.anchoredPosition.y);
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
            FitCatalogArea(false);
            return slots;
        }

        int prevIndex = SelectedRowIndex(placementListParent);
        for (int i = placementListParent.childCount - 1; i >= 0; i--)
        {
            Destroy(placementListParent.GetChild(i).gameObject);
        }

        if (placementGroup != null) placementGroup.SetActive(count > 0);
        FitCatalogArea(placementGroup != null && count > 0);

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
        if (detailDescriptionText != null) detailDescriptionText.text = "設備を選んでください";
        if (detailEffectText != null) detailEffectText.text = "";
        if (detailCostText != null) detailCostText.text = "";
        if (detailIcon != null) detailIcon.enabled = false;
        if (purchaseButton != null) purchaseButton.gameObject.SetActive(false);
        if (removeButton != null) removeButton.gameObject.SetActive(false);
        if (upgradeButton != null) upgradeButton.gameObject.SetActive(false);
        if (repairButton != null) repairButton.gameObject.SetActive(false);
    }

    public void ShowMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }
}

/// <summary>設備画面の詳細に出す中身（Presenter が組む）。</summary>
public sealed class ShopMachineDetailInfo
{
    public ShopMachineData Machine;
    public bool Placed;
    public int Level;
    /// <summary>効果の行（置いてあれば今のレベルの効果）。</summary>
    public string EffectLine;
    /// <summary>値段の行（置く前 = 購入費、置いた後 = 次のレベル・壊れやすさ・撤去の返金。リッチテキスト可）。</summary>
    public string CostLine;
    public bool CanPurchase;
    public bool CanUpgrade;
    public string UpgradeLabel;
    public bool ShowRepair;
    public bool CanRepair;
    public string RepairLabel;
}
