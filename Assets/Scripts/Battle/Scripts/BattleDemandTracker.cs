using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FightScene 専用の需要度管理。
/// TomsShop の Demand とは完全に独立した BattleDemand を管理する。
/// 戦闘終了時に破棄され、TomsShop 側には影響しない。
/// </summary>
public class BattleDemandTracker
{
    /// <summary>
    /// アイテムごとの戦闘中需要度（0.05〜1.0）
    /// </summary>
    private readonly Dictionary<string, float> _battleDemands = new();

    /// <summary>
    /// アイテムごとの連続販売ターン数（案C: トレンド用）
    /// </summary>
    private readonly Dictionary<string, int> _consecutiveSoldTurns = new();

    /// <summary>
    /// アイテムごとの連続未販売ターン数（案C: トレンド用）
    /// </summary>
    private readonly Dictionary<string, int> _consecutiveUnsoldTurns = new();

    /// <summary>
    /// 今ターンで売れたアイテムIDを記録（ターン末にリセット）
    /// </summary>
    private readonly HashSet<string> _soldThisTurn = new();

    // --- 案C: トレンド設定 ---
    private readonly float _buzzBonus2Turn;        // 2ターン連続販売時の需要UP
    private readonly float _buzzBonus3PlusTurn;    // 3ターン以上連続販売時の需要UP
    private readonly float _unsoldPenalty;          // 2ターン以上未販売時の需要DOWN

    // --- 案E: 価格逆相関設定 ---
    private readonly float _highPriceThreshold;    // この倍率以上で「高値」判定（例: 2.0 = 元値の2倍）
    private readonly float _lowPriceThreshold;     // この倍率以下で「安値」判定（例: 0.5 = 元値の半分）
    private readonly float _highPriceDemandDecay;  // 高値時の需要減衰率（例: 0.92）
    private readonly float _lowPriceDemandGrowth;  // 安値時の需要増加率（例: 1.08）

    // --- 需要上下限 ---
    private const float MinDemand = 0.05f;
    private const float MaxDemand = 1.0f;

    public BattleDemandTracker(
        float buzzBonus2Turn = 0.05f,
        float buzzBonus3PlusTurn = 0.10f,
        float unsoldPenalty = 0.08f,
        float highPriceThreshold = 2.0f,
        float lowPriceThreshold = 0.5f,
        float highPriceDemandDecay = 0.92f,
        float lowPriceDemandGrowth = 1.08f)
    {
        _buzzBonus2Turn = buzzBonus2Turn;
        _buzzBonus3PlusTurn = buzzBonus3PlusTurn;
        _unsoldPenalty = unsoldPenalty;
        _highPriceThreshold = highPriceThreshold;
        _lowPriceThreshold = lowPriceThreshold;
        _highPriceDemandDecay = highPriceDemandDecay;
        _lowPriceDemandGrowth = lowPriceDemandGrowth;
    }

    /// <summary>
    /// 初期化。RuntimeItemData の Demand をコピーして BattleDemand を設定する。
    /// </summary>
    public void Initialize(IEnumerable<RuntimeItemData> items)
    {
        _battleDemands.Clear();
        _consecutiveSoldTurns.Clear();
        _consecutiveUnsoldTurns.Clear();

        if (items == null) return;

        foreach (var item in items)
        {
            if (item == null) continue;
            if (_battleDemands.ContainsKey(item.ItemId)) continue;
            _battleDemands[item.ItemId] = Mathf.Clamp(item.Demand.Value, MinDemand, MaxDemand);
            _consecutiveSoldTurns[item.ItemId] = 0;
            _consecutiveUnsoldTurns[item.ItemId] = 0;
        }
    }

    /// <summary>
    /// 後から追加されたアイテムを登録する（売り場にドラッグされた時など）。
    /// </summary>
    public void EnsureTracked(RuntimeItemData item)
    {
        if (item == null) return;
        if (_battleDemands.ContainsKey(item.ItemId)) return;
        _battleDemands[item.ItemId] = Mathf.Clamp(item.Demand.Value, MinDemand, MaxDemand);
        _consecutiveSoldTurns[item.ItemId] = 0;
        _consecutiveUnsoldTurns[item.ItemId] = 0;
    }

    /// <summary>
    /// 指定アイテムの BattleDemand を取得する。
    /// </summary>
    public float GetDemand(string itemId)
    {
        return _battleDemands.TryGetValue(itemId, out float demand) ? demand : 0.3f;
    }

    /// <summary>
    /// 指定アイテムの BattleDemand を直接変更する。
    /// </summary>
    public void AdjustDemand(string itemId, float delta)
    {
        if (!_battleDemands.ContainsKey(itemId)) return;
        _battleDemands[itemId] = Mathf.Clamp(_battleDemands[itemId] + delta, MinDemand, MaxDemand);
    }

    /// <summary>
    /// 指定タイプの全アイテムの BattleDemand を変更する。
    /// </summary>
    public void AdjustDemandByType(ItemTypeData.ItemType targetType, float delta, List<RuntimeItemData> saleItems, List<RuntimeItemData> inventoryItems)
    {
        AdjustDemandInList(saleItems, targetType, delta);
        AdjustDemandInList(inventoryItems, targetType, delta);
    }

    /// <summary>
    /// 属性相性に基づいて BattleDemand を変動させる（案A）。
    /// </summary>
    public void AdjustDemandByElementAffinity(ElementType enemyElement, float effectiveDelta, float weakDelta,
        List<RuntimeItemData> saleItems, List<RuntimeItemData> inventoryItems)
    {
        AdjustDemandByAffinityInList(saleItems, enemyElement, effectiveDelta, weakDelta);
        AdjustDemandByAffinityInList(inventoryItems, enemyElement, effectiveDelta, weakDelta);
    }

    /// <summary>
    /// アイテムが売れたことを記録する（ターン中に呼ぶ）。
    /// </summary>
    public void RecordSale(string itemId)
    {
        _soldThisTurn.Add(itemId);
    }

    /// <summary>
    /// ターン末に呼び出す。連続販売/未販売カウンタを更新し、案Cのトレンド効果を適用する。
    /// </summary>
    public void ProcessEndOfTurn(List<RuntimeItemData> saleItems)
    {
        if (saleItems == null) return;

        foreach (var item in saleItems)
        {
            if (item == null) continue;
            EnsureTracked(item);

            string id = item.ItemId;
            bool soldThisTurn = _soldThisTurn.Contains(id);

            if (soldThisTurn)
            {
                // 連続販売カウンタを加算、未販売カウンタをリセット
                _consecutiveSoldTurns[id] = _consecutiveSoldTurns.GetValueOrDefault(id, 0) + 1;
                _consecutiveUnsoldTurns[id] = 0;

                int streak = _consecutiveSoldTurns[id];
                if (streak >= 3)
                {
                    AdjustDemand(id, _buzzBonus3PlusTurn);
                    Debug.Log($"[需要C] {id} がバズ中！({streak}ターン連続) BattleDemand +{_buzzBonus3PlusTurn:F2} → {GetDemand(id):F2}");
                }
                else if (streak >= 2)
                {
                    AdjustDemand(id, _buzzBonus2Turn);
                    Debug.Log($"[需要C] {id} 口コミ発生({streak}ターン連続) BattleDemand +{_buzzBonus2Turn:F2} → {GetDemand(id):F2}");
                }
            }
            else
            {
                // 未販売カウンタを加算、販売カウンタをリセット
                _consecutiveUnsoldTurns[id] = _consecutiveUnsoldTurns.GetValueOrDefault(id, 0) + 1;
                _consecutiveSoldTurns[id] = 0;

                int unsoldStreak = _consecutiveUnsoldTurns[id];
                if (unsoldStreak >= 2)
                {
                    AdjustDemand(id, -_unsoldPenalty);
                    Debug.Log($"[需要C] {id} が話題から消えた({unsoldStreak}ターン未販売) BattleDemand -{_unsoldPenalty:F2} → {GetDemand(id):F2}");
                }
            }
        }

        // 今ターンの販売記録をリセット
        _soldThisTurn.Clear();
    }

    /// <summary>
    /// 毎ターン呼び出す。価格と需要の逆相関を適用する（案E）。
    /// 高値のアイテムは需要減、安値のアイテムは需要増。
    /// </summary>
    public void ApplyPriceDemandCorrelation(List<RuntimeItemData> saleItems, List<RuntimeItemData> inventoryItems, ItemModel itemModel)
    {
        ApplyCorrelationToList(saleItems, itemModel);
        ApplyCorrelationToList(inventoryItems, itemModel);
    }

    // ========================================
    // 内部メソッド
    // ========================================

    private void AdjustDemandInList(List<RuntimeItemData> items, ItemTypeData.ItemType targetType, float delta)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null || item.ItemType != targetType) continue;
            EnsureTracked(item);
            AdjustDemand(item.ItemId, delta);
        }
    }

    private void AdjustDemandByAffinityInList(List<RuntimeItemData> items, ElementType enemyElement, float effectiveDelta, float weakDelta)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null) continue;
            EnsureTracked(item);

            if (ElementAttributeMapper.IsEffective(item.ItemAttribute, enemyElement))
            {
                AdjustDemand(item.ItemId, effectiveDelta);
            }
            else if (ElementAttributeMapper.IsWeak(item.ItemAttribute, enemyElement))
            {
                AdjustDemand(item.ItemId, weakDelta);
            }
        }
    }

    private void ApplyCorrelationToList(List<RuntimeItemData> items, ItemModel itemModel)
    {
        if (items == null || itemModel == null) return;
        foreach (var item in items)
        {
            if (item == null) continue;
            EnsureTracked(item);

            var master = itemModel.GetMasterItem(item.ItemId);
            if (master == null || master.basePrice <= 0) continue;

            float priceRatio = (float)item.CurrentPrice.Value / master.basePrice;

            if (priceRatio >= _highPriceThreshold)
            {
                // 高値 → 需要減衰
                float current = GetDemand(item.ItemId);
                float newDemand = Mathf.Clamp(current * _highPriceDemandDecay, MinDemand, MaxDemand);
                _battleDemands[item.ItemId] = newDemand;
            }
            else if (priceRatio <= _lowPriceThreshold)
            {
                // 安値 → 需要増加
                float current = GetDemand(item.ItemId);
                float newDemand = Mathf.Clamp(current * _lowPriceDemandGrowth, MinDemand, MaxDemand);
                _battleDemands[item.ItemId] = newDemand;
            }
        }
    }
}

