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
    private int currentTurnForCost = 1;
    private DungeonName? selectedKey;

    private void Awake()
    {
        if (purchaseButton != null)
            purchaseButton.onClick.AddListener(() =>
            {
                if (selectedKey.HasValue) OnMapPurchaseClicked.OnNext(selectedKey.Value);
            });
        if (detailPanel != null) detailPanel.SetActive(false);
    }

    public void SetMapSlot(List<DungeonData> maps, Dictionary<DungeonName, int[]> mapCosts, int currentTurn)
    {
        currentMaps = maps ?? new List<DungeonData>();
        currentCosts = mapCosts ?? new Dictionary<DungeonName, int[]>();
        currentTurnForCost = currentTurn;

        slotDisposables.Clear();
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

    private void ShowDetail(DungeonData data)
    {
        if (detailPanel == null) return;
        detailPanel.SetActive(true);

        bool purchased = data.isShowedInfo;

        if (detailIcon != null)
        {
            detailIcon.sprite = data.dungeonIcon;
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
            weaknessText.text = purchased ? $"弱点: {AttributeLabel(data.requiredAttribute)}属性" : "弱点: ???";
        if (recommendText != null)
            recommendText.text = purchased ? $"推奨Lv: {data.recommendedLevel}" : "推奨Lv: ???";
        if (difficultyText != null)
            difficultyText.text = purchased ? $"難易度: {new string('★', Mathf.Clamp(data.difficulty, 1, 10))}" : "難易度: ???";

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

    private int GetCost(DungeonName key)
    {
        if (!currentCosts.TryGetValue(key, out var costs) || costs.Length == 0) return 0;
        if (costs.Length >= currentTurnForCost && currentTurnForCost > 0)
            return costs[currentTurnForCost - 1];
        return costs[costs.Length - 1];
    }

    private static string AttributeLabel(ItemTypeData.ItemAttribute attr) => attr switch
    {
        ItemTypeData.ItemAttribute.Fire => "火",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Earth => "土",
        ItemTypeData.ItemAttribute.Wind => "風",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark => "闇",
        _ => attr.ToString()
    };

    private void OnDestroy()
    {
        slotDisposables.Dispose();
    }
}
