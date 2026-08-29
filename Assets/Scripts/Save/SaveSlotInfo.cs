/// <summary>
/// タイトル画面のロードパネルでスロットを表示するためのサマリ情報。
/// 実データ（各モデルのJSON）を完全にロードせず、表示に必要な最小限だけを保持する。
/// </summary>
public sealed class SaveSlotInfo
{
    /// <summary>スロット番号（0〜SaveSlotManager.MaxSlots-1）。</summary>
    public int SlotIndex;

    /// <summary>このスロットにセーブデータが存在するか。</summary>
    public bool Exists;

    /// <summary>進行日数（CurrentTurn）。</summary>
    public int Day;

    /// <summary>所持金。</summary>
    public int Gold;

    /// <summary>選択中のゲームモード（難易度）。</summary>
    public GameModeId Mode;

    // --- プロフィール（メタ進行）情報。ラン終了後もスロットに残る ---

    /// <summary>プロフィール（メタ進行 or 進行中ラン）が存在するか。</summary>
    public bool HasProfile;

    /// <summary>メタ通貨（信用）の残高。</summary>
    public int MetaCurrency;

    /// <summary>総ラン数。</summary>
    public int TotalRuns;

    /// <summary>クリア時のベストランク（未クリアなら空文字）。</summary>
    public string BestRank = "";
}
