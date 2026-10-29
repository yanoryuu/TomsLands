using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>マシンの効果種別。v1 は「毎日発動型」と「常時バフ型」のみ。</summary>
public enum ShopMachineEffectType
{
    /// <summary>毎朝、固定ゴールドを産む（お金製造機）。</summary>
    DailyMoney,
    /// <summary>毎朝、指定アイテムを在庫に追加する（アイテム自動生成機）。</summary>
    DailyItem,
    /// <summary>営業売上に倍率ボーナス（客寄せマシン）。</summary>
    RevenueMultiplier,
    /// <summary>
    /// 需要の下限を底上げする（旧・冷蔵ケース）。2026-10-29 に冷蔵ケースは UndisplayedDemandKeep へ移ったので
    /// 同梱のマシンでは使っていない（配信・旧データで指定されたときのために効果の集計だけ残す）。
    /// </summary>
    DemandFloorBonus,
    /// <summary>陳列していない品の、毎朝の需要の落ち込みを減らす（冷蔵ケース。値 0.3 = 落ち込みが 7 割になる）。</summary>
    UndisplayedDemandKeep,
}

/// <summary>設置カテゴリ。v1 は Any 固定運用（将来の床/壁/カウンター分離用に器だけ用意）。</summary>
public enum ShopMachinePlacementType
{
    Any,
    Floor,
    Wall,
    Counter,
}

/// <summary>
/// 店に設置できるマシンのマスターデータ。
/// 設置枠数は店レベル（ShopLevelSettings.machineSlots）が規定する。
/// 配信: balance.json の "shopMachines"（部分上書き）と "machineLevels"（levels[] の全置換。シート machineLevels）。
/// レベルの表 levels があれば、購入費・効果・壊れやすさは表の値を使う（index 0 = Lv1。Lv1 の cost = 購入費、
/// Lv2 以降の cost = そのレベルへ上げる費用）。表が空なら旧フィールド（cost・dailyMoney など）で Lv1 だけのマシンになる。
/// </summary>
[CreateAssetMenu(fileName = "ShopMachineData", menuName = "ScriptableObjects/ShopMachine/ShopMachineData")]
public class ShopMachineData : ScriptableObject
{
    [Header("共通")]
    public string machineId;
    public string machineName;
    [TextArea] public string description;
    public Sprite icon;
    [Tooltip("店に設置したときの見た目スプライト（null なら icon で代用）")]
    public Sprite shopSprite;
    [Tooltip("購入価格（levels が空のときだけ使う。levels があれば Lv1 の cost が購入費）")]
    public int cost = 4000;
    public ShopMachinePlacementType placementType = ShopMachinePlacementType.Any;
    [Tooltip("購入に必要な店レベル")]
    public int requiredShopLevel = 1;

    [Header("効果")]
    public ShopMachineEffectType effectType = ShopMachineEffectType.DailyMoney;
    [Tooltip("DailyMoney: 毎朝入るゴールド")]
    public int dailyMoney = 800;
    [Tooltip("DailyItem: 毎朝生成するアイテムID（dailyItemSelectable=true のときは初期選択。空なら未選択で開始）")]
    public string dailyItemId = "";
    [Tooltip("DailyItem: 毎朝生成する個数（dailyItemSelectable=false の固定生産時のみ使用）")]
    public int dailyItemCount = 1;
    [Tooltip("DailyItem: true ならプレイヤーが生産アイテムを選べる（鍛冶屋レベルで解放済みのアイテムから）")]
    public bool dailyItemSelectable = false;
    [Tooltip("DailyItem(選択式): 毎朝この金額ぶんの製造が進み、選択アイテムの基準価格に達するごとに1個生産する。" +
             "高い武器ほどゆっくり作られるため、何を選んでもバランスが保たれる")]
    public int dailyProductionBudget = 1000;
    [Tooltip("RevenueMultiplier: 営業売上への加算倍率（0.05 = +5%）")]
    public float revenueMultiplierBonus = 0.05f;
    [Tooltip("DemandFloorBonus: 需要下限への加算量（0.05 = 下限+5%）")]
    public float demandFloorBonus = 0.05f;

    [Header("レベル（index 0 = Lv1。配列の長さが最大レベル）")]
    [Tooltip("cost: Lv1 = 購入費 / Lv2 以降 = そのレベルへ上げる費用。effectValue: 種別ごとの効果の値。" +
             "safeDays: 置いて（強化・修理して）から壊れない朝の数。breakChanceStep: その後 1 朝ごとに増える故障率")]
    public List<MachineLevelEntry> levels = new();

    /// <summary>最大レベル（表が空なら 1）。</summary>
    public int MaxLevel => levels != null && levels.Count > 0 ? levels.Count : 1;

    /// <summary>1 始まりのレベルの行（表が空・範囲外なら null）。</summary>
    public MachineLevelEntry GetLevel(int level)
    {
        if (levels == null || levels.Count == 0) return null;
        return levels[Mathf.Clamp(level, 1, levels.Count) - 1];
    }

    /// <summary>そのレベルの行の費用（定価。Lv1 = 購入費、Lv2 以降 = そのレベルへ上げる費用）。</summary>
    public int LevelCost(int level)
    {
        var row = GetLevel(level);
        if (row != null) return Mathf.Max(0, row.cost);
        return level <= 1 ? Mathf.Max(0, cost) : 0;
    }

    /// <summary>購入費（定価）。</summary>
    public int BaseCost => LevelCost(1);

    /// <summary>そのレベルの効果の値（種別ごとの単位。表が空なら旧フィールド）。</summary>
    public float EffectValueAt(int level)
    {
        var row = GetLevel(level);
        if (row != null) return row.effectValue;
        return effectType switch
        {
            ShopMachineEffectType.DailyMoney => dailyMoney,
            ShopMachineEffectType.DailyItem => dailyItemSelectable ? dailyProductionBudget : dailyItemCount,
            ShopMachineEffectType.RevenueMultiplier => revenueMultiplierBonus,
            ShopMachineEffectType.DemandFloorBonus => demandFloorBonus,
            _ => 0f,
        };
    }

    /// <summary>DailyMoney: 毎朝のゴールド。</summary>
    public int DailyMoneyAt(int level) => Mathf.Max(0, Mathf.RoundToInt(EffectValueAt(level)));

    /// <summary>DailyItem（選択式）: 毎朝の製造の進み（G）。</summary>
    public int ProductionBudgetAt(int level) => Mathf.Max(0, Mathf.RoundToInt(EffectValueAt(level)));

    /// <summary>DailyItem（固定生産）: 毎朝の個数。</summary>
    public int ItemCountAt(int level) => Mathf.Max(0, Mathf.RoundToInt(EffectValueAt(level)));

    /// <summary>壊れない朝の数（表が空なら 0 = 壊れやすさの設定なし）。</summary>
    public int SafeDaysAt(int level) => Mathf.Max(0, GetLevel(level)?.safeDays ?? 0);

    /// <summary>壊れない朝を過ぎてから 1 朝ごとに増える故障率（表が空なら 0）。</summary>
    public float BreakChanceStepAt(int level) => Mathf.Max(0f, GetLevel(level)?.breakChanceStep ?? 0f);

    /// <summary>このレベルで壊れることがあるか（safeDays 0・step 0 なら壊れない）。</summary>
    public bool CanBreakAt(int level) => BreakChanceStepAt(level) > 0f;

    /// <summary>
    /// 置いて（強化・修理して）から morning 回目の朝に壊れる確率。
    /// safeDays 回目までは 0、その後は breakChanceStep × (超えた朝の数)。1 で頭打ち。
    /// </summary>
    public float BreakChanceOnMorning(int level, int morning)
    {
        float step = BreakChanceStepAt(level);
        int over = morning - SafeDaysAt(level);
        if (step <= 0f || over <= 0) return 0f;
        return Mathf.Clamp01(step * over);
    }

    /// <summary>効果の1行説明（Lv1。購入UI用）。</summary>
    public string EffectSummary => EffectSummaryAt(1);

    /// <summary>そのレベルの効果の1行説明。</summary>
    public string EffectSummaryAt(int level) => effectType switch
    {
        ShopMachineEffectType.DailyMoney => $"毎朝 +{DailyMoneyAt(level):N0}G",
        ShopMachineEffectType.DailyItem => dailyItemSelectable
            ? $"選んだ武具を毎朝 {ProductionBudgetAt(level):N0}G分製造"
            : $"毎朝 武具 ×{ItemCountAt(level)} を製造",
        ShopMachineEffectType.RevenueMultiplier => $"営業売上 +{EffectValueAt(level):P0}",
        ShopMachineEffectType.DemandFloorBonus => $"全商品の需要の底 +{EffectValueAt(level):P0}",
        ShopMachineEffectType.UndisplayedDemandKeep => $"並べていない品の需要の落ち込み -{Mathf.Clamp01(EffectValueAt(level)):P0}",
        _ => "",
    };

    /// <summary>
    /// 配信値を当てる（"shopMachines" の部分上書き → "machineLevels" の levels[] 全置換の順）。
    /// GameLifetimeScope と AutoPlay のヘッドレスで同じものを使う。
    /// </summary>
    public static List<ShopMachineData> ApplyRemoteBalance(List<ShopMachineData> baked)
    {
        var list = RemoteBalance.ApplyList("shopMachines", baked, m => m.machineId);
        return RemoteBalance.ApplyList("machineLevels", list, m => m.machineId);
    }
}

/// <summary>マシンの1レベルぶんの定義（JsonUtility 対応のフラット構造。シート machineLevels の1行）。</summary>
[Serializable]
public class MachineLevelEntry
{
    [Tooltip("Lv1 = 購入費 / Lv2 以降 = このレベルへ上げる費用（定価。村の職人組合の割引はゲーム側で掛ける）")]
    public int cost;

    [Tooltip("効果の値。DailyMoney = 毎朝の G / DailyItem(選択式) = 毎朝の製造の進み G / RevenueMultiplier = 売上への加算（0.06 = +6%）" +
             " / UndisplayedDemandKeep = 並べていない品の需要の落ち込みを減らす割合（0.3 = 落ち込みが 7 割）")]
    public float effectValue;

    [Tooltip("置いて（強化・修理して）からこの回数の朝までは壊れない")]
    public int safeDays;

    [Tooltip("safeDays を過ぎてから 1 朝ごとに増える故障率（0.15 なら 15% → 30% → …）。0 なら壊れない")]
    public float breakChanceStep;
}
