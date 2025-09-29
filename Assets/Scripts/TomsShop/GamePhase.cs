public enum GamePhase
{
    Title,// タイトルフェーズ：タイトル画面、ロード画面
    Preparation,  // 準備フェーズ：仕入れ、勇者への装備、作戦決定
    StreamingSetting, // 配信に持っていくアイテムの選定
    Streaming,       // 戦闘フェーズ：勇者が魔王ダンジョンで戦う
    End,          // エンドフェーズ：勝敗判定、資産計算
    TomsShop,     //　トムの店の中にいる時のUI
    Map,
    BlackSmith,
    ToolShop,
    InfoBroker,
    Setting,
    StreamingResult
}