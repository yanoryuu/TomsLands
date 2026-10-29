using System;
using DG.Tweening;
using System.Collections.Generic;
using System.Linq;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 情報屋・地図タブのView（鍛冶屋と同じ「左リスト＋右詳細」構成）。
/// 左: 全ダンジョンの行（購入済みは「購入済み」表示）。行クリックで選択。
/// 右: 選択ダンジョンの詳細。未購入は ??? 表示＋購入ボタン、購入済みは弱点/推奨Lv/難易度/説明を公開。
/// 詳細パネルの参照は未配線（null）でも動作する（リストのみで成立）。
/// </summary>
public class MapInfoView : MonoBehaviour
{
    [Header("ScrollView")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private GameObject mapInfoContent;
    [SerializeField] private GameObject mapPurchaseSlotPrefab;

    [Header("詳細パネル（右ペイン） ※未配線でも動作する")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TextMeshProUGUI detailNameText;
    [SerializeField] private TextMeshProUGUI detailStatusText;      // 情報購入済み / 情報未購入
    [SerializeField] private TextMeshProUGUI detailDescriptionText; // 説明（未購入は???）
    [SerializeField] private TextMeshProUGUI weaknessText;          // 弱点属性
    [SerializeField] private TextMeshProUGUI recommendText;         // 推奨Lv
    [SerializeField] private TextMeshProUGUI difficultyText;        // 難易度
    [SerializeField] private TextMeshProUGUI costText;              // 情報料
    [SerializeField] private Button purchaseButton;                 // 情報を買う

    /// <summary>詳細パネルの購入ボタンが押された（選択中ダンジョンのキーを通知）。</summary>
    public Subject<DungeonName> OnMapPurchaseClicked { get; } = new();

    private readonly CompositeDisposable slotDisposables = new();
    private readonly List<MapPurchaseSlot> activeSlots = new();
    private List<DungeonData> currentMaps = new();
    private Dictionary<DungeonName, int[]> currentCosts = new();
    private int currentCost;
    private DungeonName? selectedKey;
    private DungeonName? upcomingKey;

    private readonly PendingFocus pendingFocus = new();
    private UIFocusScope focusScope;
    private Func<Selectable> rowFocusTarget;

    private void Awake()
    {
        if (purchaseButton != null)
            purchaseButton.onClick.AddListener(() =>
            {
                if (selectedKey.HasValue) OnMapPurchaseClicked.OnNext(selectedKey.Value);
            });
        if (detailPanel != null) detailPanel.SetActive(false);
    }

    /// <param name="mapCosts">情報料テーブル（載っているダンジョンの情報だけ売る）</param>
    /// <param name="cost">今日の地図の値段（どのダンジョンも同じ。納付の期日が来るたびに上がる）</param>
    /// <param name="upcoming">次の配信先（地図の NEXT と同じ）。その行を先頭に置いて NEXT の印を付け、最初に選ぶ</param>
    public void SetMapSlot(List<DungeonData> maps, Dictionary<DungeonName, int[]> mapCosts, int cost, DungeonName? upcoming = null)
    {
        upcomingKey = upcoming;
        currentMaps = OrderUpcomingFirst(maps, upcoming);
        currentCosts = mapCosts ?? new Dictionary<DungeonName, int[]>();
        currentCost = cost;

        slotDisposables.Clear();
        // パッド/キー: 行を選んだまま作り直した（購入直後など）なら、同じダンジョンの行へフォーカスを戻す
        bool refocusRow = (mapInfoContent != null && ControllerFocusUtil.IsSelectionInside(mapInfoContent.transform))
                          || (purchaseButton != null && ControllerFocusUtil.IsSelectionInside(purchaseButton.transform));
        for (int i = mapInfoContent.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(mapInfoContent.transform.GetChild(i).gameObject);
        }
        activeSlots.Clear();

        foreach (var data in currentMaps)
        {
            GameObject slotObj = Instantiate(mapPurchaseSlotPrefab, mapInfoContent.transform);
            MapPurchaseSlot slot = slotObj.GetComponent<MapPurchaseSlot>();
            slot.SetMapInfo(data.key, data.dungeonName, data.dungeonIcon, GetCost(data.key), data.isShowedInfo, IsSellable(data.key));
            slot.SetUpcoming(upcomingKey.HasValue && upcomingKey.Value == data.key);
            activeSlots.Add(slot);

            slot.OnSelected
                .Subscribe(key => SelectDungeon(key))
                .AddTo(slotDisposables);
        }

        if (mapInfoContent)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(mapInfoContent.GetComponent<RectTransform>());
        }

        // 選択の維持 or 先頭を自動選択（鍛冶屋と同じ操作感）
        if (selectedKey.HasValue && currentMaps.Any(d => d.key == selectedKey.Value))
            SelectDungeon(selectedKey.Value);
        else if (currentMaps.Count > 0)
            SelectDungeon(currentMaps[0].key);
        else if (detailPanel != null)
            detailPanel.SetActive(false);

        if (refocusRow) pendingFocus.Request(true);
    }

    /// <summary>次の配信先を先頭へ（残りはデータの順のまま）。元のリストは変えない。</summary>
    private static List<DungeonData> OrderUpcomingFirst(List<DungeonData> maps, DungeonName? upcoming)
    {
        var list = maps != null ? new List<DungeonData>(maps) : new List<DungeonData>();
        if (!upcoming.HasValue) return list;
        int index = list.FindIndex(d => d != null && d.key == upcoming.Value);
        if (index > 0)
        {
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(0, item);
        }
        return list;
    }

    private void LateUpdate()
    {
        if (focusScope == null) focusScope = GetComponentInParent<UIFocusScope>();
        // ラムダを毎フレーム作らないよう、1つを使い回す
        pendingFocus.Tick(focusScope, rowFocusTarget ??= SelectedOrFirstRowTarget);
    }

    /// <summary>選択中ダンジョンの行、無ければ先頭行のフォーカス先（パッド/キー用。無ければ null）。</summary>
    public Selectable SelectedOrFirstRowTarget()
    {
        var row = SelectedRowTarget();
        return row != null ? row : FirstRowTarget();
    }

    /// <summary>先頭行のフォーカス先（パッド/キー用。無ければ null）。</summary>
    public Selectable FirstRowTarget()
    {
        foreach (var slot in activeSlots)
        {
            if (slot == null || slot.FocusTarget == null) continue;
            if (UIFocusManager.IsUsable(slot.FocusTarget.gameObject)) return slot.FocusTarget;
        }
        return null;
    }

    /// <summary>選択中ダンジョンの行のフォーカス先（パッド/キー用。無ければ null）。</summary>
    public Selectable SelectedRowTarget()
    {
        if (!selectedKey.HasValue) return null;
        foreach (var slot in activeSlots)
        {
            if (slot == null || slot.FocusTarget == null || slot.DungeonKey != selectedKey.Value) continue;
            if (UIFocusManager.IsUsable(slot.FocusTarget.gameObject)) return slot.FocusTarget;
        }
        return null;
    }

    /// <summary>ダンジョンを選択して詳細を表示する（購入直後の再表示にも使う）。</summary>
    public void SelectDungeon(DungeonName key)
    {
        var data = currentMaps.FirstOrDefault(d => d.key == key);
        if (data == null) return;

        selectedKey = key;
        foreach (var slot in activeSlots)
        {
            if (slot != null) slot.SetSelected(slot.DungeonKey == key);
        }
        ShowDetail(data);
    }

    /// <summary>情報を買った直後: 弱点の欄を跳ねさせ、金色に少し光らせる（I-14。新聞のハンコと同じ扱い）。</summary>
    public void PopWeakness()
    {
        if (weaknessText == null || !weaknessText.gameObject.activeInHierarchy) return;
        UIFx.Pop(weaknessText.transform, 1.25f, 0.25f);
        var baseColor = weaknessText.color;
        weaknessText.DOKill();
        weaknessText.color = new Color32(0xE8, 0xA3, 0x3D, 0xFF);
        weaknessText.DOColor(baseColor, 0.6f).SetDelay(0.25f).SetLink(weaknessText.gameObject);
    }

    private void ShowDetail(DungeonData data)
    {
        if (detailPanel == null) return;
        detailPanel.SetActive(true);

        bool purchased = data.isShowedInfo;

        if (detailIcon != null)
        {
            detailIcon.sprite = data.dungeonIcon;
            detailIcon.preserveAspect = true;
            detailIcon.enabled = data.dungeonIcon != null;
        }
        if (detailNameText != null) detailNameText.text = data.dungeonName;
        if (detailStatusText != null)
        {
            detailStatusText.text = purchased ? "情報購入済み" : "情報未購入";
            detailStatusText.color = purchased ? new Color(0.55f, 0.9f, 0.55f) : new Color(1f, 0.75f, 0.4f);
        }
        bool sellable = IsSellable(data.key);
        if (detailDescriptionText != null)
        {
            if (purchased)
                detailDescriptionText.text = data.dungeonDescription;
            else if (sellable)
                detailDescriptionText.text = "？？？\n\n情報を購入すると、このダンジョンの弱点属性・推奨レベル・詳しい様子がわかる。";
            else
                detailDescriptionText.text = "？？？\n\nこの場所の情報は、この店では扱っていないらしい……。";
        }

        if (weaknessText != null)
            weaknessText.text = purchased ? $"弱点: {ItemAttributeText.WithSuffix(data.requiredAttribute)}" : "弱点: ？";
        if (recommendText != null)
            recommendText.text = purchased ? $"推奨Lv: {data.recommendedLevel}" : "推奨Lv: ？";
        if (difficultyText != null)
            difficultyText.text = purchased ? $"難易度: {new string('★', Mathf.Clamp(data.difficulty, 1, 10))}" : "難易度: ？";

        int cost = GetCost(data.key);
        if (costText != null)
        {
            costText.gameObject.SetActive(!purchased && sellable);
            costText.text = $"情報料 {cost:N0}G";
        }
        if (purchaseButton != null)
            purchaseButton.gameObject.SetActive(!purchased && sellable);
    }

    /// <summary>情報料テーブルに載っているか（載っていないダンジョンの情報は売っていない）。</summary>
    private bool IsSellable(DungeonName key) =>
        currentCosts.TryGetValue(key, out var costs) && costs.Length > 0;

    private int GetCost(DungeonName key) => IsSellable(key) ? currentCost : 0;

    private void OnDestroy()
    {
        slotDisposables.Dispose();
    }
}
