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
    /// <summary>需要の下限を底上げする（冷蔵ケース等）。</summary>
    DemandFloorBonus,
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
    [Tooltip("購入価格。回収4〜6ターンを目安に設定する")]
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

    /// <summary>効果の1行説明（購入UI用）。</summary>
    public string EffectSummary => effectType switch
    {
        ShopMachineEffectType.DailyMoney => $"毎朝 +{dailyMoney:N0}G",
        ShopMachineEffectType.DailyItem => dailyItemSelectable
            ? $"選んだ武具を毎朝製造（{dailyProductionBudget:N0}G分/日）"
            : $"毎朝 {dailyItemId} ×{dailyItemCount} を生成",
        ShopMachineEffectType.RevenueMultiplier => $"営業売上 +{revenueMultiplierBonus:P0}",
        ShopMachineEffectType.DemandFloorBonus => $"全商品の需要下限 +{demandFloorBonus:P0}",
        _ => "",
    };
}
