using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;

/// <summary>最後に使われた入力デバイスの種類。</summary>
public enum InputDeviceKind
{
    Mouse = 0,
    Keyboard = 1,
    Gamepad = 2,
}

/// <summary>ボタン表記の系統（ガイド表示の切替用）。</summary>
public enum GamepadStyle
{
    Xbox = 0,
    PlayStation = 1,
    Nintendo = 2,
}

/// <summary>
/// 論理ボタン。物理位置（Xbox 配置）で表す。A=下 / B=右 / X=左 / Y=上。
/// </summary>
public enum ControllerButton
{
    None = 0,
    A = 1,
    B = 2,
    X = 3,
    Y = 4,
    LB = 5,
    RB = 6,
    LT = 7,
    RT = 8,
    Start = 9,
    Select = 10,
    DPad = 11,
    LStick = 12,
    RStick = 13,
}

/// <summary>
/// 入力デバイス検知とパッド用 UI アクションの窓口（静的サービス）。
/// 毎フレームの更新は <see cref="ControllerSupportRunner"/> が行う（自動生成・DontDestroyOnLoad）。
///
/// 使い方:
///   ControllerInput.IsGamepad.Subscribe(on => ...).AddTo(this);
///   ControllerInput.OnButtonPressed.Where(b => b == ControllerButton.Y).Subscribe(...)
/// 画面単位のボタン処理は UIFocusScope の UnityEvent / Observable を使う方が簡単。
/// </summary>
public static class ControllerInput
{
    // InputSystem_Actions.inputactions の UI マップのアクション名
    public const string ActionSubmit = "UI/Submit";
    public const string ActionCancel = "UI/Cancel";
    public const string ActionTabLeft = "UI/TabLeft";
    public const string ActionTabRight = "UI/TabRight";
    public const string ActionButtonX = "UI/ButtonX";
    public const string ActionButtonY = "UI/ButtonY";
    public const string ActionMenu = "UI/Menu";

    private const float StickThreshold = 0.5f;
    private const float MouseMoveThresholdSqr = 16f;

    private static readonly ReactiveProperty<InputDeviceKind> device = new(InputDeviceKind.Mouse);
    private static readonly ReactiveProperty<bool> isNavigating = new(false);
    private static readonly ReactiveProperty<bool> isGamepad = new(false);
    private static readonly ReactiveProperty<GamepadStyle> style = new(GamepadStyle.Xbox);
    private static readonly Subject<ControllerButton> buttonPressed = new();
    private static readonly List<InputDevice> disabledJoysticks = new();

    private static InputAction submitAction, cancelAction, tabLeftAction, tabRightAction, buttonXAction, buttonYAction, menuAction;
    private static InputActionAsset resolvedAsset;
    private static bool debugOverride;

    /// <summary>最後に使われたデバイス。</summary>
    public static ReadOnlyReactiveProperty<InputDeviceKind> Device => device;

    /// <summary>キーボード or パッドでフォーカス移動中か（選択枠を出す・自動フォーカスする条件）。</summary>
    public static ReadOnlyReactiveProperty<bool> IsNavigating => isNavigating;

    /// <summary>パッド使用中か（ボタンガイドを出す条件）。</summary>
    public static ReadOnlyReactiveProperty<bool> IsGamepad => isGamepad;

    /// <summary>最後に使われたパッドの表記系統。当面は Xbox 表記が既定。</summary>
    public static ReadOnlyReactiveProperty<GamepadStyle> Style => style;

    /// <summary>
    /// 論理ボタンが押された（A=Submit, B=Cancel, X, Y, LB/RB=TabLeft/Right, Start=Menu）。
    /// キーボード割当（Enter/Esc/Q/E）でも発火する。入力欄フォーカス中は発火しない。
    /// </summary>
    public static Observable<ControllerButton> OnButtonPressed => buttonPressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        device.Value = InputDeviceKind.Mouse;
        isNavigating.Value = false;
        isGamepad.Value = false;
        style.Value = GamepadStyle.Xbox;
        resolvedAsset = null;
        submitAction = cancelAction = tabLeftAction = tabRightAction = buttonXAction = buttonYAction = menuAction = null;
        debugOverride = false;
        disabledJoysticks.Clear();
    }

    /// <summary>
    /// このフレームに何かが押されたか（キー・マウスボタン・パッドのボタン・タッチ）。
    /// 旧 Input.anyKeyDown の置き換え。
    /// </summary>
    public static bool AnyPressedThisFrame()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
        var mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)) return true;
        var touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;
        foreach (var pad in Gamepad.all)
            if (AnyPadButtonPressed(pad)) return true;
        return false;
    }

    /// <summary>テキスト入力欄にフォーカスがあるか（このとき Q/E などのショートカットを無視する）。</summary>
    public static bool IsTextInputFocused()
    {
        var es = EventSystem.current;
        var go = es != null ? es.currentSelectedGameObject : null;
        if (go == null) return false;
        // 毎フレーム呼ばれるので GetComponent ではなく TryGetComponent（Editor で null 用の割り当てが出ない）
        if (go.TryGetComponent<TMP_InputField>(out var tmp) && tmp.isFocused) return true;
        return go.TryGetComponent<UnityEngine.UI.InputField>(out var legacy) && legacy.isFocused;
    }

    /// <summary>
    /// デバッグ・キャプチャ用: デバイス種別を強制する（実機入力があれば通常どおり上書きされる）。
    /// </summary>
    public static void DebugSetDevice(InputDeviceKind kind, GamepadStyle padStyle = GamepadStyle.Xbox)
    {
        style.Value = padStyle;
        SetDevice(kind);
        debugOverride = true;
    }

    /// <summary>UI マップのアクションを名前で取得（"UI/TabLeft" など）。見つからなければ null。</summary>
    public static InputAction FindAction(string path)
    {
        var asset = ResolveAsset();
        return asset != null ? asset.FindAction(path, false) : null;
    }

    // ---------------------------------------------------------------- 内部（Runner から呼ぶ）

    internal static void Tick()
    {
        DetectDevice();
        DispatchButtons();
    }

    /// <summary>
    /// Gamepad 以外のジョイスティック（汎用 HID ゲームコントローラー）を無効にし、以後つながったものも無効にする。
    /// 左手デバイス（Razer Tartarus 等）や仮想コントローラーの中には、触っていなくても軸やハットが
    /// 端に張り付いたままのものがあり、PlayerInput の自動切替や汎用バインドに拾われると「上を押しっぱなし」になる。
    /// このゲームは Gamepad / キーボード / マウスで遊ぶ前提なので Joystick はまとめて止める。
    /// </summary>
    internal static void InstallJoystickFilter()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        InputSystem.onDeviceChange += OnDeviceChange;
        var devices = InputSystem.devices;
        for (int i = 0; i < devices.Count; i++)
            DisableIfJoystick(devices[i]);
    }

    /// <summary>InstallJoystickFilter で止めたデバイスを元に戻す（Editor の再生終了時など）。</summary>
    internal static void UninstallJoystickFilter()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        foreach (var d in disabledJoysticks)
            if (d != null && d.added && !d.enabled) InputSystem.EnableDevice(d);
        disabledJoysticks.Clear();
    }

    private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled)
            DisableIfJoystick(device);
    }

    private static void DisableIfJoystick(InputDevice device)
    {
        if (device is not Joystick || !device.enabled) return;
        InputSystem.DisableDevice(device);
        if (!disabledJoysticks.Contains(device)) disabledJoysticks.Add(device);
    }

    internal static bool CancelPressedThisFrame()
    {
        EnsureActions();
        return cancelAction != null && cancelAction.WasPressedThisFrame();
    }

    private static void SetDevice(InputDeviceKind kind)
    {
        debugOverride = false;
        if (device.Value == kind) return;
        device.Value = kind;
        isNavigating.Value = kind != InputDeviceKind.Mouse;
        isGamepad.Value = kind == InputDeviceKind.Gamepad;
    }

    private static void DetectDevice()
    {
        foreach (var pad in Gamepad.all)
        {
            if (AnyPadButtonPressed(pad) || StickActive(pad))
            {
                style.Value = DetectStyle(pad);
                SetDevice(InputDeviceKind.Gamepad);
                return;
            }
        }

        var kb = Keyboard.current;
        if (kb != null && IsNavigationKeyPressed(kb) && !IsTextInputFocused())
        {
            SetDevice(InputDeviceKind.Keyboard);
            return;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            bool moved = mouse.delta.ReadValue().sqrMagnitude > MouseMoveThresholdSqr;
            bool clicked = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            bool scrolled = mouse.scroll.ReadValue().sqrMagnitude > 0.01f;
            if ((moved && !debugOverride) || clicked || scrolled)
            {
                SetDevice(InputDeviceKind.Mouse);
                return;
            }
        }

        var touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            SetDevice(InputDeviceKind.Mouse);
    }

    private static bool IsNavigationKeyPressed(Keyboard kb)
    {
        return kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame
            || kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame
            || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
            || kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
    }

    private static bool AnyPadButtonPressed(Gamepad pad)
    {
        if (pad == null) return false;
        return Pressed(pad.buttonSouth) || Pressed(pad.buttonEast) || Pressed(pad.buttonWest) || Pressed(pad.buttonNorth)
            || Pressed(pad.leftShoulder) || Pressed(pad.rightShoulder) || Pressed(pad.leftTrigger) || Pressed(pad.rightTrigger)
            || Pressed(pad.startButton) || Pressed(pad.selectButton)
            || Pressed(pad.leftStickButton) || Pressed(pad.rightStickButton)
            || Pressed(pad.dpad.up) || Pressed(pad.dpad.down) || Pressed(pad.dpad.left) || Pressed(pad.dpad.right);
    }

    private static bool Pressed(ButtonControl b) => b != null && b.wasPressedThisFrame;

    private static bool StickActive(Gamepad pad)
    {
        return pad.leftStick.ReadValue().sqrMagnitude > StickThreshold * StickThreshold
            || pad.rightStick.ReadValue().sqrMagnitude > StickThreshold * StickThreshold;
    }

    private static GamepadStyle DetectStyle(Gamepad pad)
    {
        if (pad is UnityEngine.InputSystem.DualShock.DualShockGamepad) return GamepadStyle.PlayStation;
        var desc = (pad.layout ?? string.Empty) + " " + (pad.description.product ?? string.Empty) + " " + (pad.description.manufacturer ?? string.Empty);
        if (desc.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0
            || desc.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0
            || desc.IndexOf("Sony", StringComparison.OrdinalIgnoreCase) >= 0) return GamepadStyle.PlayStation;
        if (desc.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0
            || desc.IndexOf("Nintendo", StringComparison.OrdinalIgnoreCase) >= 0) return GamepadStyle.Nintendo;
        return GamepadStyle.Xbox;
    }

    private static void DispatchButtons()
    {
        EnsureActions();
        if (IsTextInputFocused()) return;
        Fire(submitAction, ControllerButton.A);
        Fire(cancelAction, ControllerButton.B);
        Fire(buttonXAction, ControllerButton.X);
        Fire(buttonYAction, ControllerButton.Y);
        Fire(tabLeftAction, ControllerButton.LB);
        Fire(tabRightAction, ControllerButton.RB);
        Fire(menuAction, ControllerButton.Start);
    }

    private static void Fire(InputAction action, ControllerButton button)
    {
        if (action != null && action.WasPressedThisFrame())
            buttonPressed.OnNext(button);
    }

    private static InputActionAsset ResolveAsset()
    {
        if (resolvedAsset != null) return resolvedAsset;
        // 1) プロジェクト共通アクション（ProjectSettings > Input System Package）
        var asset = InputSystem.actions;
        // 2) フォールバック: 現在の EventSystem の UI モジュールが参照するアセット
        if (asset == null || asset.FindAction(ActionTabLeft, false) == null)
        {
            var es = EventSystem.current;
            var module = es != null ? es.currentInputModule as InputSystemUIInputModule : null;
            if (module != null && module.actionsAsset != null) asset = module.actionsAsset;
        }
        resolvedAsset = asset;
        return asset;
    }

    private static void EnsureActions()
    {
        if (submitAction == null || resolvedAsset == null)
        {
            resolvedAsset = null;
            var asset = ResolveAsset();
            if (asset == null) return;
            submitAction = asset.FindAction(ActionSubmit, false);
            cancelAction = asset.FindAction(ActionCancel, false);
            tabLeftAction = asset.FindAction(ActionTabLeft, false);
            tabRightAction = asset.FindAction(ActionTabRight, false);
            buttonXAction = asset.FindAction(ActionButtonX, false);
            buttonYAction = asset.FindAction(ActionButtonY, false);
            menuAction = asset.FindAction(ActionMenu, false);
        }
        EnableIfNeeded(submitAction);
        EnableIfNeeded(cancelAction);
        EnableIfNeeded(tabLeftAction);
        EnableIfNeeded(tabRightAction);
        EnableIfNeeded(buttonXAction);
        EnableIfNeeded(buttonYAction);
        EnableIfNeeded(menuAction);
    }

    private static void EnableIfNeeded(InputAction action)
    {
        if (action != null && !action.enabled) action.Enable();
    }
}
