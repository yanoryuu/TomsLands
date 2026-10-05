using UnityEngine;

/// <summary>
/// 金融システムのバランス設定。ShopEconomySettings と同じ流儀
/// （RemoteBalance の "finance" 区画で配信上書き可能）。
/// </summary>
[CreateAssetMenu(fileName = "FinanceSettings", menuName = "ScriptableObjects/Finance/FinanceSettings")]
public class FinanceSettings : ScriptableObject
{
    [Header("ファンド手数料")]
    [Tooltip("購入手数料率（0.02 = 2%上乗せ）")]
    public float fundBuyFeeRate = 0.02f;
    [Tooltip("解約手数料率（0.02 = 2%差し引き）")]
    public float fundSellFeeRate = 0.02f;

    [Header("強制売却（借金返済時の救済）")]
    [Tooltip("強制売却時にファンド解約手数料へ上乗せされる割増率")]
    public float forcedSaleExtraFeeRate = 0.10f;
    [Tooltip("債券を満期前に強制解約したとき戻る元本の割合（利息なし）")]
    public float bondEarlyRedemptionRate = 0.85f;

    [Header("チャート")]
    [Tooltip("ファンド基準価額の履歴保持ターン数")]
    public int navHistoryCapacity = 12;
}
