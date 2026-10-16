#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// 【エディタ専用・開発用】GameplayRecordingSignals の毎フレーム処理（入力監視と LifetimeScope への再バインド）。
/// GameplayRecordingSignals.Activate() が DontDestroyOnLoad の GameObject（[GameplayRecordingSignals]） に付けて使う。手動で配置しない。
/// </summary>
[AddComponentMenu("")]
public class GameplayRecordingSignalsDriver : MonoBehaviour
{
    private int _bindFramesLeft;
    private bool _bindSkipInitial;
    private Vector3 _lastMousePosition;

    public void RequestBind(int frames, bool skipInitial)
    {
        // 最後の要求が優先（Activate 時=初回通知スキップ、以降のシーンロード=スキップしない）
        _bindFramesLeft = frames;
        _bindSkipInitial = skipInitial;
    }

    private void Start()
    {
        _lastMousePosition = Input.mousePosition;
    }

    private void Update()
    {
        if (!GameplayRecordingSignals.IsActive) return;

        if (_bindFramesLeft > 0)
        {
            _bindFramesLeft--;
            GameplayRecordingSignals.TryBindScopes(_bindSkipInitial);
        }

        var mouse = Input.mousePosition;
        if (Input.anyKey || Input.anyKeyDown || Input.touchCount > 0
            || Input.mouseScrollDelta.sqrMagnitude > 0f
            || (mouse - _lastMousePosition).sqrMagnitude > 0.01f)
        {
            GameplayRecordingSignals.NotifyInput();
        }
        _lastMousePosition = mouse;
    }
}
#endif
