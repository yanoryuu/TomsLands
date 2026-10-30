using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// #4 ボタンの手ざわり（<see cref="ButtonFeel"/>）を全ボタンへ自動で付ける。ButtonSoundHandler と同じ方式:
/// シーン読み込み時に一番外側の Canvas へこれを付け、0.25 秒ごとに階層の数が変わっていたら付け直す（後から生成した行のボタンも拾う）。
/// 既に ButtonFeel が付いているボタン（Disable したものを含む）には何もしない。
/// コードで生成した別ルートの Canvas は <see cref="InstallUnder"/> を呼ぶ。
/// </summary>
[DisallowMultipleComponent]
public sealed class ButtonFeelInstaller : MonoBehaviour
{
    private const float RescanInterval = 0.25f;

    private int lastHierarchyCount = -1;
    private float nextCheckTime;

    /// <summary>root 以下の全ボタンにすぐ付け、以後の増加も見張る。</summary>
    public static void InstallUnder(GameObject root)
    {
        if (root == null) return;
        if (!root.TryGetComponent<ButtonFeelInstaller>(out var installer)) installer = root.AddComponent<ButtonFeelInstaller>();
        installer.Scan();
    }

    private void Start() => Scan();

    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        if (now < nextCheckTime) return;
        nextCheckTime = now + RescanInterval;
        if (transform.hierarchyCount != lastHierarchyCount) Scan();
    }

    private void Scan()
    {
        lastHierarchyCount = transform.hierarchyCount;
        foreach (var b in GetComponentsInChildren<Button>(includeInactive: true))
        {
            if (b.TryGetComponent<ButtonFeel>(out _) || !ButtonFeel.IsEligible(b)) continue;
            b.gameObject.AddComponent<ButtonFeel>();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallOnSceneLoaded()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null) continue;
                // 一番外側の Canvas だけ（入れ子は外側が拾う）
                var parentCanvas = canvas.transform.parent != null ? canvas.transform.parent.GetComponentInParent<Canvas>(true) : null;
                if (parentCanvas != null) continue;
                if (canvas.GetComponentInParent<ButtonFeelInstaller>(true) != null) continue;
                if (canvas.GetComponentInChildren<Button>(true) == null) continue;
                canvas.gameObject.AddComponent<ButtonFeelInstaller>();
            }
        }
    }
}
