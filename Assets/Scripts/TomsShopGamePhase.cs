/// <summary>
/// トムの店フェーズ内のサブステート
/// </summary>
public enum TomsShopGamePhase
{
    Shop,            // トムの店メイン画面
    BlackSmith,      // 鍛冶屋：武器・防具を仕入れる
    ToolShop,        // 道具屋：道具を仕入れる
    Broker,          // 情報屋：情報を買う
    Map,             // マップ：ダンジョン選択
    DungeonLevelUp,  // ダンジョンレベルアップ：ダンジョンのレベルを上げる
    TurnEndSummary,  // ターン終了サマリー：売上・トレンド・次配信までの日数
    Advertisement,   // 広告購入：マーケティング広告を購入してステータスを上げる
}
