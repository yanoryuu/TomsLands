using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using R3;
using TMPro;

public class StreamingSettingView : MonoBehaviour
{
    [SerializeField] private Transform availablePanel;
    [SerializeField] private Transform selectedPanel;
    [SerializeField] private GameObject availableSlotPrefab;
    [SerializeField] private GameObject selectedSlotPrefab;

    [Header("確定ボタン")]
    [SerializeField] private Button confirmButton;

    [Header("前回と同じで確定")]
    [SerializeField] private Button quickConfirmButton;
    [SerializeField] private TextMeshProUGUI quickConfirmLabel;

    [Header("オート陳列")]
    [SerializeField] private Button autoSelectButton;

    [Header("行き先のダンジョン（未配線なら出さない）")]
    [SerializeField] private TextMeshProUGUI dungeonNameText;
    [SerializeField] private TextMeshProUGUI dungeonLevelText;
    [SerializeField] private TextMeshProUGUI dungeonWeaknessText;

    public Subject<string> OnItemSelected     { get; } = new();
    public Subject<string> OnItemDeselected   { get; } = new();
    public Subject<(string id, int qty)> OnQuantityChanged { get; } = new();
    public Subject<Unit> OnConfirmClicked     { get; } = new();
    public Subject<Unit> OnQuickConfirmClicked { get; } = new();
    public Subject<Unit> OnAutoSelectClicked { get; } = new();

    private void Awake()
    {
        if (confirmButton != null)
            confirmButton.onClick.AddListener(() => OnConfirmClicked.OnNext(Unit.Default));

        if (quickConfirmButton != null)
            quickConfirmButton.onClick.AddListener(() => OnQuickConfirmClicked.OnNext(Unit.Default));

        if (autoSelectButton != null)
            autoSelectButton.onClick.AddListener(() => OnAutoSelectClicked.OnNext(Unit.Default));

        SetupGamepad();
    }

    // ─── パッド/キー操作 ───
    // 十字で移動・A で追加（左の一覧）／数量の ± ・X で解除（右の一覧）・確定ボタンで配信開始。
    private UIFocusScope _scope;
    private int _padRefocusIndex = -1;

    private void SetupGamepad()
    {
        _scope = UIFocusScope.GetOrAdd(gameObject);
        AddAutoFollow(availablePanel);
        AddAutoFollow(selectedPanel);
    }

    private static void AddAutoFollow(Transform content)
    {
        var scroll = content != null ? content.GetComponentInParent<ScrollRect>(true) : null;
        if (scroll == null) return;
        if (!scroll.TryGetComponent<ScrollRectAutoFollow>(out _))
            scroll.gameObject.AddComponent<ScrollRectAutoFollow>();
        // 一覧の間を十字で移るときにスクロールバーで止まらないよう、ナビ対象から外す（自動スクロールで追従する）
        foreach (var bar in new[] { scroll.verticalScrollbar, scroll.horizontalScrollbar })
            if (bar != null) bar.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    private static Selectable FirstSelectable(GameObject go)
    {
        if (go == null) return null;
        foreach (var s in go.GetComponentsInChildren<Selectable>(true))
            if (s.navigation.mode != Navigation.Mode.None) return s;
        return null;
    }

    /// <summary>
    /// 読み込まれた前回構成の件数を「前回と同じ」ボタンラベルに反映する
    /// </summary>
    public void SetQuickConfirmLabel(int count)
    {
        if (quickConfirmLabel != null)
            quickConfirmLabel.text = count > 0 ? $"前回と同じ ({count}種)" : "前回と同じ";
        if (quickConfirmButton != null)
            quickConfirmButton.gameObject.SetActive(count > 0);
    }

    /// <summary>行き先のダンジョン欄（名前・レベル・弱点）。情報未購入の項目は呼び出し側で「？」にして渡す。</summary>
    public void ShowDungeonInfo(string dungeonName, string level, string weakness)
    {
        if (dungeonNameText != null) dungeonNameText.text = dungeonName;
        if (dungeonLevelText != null) dungeonLevelText.text = level;
        if (dungeonWeaknessText != null) dungeonWeaknessText.text = weakness;
    }

    /// <param name="matchupOf">品ごとの行き先との相性（null なら出さない）。値段と一緒に各行へ出す</param>
    public void PopulateAvailable(IEnumerable<RuntimeItemData> items, System.Func<RuntimeItemData, AttributeMatchup> matchupOf = null)
    {
        foreach (Transform t in availablePanel) Destroy(t.gameObject);
        Selectable firstAvailable = null;
        foreach (var item in items)
        {
            // 所持数が0以下のアイテムは表示しない
            if (item.Stock.Value <= 0) continue;

            var go   = Instantiate(availableSlotPrefab, availablePanel);
            var slot = go.GetComponent<AvailableItemSlot>();
            slot.Initialize(item.ItemId, item.ItemIcon, item.ItemName, item.Stock.Value, item.ItemAttribute);
            if (matchupOf != null) slot.SetReadInfo(item.CurrentPrice.Value, matchupOf(item));
            slot.OnItemSelected += id => OnItemSelected.OnNext(id);
            if (firstAvailable == null) firstAvailable = FirstSelectable(go);
        }

        // 最初のフォーカスは左の一覧の先頭（確定ボタンは誤爆しやすいので既定にしない）
        if (_scope != null)
        {
            // 並べる物が無いときは「配信開始」ではなく「オート選択」へ
            _scope.FirstSelected = firstAvailable != null ? firstAvailable : autoSelectButton;
            _scope.Refocus(resetToFirst: true);
        }
    }

    public void PopulateSelected(IEnumerable<KeyValuePair<string, int>> selected, ItemModel model)
    {
        foreach (Transform t in selectedPanel) Destroy(t.gameObject);
        var created = new List<SelectedItemSlot>();

        foreach (var kv in selected)
        {
            var id     = kv.Key;
            var qty    = kv.Value;
            var master = model.GetMasterItem(id);
            var runtime= model.GetRuntimeItem(id);
            if (master == null || runtime == null)
                continue;

            int maxQty = runtime.Stock.Value;

            var go   = Instantiate(selectedSlotPrefab, selectedPanel);
            var slot = go.GetComponent<SelectedItemSlot>();
            slot.Initialize(
                id,
                master.itemIcon,
                runtime.ItemBackground,
                master.itemName,
                qty,
                maxQty
            );
            slot.OnQuantityChanged += (itemId, newQty) => 
                OnQuantityChanged.OnNext((itemId, newQty));
            slot.OnItemDeselected += itemId =>
                OnItemDeselected.OnNext(itemId);
            slot.OnPadDeselectRequested += s => _padRefocusIndex = created.IndexOf(s);
            created.Add(slot);
        }

        // パッドの X で解除した直後: 同じ位置（末尾なら1つ上）のスロットへフォーカスを戻す
        if (_padRefocusIndex >= 0)
        {
            int idx = _padRefocusIndex;
            _padRefocusIndex = -1;
            Selectable target = created.Count > 0
                ? FirstSelectable(created[Mathf.Clamp(idx, 0, created.Count - 1)].gameObject)
                : null;
            if (_scope != null)
            {
                if (target != null) _scope.Select(target);
                else _scope.Refocus(resetToFirst: true);
            }
        }
    }

    /// <summary>
    /// 個別追加用。（必要であればこちらも同様に maxQty を渡す）
    /// </summary>
    public void AddSelectedItem(string id, Sprite icon, Sprite background, string name, int qty, int maxQty)
    {
        var go   = Instantiate(selectedSlotPrefab, selectedPanel);
        var slot = go.GetComponent<SelectedItemSlot>();
        slot.Initialize(id, icon, background, name, qty, maxQty);
        slot.OnQuantityChanged += (itemId, newQty) => 
            OnQuantityChanged.OnNext((itemId, newQty));
        slot.OnItemDeselected += itemId => 
            OnItemDeselected.OnNext(itemId);
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}
