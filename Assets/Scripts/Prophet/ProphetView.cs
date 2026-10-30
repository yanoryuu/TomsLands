using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>品のカードの棚の絞り込み。</summary>
public enum ProphetItemFilter { All, Weapon, Armor, Tool, InStock, News }

/// <summary>品のカードの棚の並べ替え（押すたびに次へ）。</summary>
public enum ProphetItemSort { Stock, Price, Change }

/// <summary>
/// 占い師の画面「上げる枠・下げる枠」（Docs/Prophet_Intervention.md）。
///   左: 占い師（立ち絵・セリフの吹き出し・下に力の玉「占い師の力 N / 2・配信 1 回で 1 つ回復（最大 2）」）
///   右上: ▲ 上げる枠（緑）・▼ 下げる枠（赤）
///   右下: 品のカードの棚（解放済みの全品。絞り込み・並べ替え）
/// 操作: カードを枠へドラッグ（枠の上で「需要 ▲ 3 ターン／力を 1 つ使う」→ 離して確定）／カードを押す → 枠を押す／
///       パッドは棚で決定 → 左右で枠を選んで決定（LB＝上げる・RB＝下げる の近道）。B / Esc で選んだカードをやめる。
/// 見た目と入力だけで、入れられるか・入れる（力・代金）は ProphetPresenter → IProphetIntervention が決める。
/// AutoPlay が名前で押す口（変えない）: ItemCardButton(itemId) / UpSlotButton / DownSlotButton / SlotButton(dir) /
///   closeButton（フィールド）/ IsOpen / SelectedItemId / ClearSelection()。
/// </summary>
public class ProphetView : MonoBehaviour
{
    [Header("占い師の力")]
    [Tooltip("占い師の力 N / 2")]
    [SerializeField] private TMP_Text powerText;
    [Tooltip("光る玉（左から光る。使うと暗くなる）")]
    [SerializeField] private Image[] powerGems;

    [Header("枠")]
    [SerializeField] private ProphetSlotView upSlot;
    [SerializeField] private ProphetSlotView downSlot;

    [Header("品のカードの棚")]
    [SerializeField] private ScrollRect shelfScroll;
    [SerializeField] private RectTransform shelfContent;
    [SerializeField] private ProphetItemCard itemCardTemplate;
    [Tooltip("絞り込みのボタン（すべて・武器・防具・道具・在庫あり・新聞で動く の順）")]
    [SerializeField] private Button[] filterButtons;
    [SerializeField] private Button sortButton;
    [SerializeField] private TMP_Text sortLabel;
    [Tooltip("当てはまる品が無いときの文")]
    [SerializeField] private TMP_Text shelfEmptyText;
    [SerializeField] private TMP_Text hintText;

    [Header("セリフ")]
    [SerializeField] private TMP_Text dialogueText;

    [Header("キャラ表情（バズ連動・任意）")]
    [SerializeField] private CharacterExpression characterExpression;

    [Header("操作")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button characterButton;

    public Subject<Unit> OnCloseClicked { get; } = new();
    public Subject<Unit> OnCharacterClicked { get; } = new();
    /// <summary>品をその枠へ入れたい（ドラッグで離した・選んでから枠を押した・LB/RB）。</summary>
    public Subject<(string itemId, ProphetDirection direction)> OnPlaceRequested { get; } = new();
    /// <summary>枠に入っている品のカードを押した／持とうとした。</summary>
    public Subject<string> OnInUseItemPressed { get; } = new();
    /// <summary>品を選ばずに枠を押した。</summary>
    public Subject<ProphetDirection> OnSlotPressedWithoutItem { get; } = new();
    /// <summary>セリフの板に一時的に出す文（null で元に戻す）。</summary>
    public Subject<string> OnHint { get; } = new();

    /// <summary>枠の上にカードを持ってきたときの確かめの文と、入れられるか（Presenter が IProphetIntervention.CanPlace で決める）。</summary>
    public Func<string, ProphetDirection, (string text, bool ok)> PreviewProvider { get; set; }

    // ───────── AutoPlay が名前で使う口 ─────────

    /// <summary>占い師の画面が開いているか。</summary>
    public bool IsOpen => isActiveAndEnabled && gameObject.activeInHierarchy;
    public Button UpSlotButton => upSlot != null ? upSlot.Button : null;
    public Button DownSlotButton => downSlot != null ? downSlot.Button : null;
    public Button SlotButton(ProphetDirection direction) => direction == ProphetDirection.Up ? UpSlotButton : DownSlotButton;
    public Button CloseButton => closeButton;
    /// <summary>押して選んでいる品（無ければ null）。</summary>
    public string SelectedItemId => selectedItemId;

    /// <summary>品のカードのボタン。今の絞り込みで隠れていれば「すべて」に戻して探す。無ければ null。</summary>
    public Button ItemCardButton(string itemId)
    {
        var card = FindCard(itemId);
        if (card == null && filter != ProphetItemFilter.All)
        {
            SetFilter(ProphetItemFilter.All);
            card = FindCard(itemId);
        }
        return card != null ? card.Button : null;
    }

    // ───────── 内部 ─────────

    private UIFocusScope focusScope;
    private readonly PendingFocus pendingFocus = new();
    private Func<Selectable> firstSelectable;
    private readonly List<ProphetItemCard> cards = new();
    private readonly List<ProphetItemInfo> allItems = new();
    private bool initialized;

    private ProphetItemFilter filter = ProphetItemFilter.All;
    private ProphetItemSort sort = ProphetItemSort.Stock;
    private string selectedItemId;
    private ProphetItemCard carried;
    private RectTransform ghost;
    private ProphetSlotView hoveredSlot;

    private static readonly string[] FilterNames = { "すべて", "武器", "防具", "道具", "在庫あり", "新聞で動く" };
    private static readonly Color ChipOn = new(0.91f, 0.73f, 0.29f, 1f);
    private static readonly Color ChipOff = new(0f, 0f, 0f, 0.3f);
    private static readonly Color ChipTextOn = new(0.23f, 0.14f, 0.08f, 1f);
    private static readonly Color ChipTextOff = new(1f, 0.95f, 0.84f, 1f);

    private const string DefaultHint = "品のカードを上の枠へ（押してから枠を押しても入る。パッドは LB＝上げる・RB＝下げる）";

    [VContainer.Inject]
    public void Construct(BuzzSystem buzzSystem)
    {
        if (characterExpression != null) characterExpression.Bind(buzzSystem);
    }

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        if (closeButton != null) closeButton.onClick.AddListener(() => OnCloseClicked.OnNext(Unit.Default));
        if (characterButton != null)
        {
            characterButton.onClick.AddListener(() => OnCharacterClicked.OnNext(Unit.Default));
            characterButton.navigation = new Navigation { mode = Navigation.Mode.None }; // 立ち絵はパッドで選ばない
        }
        foreach (var slot in new[] { upSlot, downSlot })
        {
            if (slot == null) continue;
            var s = slot;
            if (s.Button != null) s.Button.onClick.AddListener(() => OnSlotPressed(s));
            var relay = s.gameObject.GetComponent<ProphetHoverRelay>() ?? s.gameObject.AddComponent<ProphetHoverRelay>();
            relay.Entered = () => { if (carried == null && !string.IsNullOrEmpty(selectedItemId)) ShowPreview(s); };
            relay.Exited = () => { if (carried == null) ClearPreview(s); };
        }
        if (filterButtons != null)
            for (int i = 0; i < filterButtons.Length; i++)
            {
                if (filterButtons[i] == null) continue;
                var f = (ProphetItemFilter)i;
                filterButtons[i].onClick.AddListener(() => SetFilter(f));
            }
        if (sortButton != null) sortButton.onClick.AddListener(NextSort);
        if (itemCardTemplate != null) itemCardTemplate.gameObject.SetActive(false);
        focusScope = GetComponentInParent<UIFocusScope>();
        if (focusScope != null)
        {
            focusScope.OnTabLeftEvent.AddListener(() => PlaceSelected(ProphetDirection.Up));
            focusScope.OnTabRightEvent.AddListener(() => PlaceSelected(ProphetDirection.Down));
        }
        if (hintText != null) hintText.text = DefaultHint;
        UpdateFilterChips();
        UpdateSortLabel();
    }

    private void OnEnable() => pendingFocus.Request(true);

    private void OnDisable()
    {
        EndCarry(silent: true);
        SelectItem(null);
    }

    private void LateUpdate() => pendingFocus.Tick(focusScope, firstSelectable ??= FirstSelectable);

    private Selectable FirstSelectable()
    {
        foreach (var c in cards)
            if (c != null && c.gameObject.activeInHierarchy && !c.InUse) return c.Button;
        return UpSlotButton;
    }

    // ─────────────────────────────────────────────────────────
    // 表示（Presenter から）
    // ─────────────────────────────────────────────────────────

    /// <summary>占い師の力（光る玉と「占い師の力 N / 2」）。usedIndex の玉は今使った演出。</summary>
    public void SetPower(int power, int max, bool animateUse = false)
    {
        Initialize();
        if (powerText != null) powerText.text = $"占い師の力 <size=125%><color=#FFD970>{power}</color></size> / {max}";
        if (powerGems == null) return;
        for (int i = 0; i < powerGems.Length; i++)
        {
            var g = powerGems[i];
            if (g == null) continue;
            g.gameObject.SetActive(i < max);
            bool lit = i < power;
            g.color = lit ? new Color(0.84f, 0.72f, 1f, 1f) : new Color(0.2f, 0.15f, 0.27f, 1f);
            if (animateUse && i == power) UIMotion.Bump(g.transform, 1.3f, 0.3f);
        }
    }

    /// <summary>枠の中身（null は空き）。blocked なら空きの枠を押せない見た目にする（力が 0 など）。</summary>
    public void SetSlots(ProphetSlotInfo? up, ProphetSlotInfo? down, int effectTurns, bool blocked)
    {
        Initialize();
        Apply(upSlot, up, effectTurns, blocked);
        Apply(downSlot, down, effectTurns, blocked);
    }

    private static void Apply(ProphetSlotView slot, ProphetSlotInfo? info, int effectTurns, bool blocked)
    {
        if (slot == null) return;
        if (info.HasValue) slot.ShowActive(info.Value);
        else slot.ShowEmpty(effectTurns);
        slot.SetBlocked(blocked);
    }

    /// <summary>品のカードの棚（解放済みの全品）。絞り込み・並べ替えは View が持つ。</summary>
    public void SetItems(IReadOnlyList<ProphetItemInfo> items, bool animate)
    {
        Initialize();
        allItems.Clear();
        if (items != null) allItems.AddRange(items);
        Rebuild(animate);
    }

    /// <summary>入った演出（枠が光ってカードがはまる）。</summary>
    public void PlayPlaced(ProphetDirection direction)
    {
        var slot = SlotOf(direction);
        if (slot != null) slot.PlayPlaced();
    }

    public void PlayRejected(ProphetDirection direction)
    {
        var slot = SlotOf(direction);
        if (slot != null) slot.PlayRejected();
    }

    public void ShowDialogue(string message)
    {
        if (dialogueText != null) dialogueText.text = message;
    }

    /// <summary>選んだカード・持ち上げをやめる。</summary>
    public void ClearSelection()
    {
        EndCarry(silent: true);
        SelectItem(null);
    }

    public void SetFilter(ProphetItemFilter f)
    {
        Initialize();
        if (filter == f) return;
        filter = f;
        UpdateFilterChips();
        Rebuild(true);
    }

    // ─────────────────────────────────────────────────────────
    // 棚
    // ─────────────────────────────────────────────────────────

    private void NextSort()
    {
        sort = (ProphetItemSort)(((int)sort + 1) % 3);
        UpdateSortLabel();
        Rebuild(true);
    }

    private void UpdateSortLabel()
    {
        if (sortLabel == null) return;
        sortLabel.text = sort switch
        {
            ProphetItemSort.Price => "並び: 値段の高い順 ▼",
            ProphetItemSort.Change => "並び: 前日の値上がり順 ▼",
            _ => "並び: 在庫の多い順 ▼",
        };
    }

    private void UpdateFilterChips()
    {
        if (filterButtons == null) return;
        for (int i = 0; i < filterButtons.Length; i++)
        {
            var b = filterButtons[i];
            if (b == null) continue;
            bool on = (int)filter == i;
            if (b.targetGraphic != null) b.targetGraphic.color = on ? ChipOn : ChipOff;
            var t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null)
            {
                if (i < FilterNames.Length) t.text = FilterNames[i];
                t.color = on ? ChipTextOn : ChipTextOff;
            }
        }
    }

    private bool Passes(in ProphetItemInfo i) => filter switch
    {
        ProphetItemFilter.Weapon => i.Type == ItemTypeData.ItemType.Weapon,
        ProphetItemFilter.Armor => i.Type == ItemTypeData.ItemType.Armor,
        ProphetItemFilter.Tool => i.Type == ItemTypeData.ItemType.Tool,
        ProphetItemFilter.InStock => i.Stock > 0,
        ProphetItemFilter.News => i.NewsDirection != 0,
        _ => true,
    };

    private void Rebuild(bool animate)
    {
        if (itemCardTemplate == null || shelfContent == null) return;
        EndCarry(silent: true);
        var list = allItems.Where(i => Passes(i));
        list = sort switch
        {
            ProphetItemSort.Price => list.OrderByDescending(i => i.Price),
            ProphetItemSort.Change => list.OrderByDescending(i => i.Change1d).ThenByDescending(i => i.Price),
            _ => list.OrderByDescending(i => i.Stock).ThenByDescending(i => i.Price),
        };
        var shown = list.ToList();
        while (cards.Count < shown.Count) cards.Add(CreateCard());
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            bool on = i < shown.Count;
            card.gameObject.SetActive(on);
            if (!on) continue;
            card.Bind(shown[i]);
            card.transform.SetSiblingIndex(i);
            card.SetSelected(card.ItemId == selectedItemId);
        }
        if (!string.IsNullOrEmpty(selectedItemId) && FindCard(selectedItemId) == null) SelectItem(null);
        if (shelfEmptyText != null)
        {
            shelfEmptyText.gameObject.SetActive(shown.Count == 0);
            shelfEmptyText.text = "当てはまる品がない";
        }
        if (animate)
        {
            if (shelfScroll != null) shelfScroll.verticalNormalizedPosition = 1f;
            if (shelfContent.gameObject.activeInHierarchy) UIMotion.Stagger(shelfContent, 0.02f, 24f, 0.2f);
        }
    }

    private ProphetItemCard CreateCard()
    {
        var card = Instantiate(itemCardTemplate, shelfContent, false);
        card.gameObject.SetActive(true);
        card.Button.onClick.AddListener(() => OnCardPressed(card));
        card.DragStarted += OnCardDragStarted;
        card.Dragging += OnCardDragging;
        card.DragEnded += OnCardDragEnded;
        return card;
    }

    private ProphetItemCard FindCard(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        foreach (var c in cards)
            if (c != null && c.gameObject.activeSelf && c.ItemId == itemId) return c;
        return null;
    }

    private ProphetSlotView SlotOf(ProphetDirection d) => d == ProphetDirection.Up ? upSlot : downSlot;

    // ─────────────────────────────────────────────────────────
    // 押す → 枠を押す（パッドも同じ）
    // ─────────────────────────────────────────────────────────

    private void OnCardPressed(ProphetItemCard card)
    {
        if (card == null) return;
        if (card.InUse)
        {
            UIMotion.Shake((RectTransform)card.transform);
            OnInUseItemPressed.OnNext(card.ItemId);
            return;
        }
        if (selectedItemId == card.ItemId)
        {
            SelectItem(null);
            return;
        }
        SelectItem(card.ItemId);
        UIMotion.Bump(card.transform, 1.05f, 0.16f);
        // パッド: 置き先の枠へ（左右でもう一方の枠へ移れる）
        if (focusScope != null && ControllerInput.IsNavigating.CurrentValue)
        {
            var target = upSlot != null && !upSlot.IsOccupied ? upSlot : downSlot;
            if (target != null && target.Button != null) focusScope.Select(target.Button);
        }
    }

    private void SelectItem(string itemId)
    {
        selectedItemId = itemId;
        foreach (var c in cards)
            if (c != null) c.SetSelected(!string.IsNullOrEmpty(itemId) && c.ItemId == itemId);
        ClearPreview(null);
        if (focusScope != null) focusScope.SetCancelAction(string.IsNullOrEmpty(itemId) ? null : () => CancelSelection());
        OnHint.OnNext(string.IsNullOrEmpty(itemId) ? null : "入れる枠を押しておくれ。上げるなら緑、下げるなら赤だよ。");
        if (hintText != null) hintText.text = string.IsNullOrEmpty(itemId) ? DefaultHint : "入れる枠を押す（LB＝上げる・RB＝下げる。やめるときは B / Esc）";
    }

    private void CancelSelection()
    {
        var card = FindCard(selectedItemId);
        SelectItem(null);
        if (card != null && focusScope != null && ControllerInput.IsNavigating.CurrentValue) focusScope.Select(card.Button);
    }

    private void OnSlotPressed(ProphetSlotView slot)
    {
        if (string.IsNullOrEmpty(selectedItemId))
        {
            if (slot.IsOccupied) slot.PlayRejected();
            OnSlotPressedWithoutItem.OnNext(slot.Direction);
            return;
        }
        PlaceSelected(slot.Direction);
    }

    private void PlaceSelected(ProphetDirection direction)
    {
        if (string.IsNullOrEmpty(selectedItemId)) return;
        string id = selectedItemId;
        SelectItem(null);
        OnPlaceRequested.OnNext((id, direction));
    }

    private void ShowPreview(ProphetSlotView slot)
    {
        string id = carried != null ? carried.ItemId : selectedItemId;
        if (slot == null || string.IsNullOrEmpty(id)) return;
        if (hoveredSlot != null && hoveredSlot != slot) hoveredSlot.SetPreview(null);
        hoveredSlot = slot;
        var (text, ok) = PreviewProvider != null ? PreviewProvider(id, slot.Direction) : (null, true);
        slot.SetPreview(text, ok);
    }

    private void ClearPreview(ProphetSlotView slot)
    {
        if (slot == null)
        {
            if (upSlot != null) upSlot.SetPreview(null);
            if (downSlot != null) downSlot.SetPreview(null);
            hoveredSlot = null;
            return;
        }
        slot.SetPreview(null);
        if (hoveredSlot == slot) hoveredSlot = null;
    }

    // ─────────────────────────────────────────────────────────
    // ドラッグで枠へ
    // ─────────────────────────────────────────────────────────

    private void OnCardDragStarted(ProphetItemCard card, PointerEventData e)
    {
        if (card.InUse)
        {
            OnInUseItemPressed.OnNext(card.ItemId);
            return;
        }
        SelectItem(null);
        EndCarry(silent: true);
        carried = card;
        card.SetCarried(true);
        ghost = CreateGhost(card);
        MoveGhost(e);
        DragFeel.Lift(ghost);
        OnHint.OnNext("上げたい品は緑の枠へ、下げたい品は赤の枠へ置いておくれ。");
    }

    private void OnCardDragging(ProphetItemCard card, PointerEventData e)
    {
        if (carried != card) return;
        MoveGhost(e);
        var over = SlotUnder(e);
        if (over != hoveredSlot)
        {
            if (hoveredSlot != null) hoveredSlot.SetPreview(null);
            hoveredSlot = null;
            if (over != null) ShowPreview(over);
        }
    }

    private void OnCardDragEnded(ProphetItemCard card, PointerEventData e)
    {
        if (carried != card) return;
        var over = SlotUnder(e);
        string id = card.ItemId;
        EndCarry(silent: over != null);
        if (over != null) OnPlaceRequested.OnNext((id, over.Direction));
    }

    private ProphetSlotView SlotUnder(PointerEventData e)
    {
        foreach (var s in new[] { upSlot, downSlot })
            if (s != null && s.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)s.transform, e.position, e.pressEventCamera))
                return s;
        return null;
    }

    private void EndCarry(bool silent)
    {
        var card = carried;
        if (card == null) return;
        carried = null;
        card.SetCarried(false);
        ClearPreview(null);
        OnHint.OnNext(null);
        var g = ghost;
        ghost = null;
        if (g == null) return;
        if (silent || UIMotion.IsSkip) UIMotion.DestroySafe(g.gameObject);
        else DragFeel.Drop(g, false, () => { if (g != null) UIMotion.DestroySafe(g.gameObject); });
    }

    private RectTransform CreateGhost(ProphetItemCard card)
    {
        var src = card.Face;
        var canvas = GetComponentInParent<Canvas>();
        var root = canvas != null ? (RectTransform)canvas.rootCanvas.transform : (RectTransform)transform;
        var g = Instantiate(src, root, false);
        g.name = "ProphetCardGhost";
        g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
        g.sizeDelta = src.rect.size;
        g.localScale = Vector3.one * 1.08f;
        g.localRotation = Quaternion.Euler(0, 0, -4f);
        foreach (var gr in g.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;
        g.SetAsLastSibling();
        return g;
    }

    private void MoveGhost(PointerEventData e)
    {
        if (ghost == null || e == null) return;
        var root = ghost.parent as RectTransform;
        if (root != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(root, e.position, e.pressEventCamera, out var world))
            ghost.position = world;
    }
}

/// <summary>枠のホバー・パッドの選択を ProphetView に伝える小さな中継。</summary>
public sealed class ProphetHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public Action Entered;
    public Action Exited;
    public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();
    public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
    public void OnSelect(BaseEventData eventData) => Entered?.Invoke();
    public void OnDeselect(BaseEventData eventData) => Exited?.Invoke();
}
