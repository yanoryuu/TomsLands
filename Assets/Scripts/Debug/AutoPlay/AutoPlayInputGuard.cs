#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 無人で回す自動操作（人格マラソン・実況の録画プリセット）の間だけ、
/// (1) Unity にフォーカスが無くても Play を進める（Application.runInBackground = true。ProjectSettings は変えない）
/// (2) 人の入力をゲームに一切通さない（Input System の全デバイスを無効にし、つながり直したものも無効にし続ける）。
///
/// 背景（memory: shared-save-folder-play-verify）: 2026-10-18、Play 中に runInBackground だけを有効にしたら、
/// ほかのウィンドウでのキー入力・ゲームパッド入力がゲームに通り、タイトルでユーザーのセーブが消えた。
/// だから runInBackground は必ず入力の遮断と一緒にだけ入れる（Engage が両方を同時に行う）。
///
/// - 自動操作は入力デバイスを使わない（AutoPlayUi は ExecuteEvents でボタンを押し、会話は AdvEngine を直接送る）ので、遮断しても進む。
/// - UI の EventSystem は InputSystemUIInputModule、宴の入力は Input System 版の戦略（GamepadInputStrategy）なので、デバイスの無効化で止まる。
/// - 旧 Input Manager を直接読む箇所（DebugResetManager の Esc 10 回・DebugMenuView の F11）は <see cref="Active"/> を見て読まない。
/// - 毎フレーム、有効に戻ったデバイスが無いか確かめて止め直す（フォーカスの出入りで Input System が戻すことがあるため）。
/// - Release で、自分が止めたデバイスだけを有効に戻し、runInBackground を元の値に戻す（Play を抜けるとき・破棄されるとき）。
/// - Engage 直後とフォーカスが変わるたびに、無効にしたキーボード・パッドへ擬似入力（Esc / Enter / A）を入れて、
///   ゲームに届かないことをログに出す（「入力の遮断を確認」）。
/// </summary>
public sealed class AutoPlayInputGuard : MonoBehaviour
{
    private const string LogPrefix = "[InputGuard]";

    /// <summary>遮断中か（旧 Input Manager を読む箇所はこれを見て読まない）。</summary>
    public static bool Active { get; private set; }

    /// <summary>止めたデバイスの数（心拍・ログ用）。</summary>
    public static int DisabledCount => Disabled.Count;

    /// <summary>直近の遮断の確かめ（"ok" / "leak: ..." / null = まだ）。</summary>
    public static string LastProbe { get; private set; }

    private static readonly List<InputDevice> Disabled = new();
    private static bool _prevRunInBackground;
    private static AutoPlayInputGuard _instance;
    private static string _reason;
    private int _probeFrames = -1;
    private string _probeWhen;
    private bool _lastFocus = true;

    /// <summary>遮断を始める（2 回呼んでも 1 回と同じ）。reason はログ用。</summary>
    public static void Engage(string reason)
    {
        if (!Application.isPlaying) return;
        if (Active)
        {
            DisableAll();
            return;
        }
        Active = true;
        _reason = reason;
        _prevRunInBackground = Application.runInBackground;
        InputSystem.onDeviceChange -= OnDeviceChange;
        InputSystem.onDeviceChange += OnDeviceChange;
        DisableAll();
        Application.runInBackground = true;
        if (_instance == null)
        {
            var go = new GameObject("[AutoPlayInputGuard]") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AutoPlayInputGuard>();
        }
        _instance._lastFocus = Application.isFocused;
        _instance.StartProbe("開始時");
        Debug.Log($"{LogPrefix} 人の入力を遮断（{reason}）: 入力デバイス {Disabled.Count} 台を無効化、runInBackground {_prevRunInBackground} → true");
    }

    /// <summary>遮断をやめる（止めたデバイスを戻し、runInBackground を元に戻す）。</summary>
    public static void Release(string why)
    {
        if (!Active) return;
        Active = false;
        InputSystem.onDeviceChange -= OnDeviceChange;
        int restored = 0;
        foreach (var d in Disabled)
        {
            if (d == null || !d.added || d.enabled) continue;
            try
            {
                InputSystem.EnableDevice(d);
                restored++;
            }
            catch (System.Exception e) { Debug.LogWarning($"{LogPrefix} {d.name} を戻せませんでした: {e.Message}"); }
        }
        Disabled.Clear();
        Application.runInBackground = _prevRunInBackground;
        Debug.Log($"{LogPrefix} 遮断を解除（{why}）: デバイス {restored} 台を戻し、runInBackground を {_prevRunInBackground} に戻した");
        if (_instance != null)
        {
            var go = _instance.gameObject;
            _instance = null;
            if (go != null) Destroy(go);
        }
    }

    private static void DisableAll()
    {
        var devices = InputSystem.devices;
        for (int i = 0; i < devices.Count; i++) DisableOne(devices[i]);
    }

    private static void DisableOne(InputDevice d)
    {
        if (d == null || !d.added || !d.enabled) return;
        try
        {
            InputSystem.DisableDevice(d);
            if (!Disabled.Contains(d)) Disabled.Add(d);
        }
        catch (System.Exception e) { Debug.LogWarning($"{LogPrefix} {d.name} を止められませんでした: {e.Message}"); }
    }

    private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!Active) return;
        if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled)
            DisableOne(device);
    }

    private void Update()
    {
        if (!Active) return;
        // フォーカスの出入り・デバイスのつなぎ直しで有効に戻ったものを止め直す
        var devices = InputSystem.devices;
        for (int i = 0; i < devices.Count; i++)
            if (devices[i].enabled) DisableOne(devices[i]);
        if (!Application.runInBackground) Application.runInBackground = true;

        bool focus = Application.isFocused;
        if (focus != _lastFocus)
        {
            _lastFocus = focus;
            StartProbe(focus ? "フォーカスが戻ったとき" : "フォーカスが外れたとき");
        }
        StepProbe();
    }

    // ---------------- 遮断の確かめ（擬似入力） ----------------

    private void StartProbe(string when)
    {
        _probeWhen = when;
        _probeFrames = 0;
        // 擬似入力は「止めてある」デバイスにだけ入れる（止まっていないデバイスに押下を入れると押しっぱなしになるので入れない）
        _probeKb = Keyboard.current is { enabled: false } k && !k.escapeKey.isPressed && !k.enterKey.isPressed ? k : null;
        _probePad = Gamepad.current is { enabled: false } g && !g.buttonSouth.isPressed ? g : null;
        try
        {
            if (_probeKb != null) InputSystem.QueueStateEvent(_probeKb, new KeyboardState(Key.Escape, Key.Enter));
            if (_probePad != null) InputSystem.QueueStateEvent(_probePad, new GamepadState(GamepadButton.South));
        }
        catch (System.Exception e) { Debug.LogWarning($"{LogPrefix} 擬似入力を入れられませんでした: {e.Message}"); }
    }

    private Keyboard _probeKb;
    private Gamepad _probePad;

    /// <summary>押下の擬似入力のあとに離す入力も入れる（万一届いていても押しっぱなしにしない）。</summary>
    private void ReleaseProbe()
    {
        try
        {
            if (_probeKb != null && _probeKb.added) InputSystem.QueueStateEvent(_probeKb, new KeyboardState());
            if (_probePad != null && _probePad.added) InputSystem.QueueStateEvent(_probePad, new GamepadState());
        }
        catch (System.Exception) { /* 離す入力が入らなくても、止めてあるデバイスなので害はない */ }
    }

    private void StepProbe()
    {
        if (_probeFrames < 0) return;
        var kb = _probeKb;
        var pad = _probePad;
        var leaks = new List<string>();
        if (kb != null && (kb.escapeKey.isPressed || kb.enterKey.isPressed)) leaks.Add("キーボード");
        if (pad != null && pad.buttonSouth.isPressed) leaks.Add("パッド");
        if (leaks.Count > 0)
        {
            LastProbe = "leak: " + string.Join("・", leaks);
            Debug.LogError($"{LogPrefix} 遮断の確認（{_probeWhen}）: 擬似入力が {LastProbe} に届いた → デバイスを止め直す");
            ReleaseProbe();
            DisableAll();
            _probeFrames = -1;
            return;
        }
        if (++_probeFrames < 5) return;
        _probeFrames = -1;
        ReleaseProbe();
        LastProbe = "ok";
        Debug.Log($"{LogPrefix} 遮断の確認（{_probeWhen}・フォーカス{(Application.isFocused ? "あり" : "なし")}）: " +
                  $"キーボード Esc/Enter{(kb != null ? "" : "（無し・止めていない）")}・パッド A{(pad != null ? "" : "（無し・止めていない）")} を擬似入力 → " +
                  $"ゲームに届かない（無効 {Disabled.Count} 台）");
    }

    private void OnDestroy()
    {
        if (_instance == this) Release("破棄");
    }

    private void OnApplicationQuit() => Release("終了");
}
#endif
