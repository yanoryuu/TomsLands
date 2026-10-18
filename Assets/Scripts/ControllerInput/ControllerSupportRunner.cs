using R3;
using UnityEngine;

/// <summary>
/// ControllerInput / UIFocusManager を毎フレーム回す常駐オブジェクト。
/// 起動時に自動生成される（シーン配置不要）。
/// </summary>
[DefaultExecutionOrder(-500)]
public sealed class ControllerSupportRunner : MonoBehaviour
{
    private static ControllerSupportRunner instance;
    private readonly CompositeDisposable disposables = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;
        var go = new GameObject("[ControllerSupport]");
        instance = go.AddComponent<ControllerSupportRunner>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        // シーンの PlayerInput が有効になる前に、軸が張り付いた汎用ジョイスティックを止めておく
        ControllerInput.InstallJoystickFilter();
        ControllerInput.Device
            .Skip(1)
            .Subscribe(UIFocusManager.OnDeviceChanged)
            .AddTo(disposables);
        ControllerInput.OnButtonPressed
            .Subscribe(UIFocusManager.HandleButton)
            .AddTo(disposables);
    }

    private void Update()
    {
        ControllerInput.Tick();
        UIFocusManager.HandleCancelInput();
    }

    private void LateUpdate()
    {
        UIFocusManager.Tick();
    }

    private void OnDestroy()
    {
        disposables.Dispose();
        if (instance == this)
        {
            instance = null;
            ControllerInput.UninstallJoystickFilter();
        }
    }
}
