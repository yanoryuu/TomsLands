/// <summary>
/// 地図の名札1枚ぶんの表示データ。MapPresenter が組み立てて MapView に渡す。
/// 開示ルール（Lv/確率は情報購入、弱点は DungeonIntelModel）は Presenter 側で解決済みの文字列にする。
/// </summary>
public class MapDungeonDisplay
{
    public DungeonName Key;
    public string Name;
    public string LevelText;       // "Lv.3" / "Lv.???"
    public string WeaknessText;    // "弱点:火" / "弱点:?"
    public string ProbabilityText; // クリア見込み "有利"（色付き）/ "？"
    public DungeonStatus Status;

    /// <summary>勇者の次の行き先か（次の1ダンジョンだけ true）。</summary>
    public bool IsNext;
    /// <summary>NEXT リボンの右側の文言（"あと2ターン" / "本日配信"）。</summary>
    public string NextText;

    /// <summary>次の行き先の、その次の行き先か（その1ダンジョンだけ true。次と同じなら IsNext を優先）。</summary>
    public bool IsAfterNext;
}
