/// <summary>
/// ゲーム全体の大きなステート（TomsShopシーン内）
/// タイトルは別シーン（Start.unity）、準備は別シーン（Preparation.unity）で管理
/// </summary>
public enum GamePhase
{
    TomsShop,     // トムの店フェーズ：店内の各画面を含む
    Result,       // リザルトフェーズ：勝敗判定、資産計算
    Setting,      // 設定画面（どこからでもアクセス可能）
}