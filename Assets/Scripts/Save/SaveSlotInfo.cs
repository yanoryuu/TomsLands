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
}
