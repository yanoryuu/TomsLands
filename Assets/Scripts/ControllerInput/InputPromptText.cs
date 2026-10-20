using R3;
using TMPro;
using UnityEngine;

/// <summary>
/// 操作ヒントの文字（例「[E] しらべる」）を、最後に使った入力機器に合わせて差し替える。
/// パッドなら決定ボタンの表記（Xbox「A」/ PlayStation「×」/ Nintendo「B」）、キーボード・マウスなら元の文字のまま。
/// 元の文字の keyboardKey 部分（既定 "[E]"）だけを置き換えるので、ヒントの文言はシーン側で決める。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class InputPromptText : MonoBehaviour
{
    [Tooltip("元の文字のうち、パッドのボタン表記に置き換える部分")]
    [SerializeField] private string keyboardKey = "[E]";

    private TMP_Text label;
    private string keyboardText;
    private readonly CompositeDisposable disposables = new();

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        keyboardText = label != null ? label.text : string.Empty;
    }

    private void OnEnable()
    {
        ControllerInput.IsGamepad.Subscribe(_ => Refresh()).AddTo(disposables);
        ControllerInput.Style.Subscribe(_ => Refresh()).AddTo(disposables);
    }

    private void OnDisable() => disposables.Clear();

    private void OnDestroy() => disposables.Dispose();

    private void Refresh()
    {
        if (label == null || string.IsNullOrEmpty(keyboardText)) return;
        if (!ControllerInput.IsGamepad.CurrentValue || string.IsNullOrEmpty(keyboardKey) || !keyboardText.Contains(keyboardKey))
        {
            label.text = keyboardText;
            return;
        }
        label.text = keyboardText.Replace(keyboardKey, $"[{SubmitButtonLabel(ControllerInput.Style.CurrentValue)}]");
    }

    /// <summary>決定（下のボタン）の表記。</summary>
    public static string SubmitButtonLabel(GamepadStyle style) => style switch
    {
        GamepadStyle.PlayStation => "×",
        GamepadStyle.Nintendo => "B",
        _ => "A",
    };
}
