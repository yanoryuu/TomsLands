using UnityEngine.SceneManagement;

/// <summary>
/// 設定画面（加算シーン「Setting」）を開く窓口。
/// HUD の歯車・店ホームの Start / Esc など、店の中のどこから開いても同じガードを通す
/// （ロード中の連打や、開いている最中の多重ロードを防ぐ）。
/// </summary>
public static class SettingSceneOpener
{
    public const string SceneName = "Setting";

    private static bool loading;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => loading = false;

    /// <summary>設定画面が開いているか（ロード中も含む）。</summary>
    public static bool IsOpen => loading || SceneManager.GetSceneByName(SceneName).isLoaded;

    /// <summary>設定画面を開く。既に開いている・ロード中なら何もしない。開き始めたら true。</summary>
    public static bool Open()
    {
        if (IsOpen) return false;
        var op = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
        if (op == null) return false;
        loading = true;
        op.completed += _ => loading = false;
        return true;
    }
}
