public enum GamePhase
{
    Preparation,  // 準備フェーズ：仕入れ、勇者への装備、作戦決定
    StreamingSetting,
    Streaming,       // 戦闘フェーズ：勇者が魔王ダンジョンで戦う
    End,          // エンドフェーズ：勝敗判定、資産計算
    TomsShop,     //　トムの店の中にいる時のUI
}