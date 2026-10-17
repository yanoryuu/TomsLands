using UnityEngine;

/// <summary>
/// 各シーンの EventSystem に付いている。ControllerOverlay（選択枠＋ボタンガイドの常駐 Canvas）を
/// まだ無ければ 1 つだけ生成し、DontDestroyOnLoad で全シーンに持ち越す。
/// どのシーンから Play しても同じ見た目になるように全シーンの EventSystem に付けてある。
/// </summary>
public sealed class ControllerOverlayBootstrap : MonoBehaviour
{
    [SerializeField] private GameObject overlayPrefab;

    private static GameObject instance;

    private void Awake()
    {
        if (instance != null || overlayPrefab == null) return;
        instance = Instantiate(overlayPrefab);
        instance.name = overlayPrefab.name;
        DontDestroyOnLoad(instance);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;
}
