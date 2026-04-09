using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using R3;

public class StreamingSalesController : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private float baseSalesInterval = 15f;
    [SerializeField] private float intervalRandomness = 3f;
    [SerializeField] private int maxItemsToSell = 5;

    [Header("参照")]
    [SerializeField] private StreamingSalesView view;
    [SerializeField] private BattleSequencer battleSequencer;

    [Header("価格変動設定 — 案1: 戦闘パフォーマンス連動")]
    [Tooltip("Heroが攻撃を当てた時の武器価格上昇率（例: 1.10 = 10%上昇）")]
    [SerializeField] private float weaponPriceUpOnHit = 1.10f;
    [Tooltip("Heroが攻撃を当てたが敵を倒せなかった時の武器価格下落率（例: 0.95 = 5%下落）")]
    [SerializeField] private float weaponPriceDownOnNonKill = 0.95f;
    [Tooltip("Heroがダメージを受けた時の防具価格下落率（例: 0.90 = 10%下落）")]
    [SerializeField] private float armorPriceDownOnHit = 0.90f;
    [Tooltip("Heroがダメージ0で攻撃を防いだ時の防具価格上昇率（例: 1.10 = 10%上昇）")]
    [SerializeField] private float armorPriceUpOnBlock = 1.10f;

    [Header("価格変動設定 — 案5: 属性相性連動（敵撃破時）")]
    [Tooltip("有利属性の装備の価格上昇率（例: 1.30 = 30%上昇）")]
    [SerializeField] private float effectiveAttributeRate = 1.30f;
    [Tooltip("不利属性の装備の価格下落率（例: 0.85 = 15%下落）")]
    [SerializeField] private float weakAttributeRate = 0.85f;

    [Header("ストップ高/ストップ安（元値に対する倍率）")]
    [Tooltip("価格の下限（元値の何倍まで下がれるか。例: 0.2 = 元値の20%が下限）")]
    [SerializeField] private float priceFloorRate = 0.2f;
    [Tooltip("価格の上限（元値の何倍まで上がれるか。例: 5.0 = 元値の5倍が上限）")]
    [SerializeField] private float priceCeilingRate = 5.0f;

    [Header("Inventory (UI)")]
    [SerializeField] private List<ItemSlotView> inventorySlotViews; // 在庫側のスロット（シーン上の 10 インスタンス）
    private List<RuntimeItemData> inventorySlotItemRefs = new List<RuntimeItemData>(); // 在庫スロットが参照している RuntimeItemData


    [Header("BattleDemand — 案A: 属性相性で需要変動")]
    [Tooltip("有利属性の敵を撃破した時の需要UP量")]
    [SerializeField] private float demandEffectiveAttributeUp = 0.20f;
    [Tooltip("不利属性の敵を撃破した時の需要DOWN量")]
    [SerializeField] private float demandWeakAttributeDown = 0.10f;

    [Header("BattleDemand — 案C: 連続販売トレンド")]
    [Tooltip("2ターン連続販売時の需要UP量（口コミ効果）")]
    [SerializeField] private float buzzBonus2Turn = 0.05f;
    [Tooltip("3ターン以上連続販売時の需要UP量（バズ効果）")]
    [SerializeField] private float buzzBonus3PlusTurn = 0.10f;
    [Tooltip("2ターン以上未販売時の需要DOWN量（話題消失）")]
    [SerializeField] private float unsoldPenalty = 0.08f;

    [Header("BattleDemand — 案E: 価格と需要の逆相関")]
    [Tooltip("高値判定の閾値（元値の何倍以上で高値とみなすか）")]
    [SerializeField] private float highPriceThreshold = 2.0f;
    [Tooltip("安値判定の閾値（元値の何倍以下で安値とみなすか）")]
    [SerializeField] private float lowPriceThreshold = 0.5f;
    [Tooltip("高値時の需要減衰率（毎ターン。例: 0.92 = 8%減）")]
    [SerializeField] private float highPriceDemandDecay = 0.92f;
    [Tooltip("安値時の需要増加率（毎ターン。例: 1.08 = 8%増）")]
    [SerializeField] private float lowPriceDemandGrowth = 1.08f;

    private ItemModel _mainItemModel;
    private StreamingSalesPresenter _presenter;
    private StreamingSalesModel _model;
    private BattleDemandTracker _demandTracker;
    private readonly CompositeDisposable _battleDisposables = new CompositeDisposable();

    /// <summary>
    /// BattleSceneStarterから呼ばれる初期化メソッド。
    /// BattleInputDataのSelectedItemsをInventory（在庫）に配置する。
    /// 売り場（ItemsForSale）は空で開始し、プレイヤーが手動で配置する。
    /// </summary>
    public void Setup(ItemModel itemModel, List<BattleInputItem> selectedItems)
    {
        // BattleInputItemからRuntimeItemDataに変換してInventory用リストを構築
        var inventoryItems = new List<RuntimeItemData>();
        foreach (var inputItem in selectedItems)
        {
            var runtime = itemModel.GetRuntimeItem(inputItem.ItemId);
            if (runtime != null)
            {
                // 持ち込み数量と価格を設定
                runtime.Stock.Value = inputItem.Quantity;
                runtime.CurrentPrice.Value = inputItem.Price;
                inventoryItems.Add(runtime);
                Debug.Log($"[StreamingSalesController] Inventory に追加: {inputItem.ItemId}, qty={inputItem.Quantity}, price={inputItem.Price}");
            }
            else
            {
                Debug.LogWarning($"[StreamingSalesController] Item not found: {inputItem.ItemId}");
            }
        }

        // 売り場は空、選択アイテムはInventoryに配置
        StartStreamingPhase(itemModel, new List<RuntimeItemData>(), inventoryItems);
    }

    /// <summary>
    /// 配信フェーズを開始する。
    /// </summary>
    /// <param name="itemModel">アイテムモデル</param>
    /// <param name="itemsForSale">売り場に最初から並べるアイテム（通常は空）</param>
    /// <param name="inventoryItems">Inventoryに配置するアイテム（StreamingSettingで選択したもの）</param>
    public void StartStreamingPhase(ItemModel itemModel, List<RuntimeItemData> itemsForSale = null, List<RuntimeItemData> inventoryItems = null)
    {
        _mainItemModel = itemModel;
    
        if (itemsForSale == null)
        {
            itemsForSale = new List<RuntimeItemData>();
        }

        _model = new StreamingSalesModel(baseSalesInterval, intervalRandomness);
        _model.SetItemsForSale(itemsForSale);

        // BattleDemandTracker を初期化（FightScene 専用の需要度管理）
        _demandTracker = new BattleDemandTracker(
            buzzBonus2Turn: buzzBonus2Turn,
            buzzBonus3PlusTurn: buzzBonus3PlusTurn,
            unsoldPenalty: unsoldPenalty,
            highPriceThreshold: highPriceThreshold,
            lowPriceThreshold: lowPriceThreshold,
            highPriceDemandDecay: highPriceDemandDecay,
            lowPriceDemandGrowth: lowPriceDemandGrowth
        );

        // 売り場と在庫の全アイテムを BattleDemandTracker に登録
        var allItems = new List<RuntimeItemData>();
        if (itemsForSale != null) allItems.AddRange(itemsForSale);
        if (inventoryItems != null) allItems.AddRange(inventoryItems);
        _demandTracker.Initialize(allItems);

        // Model に BattleDemandTracker を設定
        _model.DemandTracker = _demandTracker;

        _presenter = new StreamingSalesPresenter(_model, view);
        _presenter.Bind();

        ItemSlotView.OnItemDropped += HandleItemSwap;

        // Inventoryスロットに選択アイテムを配置
        InitializeInventorySlots(itemModel, itemsForSale, inventoryItems);

        _model.StartSalesLoopAsync(_mainItemModel, this.GetCancellationTokenOnDestroy()).Forget();
        
        // 戦闘ダメージイベントを購読して価格変動を適用
        SubscribeToBattleDamageEvents();
        
        Debug.Log($"営業開始！ 売り場: {itemsForSale.Count}種, Inventory: {inventoryItems?.Count ?? 0}種");
    }

    /// <summary>
    /// BattleSequencer のイベントを購読し、案1（戦闘パフォーマンス連動）と案5（属性相性連動）を実装する。
    /// </summary>
    private void SubscribeToBattleDamageEvents()
    {
        if (battleSequencer == null)
        {
            Debug.LogWarning("[StreamingSalesController] BattleSequencer が未設定のため、戦闘中の価格変動が無効です。");
            return;
        }

        // ========================================
        // 案1: 戦闘パフォーマンス連動
        // ========================================
        battleSequencer.OnCharacterDamaged
            .Subscribe(pair =>
            {
                var (attacker, target) = pair;

                // --- Heroが攻撃を当てた ---
                if (attacker.Type == CharacterType.Hero)
                {
                    if (target.IsDead)
                    {
                        // 敵を倒した → 武器価格UP（大きめ）
                        _model.AdjustPricesByType(ItemTypeData.ItemType.Weapon, weaponPriceUpOnHit, inventorySlotItemRefs,
                            priceFloorRate, priceCeilingRate, _mainItemModel);
                        Debug.Log($"[案1] Heroが敵を撃破！武器価格 {weaponPriceUpOnHit:P0} 上昇");
                    }
                    else
                    {
                        // 敵を倒せなかった → 武器価格DOWN
                        _model.AdjustPricesByType(ItemTypeData.ItemType.Weapon, weaponPriceDownOnNonKill, inventorySlotItemRefs,
                            priceFloorRate, priceCeilingRate, _mainItemModel);
                        Debug.Log($"[案1] Heroが攻撃したが倒せず！武器価格 {weaponPriceDownOnNonKill:P0} 下落");
                    }
                    RefreshAllSlotDisplays();
                }

                // --- Heroがダメージを受けた ---
                if (target.Type == CharacterType.Hero)
                {
                    // ApplyDamage の戻り値が1以上（最低保証ダメージ）なので、
                    // 防御力がattackPower以上なら実質「防いだ」と判定
                    int rawDamage = attacker.AttackPower;
                    int effectiveDamage = Mathf.Max(1, rawDamage - target.DefensePower);
                    
                    if (rawDamage <= target.DefensePower)
                    {
                        // 攻撃力 ≤ 防御力 → 防御成功 → 防具価格UP
                        _model.AdjustPricesByType(ItemTypeData.ItemType.Armor, armorPriceUpOnBlock, inventorySlotItemRefs,
                            priceFloorRate, priceCeilingRate, _mainItemModel);
                        Debug.Log($"[案1] Heroが攻撃を防御！防具価格 {armorPriceUpOnBlock:P0} 上昇");
                    }
                    else
                    {
                        // 通常ダメージ → 防具価格DOWN
                        _model.AdjustPricesByType(ItemTypeData.ItemType.Armor, armorPriceDownOnHit, inventorySlotItemRefs,
                            priceFloorRate, priceCeilingRate, _mainItemModel);
                        Debug.Log($"[案1] Heroが被弾！防具価格 {armorPriceDownOnHit:P0} 下落");
                    }
                    RefreshAllSlotDisplays();
                }
            })
            .AddTo(_battleDisposables);

        // ========================================
        // 案5: 属性相性連動（敵撃破時）
        // ========================================
        battleSequencer.OnEnemyDefeated
            .Subscribe(defeatedEnemy =>
            {
                var enemyElement = defeatedEnemy.Element;
                if (enemyElement == ElementType.None)
                {
                    Debug.Log($"[案5] {defeatedEnemy.Name} は無属性のため、属性相性による価格変動なし");
                    return;
                }

                // --- 案5: 属性相性で価格変動 ---
                _model.AdjustPricesByElementAffinity(
                    enemyElement,
                    effectiveAttributeRate,
                    weakAttributeRate,
                    inventorySlotItemRefs,
                    priceFloorRate,
                    priceCeilingRate,
                    _mainItemModel);

                Debug.Log($"[案5] {defeatedEnemy.Name}（{enemyElement}属性）を撃破！有利属性 {effectiveAttributeRate:P0} UP / 不利属性 {weakAttributeRate:P0} DOWN");

                // --- 案A: 属性相性で需要変動 ---
                if (_demandTracker != null)
                {
                    _demandTracker.AdjustDemandByElementAffinity(
                        enemyElement,
                        demandEffectiveAttributeUp,
                        -demandWeakAttributeDown,
                        _model.ItemsForSale,
                        inventorySlotItemRefs);

                    Debug.Log($"[需要A] {defeatedEnemy.Name}（{enemyElement}属性）撃破！有利属性の需要 +{demandEffectiveAttributeUp:F2} / 不利属性の需要 -{demandWeakAttributeDown:F2}");
                }

                RefreshAllSlotDisplays();
            })
            .AddTo(_battleDisposables);
    }

    /// <summary>
    /// 毎ターン呼び出される売買処理。売り場に出ているアイテムのみ販売対象。
    /// 販売後に案C（連続販売トレンド）と案E（価格と需要の逆相関）を適用する。
    /// </summary>
    public void ExecuteTurnSales()
    {
        if (_model == null || _mainItemModel == null)
        {
            Debug.LogWarning("[StreamingSalesController] Model が未初期化のため、ターン売買をスキップします。");
            return;
        }

        // 販売処理を実行（BattleDemand ベースで数量が決まる）
        _model.ExecuteSingleTurnSales(_mainItemModel);

        if (_demandTracker != null)
        {
            // 案E: 価格と需要の逆相関（高値→需要減、安値→需要増）
            _demandTracker.ApplyPriceDemandCorrelation(_model.ItemsForSale, inventorySlotItemRefs, _mainItemModel);

            // 案C: 連続販売トレンド（バズ/話題消失）
            _demandTracker.ProcessEndOfTurn(_model.ItemsForSale);
        }

        RefreshAllSlotDisplays();
        Debug.Log("[StreamingSalesController] ターン売買を実行しました。");
    }

    /// <summary>
    /// 売り場と在庫の全スロット表示を更新する（価格変動後などに呼ぶ）。
    /// </summary>
    private void RefreshAllSlotDisplays()
    {
        // 売り場の表示更新
        view.DisplayItems(_model.ItemsForSale);

        // 在庫スロットの表示更新
        for (int i = 0; i < inventorySlotViews.Count; i++)
        {
            if (i < inventorySlotItemRefs.Count)
            {
                inventorySlotViews[i].SetItem(inventorySlotItemRefs[i]);
            }
        }
    }


    /// <summary>
    /// Inventoryスロットを初期化する。
    /// StreamingSettingで選択したアイテムを優先的に配置し、残りは空にする。
    /// </summary>
    private void InitializeInventorySlots(ItemModel itemModel, List<RuntimeItemData> itemsForSale, List<RuntimeItemData> inventoryItems = null)
    {
        inventorySlotItemRefs = new List<RuntimeItemData>(new RuntimeItemData[inventorySlotViews.Count]);

        // StreamingSettingで選択されたアイテムをInventoryスロットに配置
        var itemsToPlace = inventoryItems ?? new List<RuntimeItemData>();

        for (int i = 0; i < inventorySlotViews.Count; i++)
        {
            RuntimeItemData assign = i < itemsToPlace.Count ? itemsToPlace[i] : null;
            
            inventorySlotItemRefs[i] = assign;
            inventorySlotViews[i].SetItem(assign);
        }
    }

    private void HandleItemSwap(ItemSlotView fromSlot, ItemSlotView toSlot)
{
    if (fromSlot == null || toSlot == null) return;

    int fromSellIndex = view.GetSlotIndex(fromSlot);
    int toSellIndex = view.GetSlotIndex(toSlot);

    int fromInvIndex = inventorySlotViews != null ? inventorySlotViews.IndexOf(fromSlot) : -1;
    int toInvIndex = inventorySlotViews != null ? inventorySlotViews.IndexOf(toSlot) : -1;

    // 1) 売り場内の入れ替え
    if (fromSellIndex != -1 && toSellIndex != -1)
    {
        Debug.Log($"売り場内スワップ: {fromSellIndex} <-> {toSellIndex}");
        SwapSellSlots(fromSellIndex, toSellIndex);
        return;
    }

    // 2) 在庫内の入れ替え
    if (fromInvIndex != -1 && toInvIndex != -1)
    {
        Debug.Log($"在庫内スワップ: {fromInvIndex} <-> {toInvIndex}");
        var tmp = inventorySlotItemRefs[toInvIndex];
        inventorySlotItemRefs[toInvIndex] = inventorySlotItemRefs[fromInvIndex];
        inventorySlotItemRefs[fromInvIndex] = tmp;

        inventorySlotViews[toInvIndex].SetItem(inventorySlotItemRefs[toInvIndex]);
        inventorySlotViews[fromInvIndex].SetItem(inventorySlotItemRefs[fromInvIndex]);
        return;
    }

    // 3) 在庫 → 売り場（toSlot が売り場）
    if (fromInvIndex != -1 && toSellIndex != -1)
    {
        Debug.Log($"在庫 → 売り場: invIndex={fromInvIndex}, sellIndex={toSellIndex}");
        MoveInventoryToSell(fromInvIndex, toSellIndex);
        return;
    }

    // 4) 売り場 → 在庫（fromSlot が売り場）
    if (fromSellIndex != -1 && toInvIndex != -1)
    {
        Debug.Log($"売り場 → 在庫: sellIndex={fromSellIndex}, invIndex={toInvIndex}");
        MoveSellToInventory(fromSellIndex, toInvIndex);
        return;
    }

    Debug.LogWarning("HandleItemSwap: どの領域でもないスワップが発生しました（無視）");
}

/// <summary>
/// 売り場内のスロット入れ替え（null も含む）
/// </summary>
private void SwapSellSlots(int indexA, int indexB)
{
    // リストを必要なサイズまで拡張
    EnsureSellListSize(Mathf.Max(indexA, indexB) + 1);

    var tmp = _model.ItemsForSale[indexB];
    _model.ItemsForSale[indexB] = _model.ItemsForSale[indexA];
    _model.ItemsForSale[indexA] = tmp;

    RefreshSellDisplay();
}

/// <summary>
/// 在庫から売り場へ移動（交換）
/// </summary>
private void MoveInventoryToSell(int invIndex, int sellIndex)
{
    var invItem = inventorySlotItemRefs[invIndex];
    if (invItem == null) return; // 空スロットからはドラッグできない

    // 売り場リストを必要なサイズまで拡張
    EnsureSellListSize(sellIndex + 1);

    // 売り場の現在のアイテム（null の可能性あり）
    var sellItem = _model.ItemsForSale[sellIndex];

    // 入れ替え
    _model.ItemsForSale[sellIndex] = invItem;
    inventorySlotItemRefs[invIndex] = sellItem;

    RefreshSellDisplay();
    inventorySlotViews[invIndex].SetItem(sellItem);
}

/// <summary>
/// 売り場から在庫へ移動（交換）
/// </summary>
private void MoveSellToInventory(int sellIndex, int invIndex)
{
    // 売り場のアイテムが存在するか確認
    if (sellIndex >= _model.ItemsForSale.Count) return;

    var sellItem = _model.ItemsForSale[sellIndex];
    if (sellItem == null) return; // 空スロットからはドラッグできない

    var invItem = inventorySlotItemRefs[invIndex];

    // 入れ替え
    _model.ItemsForSale[sellIndex] = invItem;
    inventorySlotItemRefs[invIndex] = sellItem;

    RefreshSellDisplay();
    inventorySlotViews[invIndex].SetItem(sellItem);
}

/// <summary>
/// 売り場リストを指定サイズまで null で拡張
/// </summary>
private void EnsureSellListSize(int requiredSize)
{
    while (_model.ItemsForSale.Count < requiredSize)
    {
        _model.ItemsForSale.Add(null);
    }
}

/// <summary>
/// 売り場の表示を更新
/// </summary>
private void RefreshSellDisplay()
{
    view.DisplayItems(_model.ItemsForSale);
    _model.OnItemsReordered.OnNext(Unit.Default);
}


    private void OnDestroy()
    {
        _presenter?.Dispose();
        _battleDisposables.Dispose();
        ItemSlotView.OnItemDropped -= HandleItemSwap;
    }
}
