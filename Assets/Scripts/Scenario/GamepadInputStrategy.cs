using UnityEngine;
using UnityEngine.InputSystem;
using Utage;

/// <summary>
/// Utage の入力にゲームパッドを足した IInputStrategy（Utage 標準の Input System 版の挙動はそのまま継承）。
/// A=次へ / B=ウィンドウを閉じる・バックログを閉じる / LB=バックログ / RB(押しっぱなし)=スキップ。
/// 選択肢の表示中は A を「次へ」にしない（選択肢ボタンの決定と二重に反応させないため）。
/// </summary>
#if ENABLE_INPUT_SYSTEM
public class GamepadInputStrategy : Utage.InputSystem.InputSystemStrategy
#else
public class GamepadInputStrategy : InputManagerStrategy
#endif
{
    /// <summary>選択肢の入力待ち中か（ScenarioPlayer が更新）。</summary>
    public static bool SelectionActive { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Install() => InputUtil.SetInputStrategy(new GamepadInputStrategy());

    public override bool IsInputNextButton()
    {
        if (base.IsInputNextButton()) return true;
        var pad = Gamepad.current;
        return pad != null && !SelectionActive && pad.buttonSouth.wasPressedThisFrame;
    }

    /// <summary>メッセージウィンドウを B で隠している最中か（ScenarioPlayer が設定。A でも復帰させるため）。</summary>
    public static System.Func<bool> IsMessageWindowHidden { get; set; }

    public override bool IsInputGuiClose()
    {
        if (base.IsInputGuiClose()) return true;
        var pad = Gamepad.current;
        if (pad == null) return false;
        if (pad.buttonEast.wasPressedThisFrame) return true;
        // 隠したウィンドウは A でも復帰（Utage はウィンドウ非表示中「閉じる入力」で通常表示に戻す）
        return pad.buttonSouth.wasPressedThisFrame && IsMessageWindowHidden != null && IsMessageWindowHidden();
    }

    public override bool IsInputForceSkip()
    {
        if (base.IsInputForceSkip()) return true;
        var pad = Gamepad.current;
        return pad != null && pad.rightShoulder.isPressed;
    }

    public override bool IsInputOpenBackLog()
    {
        if (base.IsInputOpenBackLog()) return true;
        var pad = Gamepad.current;
        return pad != null && pad.leftShoulder.wasPressedThisFrame;
    }
}
