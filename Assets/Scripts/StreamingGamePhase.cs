/// <summary>
/// 配信フェーズ内のサブステート
/// </summary>
public enum StreamingGamePhase
{
    StreamingSetting, // 配信準備：品出しする武器を選択
    Streaming,        // 配信中：リアルタイムで売買
    StreamingResult,  // 配信リザルト：結果表示
}
