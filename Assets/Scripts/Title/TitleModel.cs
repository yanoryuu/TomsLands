/// <summary>
/// タイトル画面内の遷移状態と新規ゲーム設定を保持する。
/// </summary>
public sealed class TitleModel
{
    public TitleType CurrentScreen { get; private set; } = TitleType.Start;
    public GameModeId SelectedDifficulty { get; private set; } = GameModeId.Short;

    /// <summary>セーブデータ画面の用途（続きから / ニューゲームの保存先選択）。</summary>
    public SaveDataPanelMode PanelMode { get; private set; } = SaveDataPanelMode.Load;

    public void ChangeScreen(TitleType screen)
    {
        CurrentScreen = screen;
    }

    public void SelectDifficulty(GameModeId difficulty)
    {
        SelectedDifficulty = difficulty;
    }

    public void SetPanelMode(SaveDataPanelMode mode)
    {
        PanelMode = mode;
    }
}

public enum TitleType
{
    Start,
    ContinueOrNewGame,
    SelectDifficulty,
    SaveData
}

/// <summary>セーブデータ画面の用途。</summary>
public enum SaveDataPanelMode
{
    /// <summary>続きから：既存スロットを選んでロードする。</summary>
    Load,
    /// <summary>ニューゲーム：保存先スロットを選ぶ（使用中なら上書き確認）。</summary>
    NewGameSlot
}
