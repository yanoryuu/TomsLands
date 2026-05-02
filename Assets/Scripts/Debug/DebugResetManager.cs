using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 展示用デバッグリセット機能。
/// ESCキーを連続10回押すとセーブデータを削除してタイトルへ戻る。
/// RuntimeInitializeOnLoadMethod によりゲーム起動時に自動生成され、
/// DontDestroyOnLoad で全シーンにまたがって動作する。
/// </summary>
public class DebugResetManager : MonoBehaviour
{
    private const int RequiredPressCount = 10;
    private const float IntervalThreshold = 2f; // この秒数以上間が空いたらカウントリセット
    private const string TitleSceneName = "TitleScene";

    private int _pressCount;
    private float _lastPressTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var go = new GameObject("[DebugResetManager]");
        go.AddComponent<DebugResetManager>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        float now = Time.unscaledTime;

        if (now - _lastPressTime > IntervalThreshold)
            _pressCount = 0;

        _pressCount++;
        _lastPressTime = now;

        Debug.Log($"[DebugReset] ESC {_pressCount} / {RequiredPressCount}");

        if (_pressCount >= RequiredPressCount)
            TriggerReset();
    }

    private void TriggerReset()
    {
        Debug.Log("[DebugReset] リセット実行 → セーブ削除 → タイトルへ");
        _pressCount = 0;
        SaveSystem.Delete();
        SceneManager.LoadScene(TitleSceneName);
    }
}
