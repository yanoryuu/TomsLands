using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// アタッチした GameObject 以下の全 Button に決定音を自動登録する。
/// Canvas ルートにアタッチするだけでシーン内の全ボタンに対応する。
/// </summary>
[DisallowMultipleComponent]
public class ButtonSoundHandler : MonoBehaviour
{
    [SerializeField] private string seKey = "共通SE/SE_選択_決定";

    private void Start()
    {
        RegisterButtons(GetComponentsInChildren<Button>(includeInactive: true));
    }

    /// <summary>動的に生成したボタンを追加登録する</summary>
    public void RegisterButtons(Button[] buttons)
    {
        foreach (var button in buttons)
            button.onClick.AddListener(PlaySE);
    }

    private void PlaySE()
    {
        SoundManager.Instance?.PlaySE(seKey);
    }
}
