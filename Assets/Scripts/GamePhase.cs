/// <summary>
/// ゲーム全体の大きなステート
/// </summary>
public enum GamePhase
{
    Title,        // タイトルフェーズ：タイトル画面、ロード画面
    Preparation,  // 準備フェーズ：前回の報酬でブースト選択など
    TomsShop,     // トムの店フェーズ：店内の各画面を含む
    Streaming,    // 配信フェーズ：配信準備・配信中・配信リザルトを含む
    Result,       // リザルトフェーズ：勝敗判定、資産計算
    Setting,      // 設定画面（どこからでもアクセス可能）
}