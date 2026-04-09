using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;

public class BlackSmithView : MonoBehaviour
{
    [Header("ScrollView")]
    [SerializeField] private ScrollRect scrollRect;            // ★ 追加
    [SerializeField] private GameObject blackSmithContent;     // scrollRect.content と一致させるのが望ましい

    [Header("Tabs")]
    [SerializeField] private GameObject weaponTab;
    [SerializeField] private GameObject armorTab;
    [SerializeField] private GameObject developmentTab;
    [SerializeField] private GameObject specialTab;

    [Header("Prefabs & Buttons")]
    [SerializeField] private GameObject itemShopSlotPrefab;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button weaponButton;
    [SerializeField] private Button armorButton;
    [SerializeField] private Button developButton;
    [SerializeField] private Button specialWeaponButton;

    [Header("Description")]
    [SerializeField] private TextMeshProUGUI itemDescriptionText;

    public Subject<Unit> OnCloseRequested { get; private set; } = new();
    public Subject<BlackSmithTab> OnChangePanel { get; private set; } = new();

    private readonly List<ItemShopSlot> activeSlots = new();

    // ★ タブごとのスクロール位置を保持（任意）
    private readonly Dictionary<BlackSmithTab, Vector2> _scrollPerTab = new();
    private BlackSmithTab _currentTab = BlackSmithTab.Weapon;
    
    private readonly Dictionary<BlackSmithTab, Vector3> initTabPos = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        weaponButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Weapon; OnChangePanel.OnNext(BlackSmithTab.Weapon); });
        armorButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Armor; OnChangePanel.OnNext(BlackSmithTab.Armor); });
        developButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Development; OnChangePanel.OnNext(BlackSmithTab.Development); });
        specialWeaponButton.onClick.AddListener(() => { _currentTab = BlackSmithTab.Special; OnChangePanel.OnNext(BlackSmithTab.Special); });

        // 初期スクロール位置（未保存タブは右上(1,1)=先頭）にしておく
        _scrollPerTab[BlackSmithTab.Weapon] = new Vector2(0, 1);
        _scrollPerTab[BlackSmithTab.Armor] = new Vector2(0, 1);
        _scrollPerTab[BlackSmithTab.Development] = new Vector2(0, 1);
        _scrollPerTab[BlackSmithTab.Special] = new Vector2(0, 1);
        
        initTabPos[BlackSmithTab.Weapon] = weaponTab.transform.localPosition;
        initTabPos[BlackSmithTab.Armor] = armorTab.transform.localPosition;
        initTabPos[BlackSmithTab.Development] = developmentTab.transform.localPosition;
        initTabPos[BlackSmithTab.Special] = specialTab.transform.localPosition;

        // 念のため整数スクロール向けセットアップ
        if (scrollRect && scrollRect.content)
        {
            // content の pivot は (0.5, 1) 推奨（上基準の縦リスト）
            // scrollRect.content.pivot = new Vector2(0.5f, 1f); // 既に設定済みなら不要
        }
    }

    /// <summary>
    /// 商品リストを表示（スクロール位置を保存・復元）
    /// </summary>
    public List<ItemShopSlot> PopulateItemList(List<RuntimeItemData> runtimeItems)
    {
        // ★ 1) いまのスクロール位置を保存
        var prePos = GetSavedScrollForTab(_currentTab);

        if (scrollRect)
        {
            // 最新の実位置を保存（保存済みより優先）
            prePos = scrollRect.normalizedPosition;
            SaveScrollForTab(_currentTab, prePos);
        }

        // 既存スロット破棄（Content配下の全子オブジェクトを削除）
        for (int i = blackSmithContent.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(blackSmithContent.transform.GetChild(i).gameObject);
        }
        activeSlots.Clear();

        // 再生成
        List<ItemShopSlot> slots = new();
        foreach (var item in runtimeItems)
        {
            var slotObj = Instantiate(itemShopSlotPrefab, blackSmithContent.transform);
            var slot = slotObj.GetComponent<ItemShopSlot>();
            slot.SetItem(
                item.ItemId,
                item.ItemName,
                item.ItemIcon,
                item.ItemBackground,
                item.CurrentPrice.Value,
                item.MaxStock.Value,
                item.Stock.Value,
                item.IsPopular.Value
            );
            slots.Add(slot);
            activeSlots.Add(slot);
        }

        // ★ 2) レイアウトを確定させ、次のフレームでスクロール位置を復元
        if (scrollRect)
            StartCoroutine(RestoreScrollNextFrame(GetSavedScrollForTab(_currentTab)));

        return slots;
    }

    public void SetDescription(string description)
    {
        itemDescriptionText.text = description;
    }

    public void SortItemTab(BlackSmithTab type)
    {
        var weaponSeq =  weaponTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Weapon].y, 0.1f);
        var armorSeq = armorTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Armor].y, 0.1f);
        var developmentSeq = developmentTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Development].y, 0.1f);
        var specialSeq = specialTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Special].y, 0.1f);
        
        // タブを一番上に持ってくる動作はそのまま
        switch (type)
        {
            case BlackSmithTab.Weapon:
                weaponSeq.Kill();
                weaponTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Weapon].y+10, 0.2f);
                break;
            case BlackSmithTab.Armor:
                armorSeq.Kill();
                armorTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Armor].y + 10, 0.2f);
                break;
            case BlackSmithTab.Development:
                developmentSeq.Kill();
                developmentTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Development].y + 10, 0.2f);
                break;
            case BlackSmithTab.Special:
                specialSeq.Kill();
                specialTab.transform.DOLocalMoveY(initTabPos[BlackSmithTab.Special].y + 10, 0.2f);
                break;
        }

        // ★ タブ切替直後に、保存してあるスクロール位置へ復元
        if (scrollRect)
            StartCoroutine(RestoreScrollNextFrame(GetSavedScrollForTab(type)));
    }

    // ===== スクロール保存・復元ユーティリティ =====

    private void SaveScrollForTab(BlackSmithTab tab, Vector2 pos)
    {
        _scrollPerTab[tab] = pos;
    }

    private Vector2 GetSavedScrollForTab(BlackSmithTab tab)
    {
        if (_scrollPerTab.TryGetValue(tab, out var pos))
            return pos;
        return new Vector2(0, 1); // 右上（縦スクの先頭）
    }

    private IEnumerator RestoreScrollNextFrame(Vector2 pos)
    {
        // レイアウト確定 → 次フレームで反映（Immediate だけだと戻ることがある）
        yield return null; // 1 frame 待つ
        LayoutRebuilder.ForceRebuildLayoutImmediate(blackSmithContent.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
        if (scrollRect)
        {
            scrollRect.normalizedPosition = pos;
            // 念押し（まだコンテンツが伸びる UI の場合）
            yield return null;
            Canvas.ForceUpdateCanvases();
            scrollRect.normalizedPosition = pos;
        }
    }
}

public enum BlackSmithTab
{
    Weapon,
    Armor,
    Development,
    Special
}
