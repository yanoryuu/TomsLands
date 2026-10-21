using System;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 画面／パネル／ポップアップのルートに付ける「フォーカスの単位」。
///
/// ・有効化(OnEnable)で UIFocusManager のスタックに積まれ、最前面ならパッド/キー操作時に
///   firstSelected（未指定なら子の最初の操作可能な Selectable）を選択する。
/// ・無効化(OnDisable)でスタックから外れ、下のスコープの「最後に選んでいた要素」へ戻る。
/// ・Cancel(B / Esc) は最前面スコープだけが受け取る。動作はインスペクタで選択。
/// ・LB/RB/X/Y/Start も最前面スコープだけが受け取る（UnityEvent か OnButton を購読）。
///
/// 並び順は priority（大きいほど前面）→ 有効化順。ポップアップは priority を上げておくと安全。
/// </summary>
[DisallowMultipleComponent]
public class UIFocusScope : MonoBehaviour
{
    public enum CancelMode
    {
        /// <summary>何もしない（Cancel を下のスコープにも渡さない）。</summary>
        None = 0,
        /// <summary>cancelButton を押したことにする（押下アニメ付き）。</summary>
        ClickButton = 1,
        /// <summary>onCancel(UnityEvent) を呼ぶ。</summary>
        InvokeEvent = 2,
    }

    [Header("フォーカス")]
    [Tooltip("最初に選択する要素。未指定なら子の最初の操作可能な Selectable。")]
    [SerializeField] private Selectable firstSelected;
    [Tooltip("OFF にすると自動選択しない（フィールド移動画面など）。")]
    [SerializeField] private bool autoSelect = true;
    [Tooltip("ON: 戻ってきたとき最後に選んでいた要素を復元する。")]
    [SerializeField] private bool rememberLastSelected = true;
    [Tooltip("ON: このスコープが最前面の間、外の要素へフォーカスが移らない（ポップアップ向け）。")]
    [SerializeField] private bool modal;
    [Tooltip("大きいほど前面扱い。ポップアップ=100 など。")]
    [SerializeField] private int priority;
    [Tooltip("ON: このスコープが最前面の間、UI の選択移動を十字キー/矢印キーのみにする（左スティック・WASD はキャラ移動用に空ける）。")]
    [SerializeField] private bool dpadOnlyNavigation;

    [Header("Cancel (B / Esc)")]
    [SerializeField] private CancelMode cancelMode = CancelMode.None;
    [SerializeField] private Button cancelButton;
    [SerializeField] private UnityEvent onCancel = new();

    [Header("補助ボタン（最前面のときだけ呼ばれる）")]
    [SerializeField] private UnityEvent onTabLeft = new();
    [SerializeField] private UnityEvent onTabRight = new();
    [SerializeField] private UnityEvent onButtonX = new();
    [SerializeField] private UnityEvent onButtonY = new();
    [SerializeField] private UnityEvent onMenu = new();

    internal int order;
    internal GameObject lastSelected;
    internal float nextFocusRetryTime;

    private readonly Subject<ControllerButton> buttonSubject = new();
    private Action codeCancel;
    private Action cancelFallback;

    public bool Modal { get => modal; set => modal = value; }
    public int Priority { get => priority; set => priority = value; }
    public bool AutoSelect { get => autoSelect; set => autoSelect = value; }
    public bool RememberLastSelected { get => rememberLastSelected; set => rememberLastSelected = value; }
    public Selectable FirstSelected { get => firstSelected; set => firstSelected = value; }
    public bool DpadOnlyNavigation { get => dpadOnlyNavigation; set => dpadOnlyNavigation = value; }
    public UnityEvent OnCancelEvent => onCancel;
    public UnityEvent OnTabLeftEvent => onTabLeft;
    public UnityEvent OnTabRightEvent => onTabRight;
    public UnityEvent OnButtonXEvent => onButtonX;
    public UnityEvent OnButtonYEvent => onButtonY;
    public UnityEvent OnMenuEvent => onMenu;

    /// <summary>このスコープが最前面のときに押された論理ボタン（B 以外。B は Cancel 処理へ）。</summary>
    public Observable<ControllerButton> OnButton => buttonSubject;

    /// <summary>現在このスコープが最前面か。</summary>
    public bool IsTop => UIFocusManager.Top == this;

    // ------------------------------------------------------------------ ライフサイクル

    private void OnEnable() => UIFocusManager.Register(this);
    private void OnDisable() => UIFocusManager.Unregister(this);
    private void OnDestroy() => buttonSubject.Dispose();

    // ------------------------------------------------------------------ 公開 API

    /// <summary>
    /// コードから構成するためのヘルパー（シーン/プレハブを触れない場所で使う）。
    /// 既に付いていればそれを返す。
    /// </summary>
    public static UIFocusScope GetOrAdd(GameObject root)
    {
        if (root == null) return null;
        return root.TryGetComponent<UIFocusScope>(out var scope) ? scope : root.AddComponent<UIFocusScope>();
    }

    /// <summary>Cancel を cancelButton の押下にする。</summary>
    public UIFocusScope SetCancelButton(Button button)
    {
        cancelButton = button;
        cancelMode = button != null ? CancelMode.ClickButton : CancelMode.None;
        return this;
    }

    /// <summary>Cancel をコードのコールバックにする（インスペクタ設定より優先）。null で解除。</summary>
    public UIFocusScope SetCancelAction(Action action)
    {
        codeCancel = action;
        return this;
    }

    /// <summary>
    /// Cancel を誰も処理しなかったとき（cancelButton が非表示・押せない、cancelMode=None）に呼ぶ処理。null で解除。
    /// 例: 店ホームで「戻る」が出ていないときのキーボード Esc を設定画面に振る。
    /// </summary>
    public UIFocusScope SetCancelFallback(Action action)
    {
        cancelFallback = action;
        return this;
    }

    /// <summary>指定要素を選択する（パッド/キー使用中のみ。マウス中は記憶だけ）。</summary>
    public void Select(Selectable target)
    {
        if (target == null) return;
        lastSelected = target.gameObject;
        if (ControllerInput.IsNavigating.CurrentValue)
            UIFocusManager.Select(target.gameObject);
    }

    /// <summary>既定の要素へフォーカスし直す（リスト再構築後などに呼ぶ）。</summary>
    public void Refocus(bool resetToFirst = false)
    {
        if (resetToFirst) lastSelected = null;
        UIFocusManager.RequestRefocus();
    }

    /// <summary>Cancel が何かしら設定されているか。</summary>
    public bool HasCancel =>
        codeCancel != null
        || cancelFallback != null
        || (cancelMode == CancelMode.ClickButton && cancelButton != null)
        || (cancelMode == CancelMode.InvokeEvent);

    // ------------------------------------------------------------------ 内部

    internal bool Contains(GameObject go) => go != null && go.transform.IsChildOf(transform);

    internal GameObject GetFocusTarget()
    {
        if (rememberLastSelected && lastSelected != null && Contains(lastSelected) && UIFocusManager.IsUsable(lastSelected))
            return lastSelected;
        if (firstSelected != null && UIFocusManager.IsUsable(firstSelected.gameObject))
            return firstSelected.gameObject;

        GameObject fallback = null;
        var list = GetComponentsInChildren<Selectable>(false);
        foreach (var s in list)
        {
            if (!UIFocusManager.IsUsable(s.gameObject)) continue;
            if (s.navigation.mode == Navigation.Mode.None) continue;
            if (s is Scrollbar)
            {
                if (fallback == null) fallback = s.gameObject;
                continue;
            }
            return s.gameObject;
        }
        return fallback;
    }

    /// <summary>Cancel を処理したら true。</summary>
    internal bool HandleCancel()
    {
        if (codeCancel != null)
        {
            codeCancel.Invoke();
            return true;
        }
        switch (cancelMode)
        {
            case CancelMode.ClickButton:
                if (cancelButton != null && cancelButton.isActiveAndEnabled && cancelButton.IsInteractable())
                {
                    var es = EventSystem.current;
                    if (es != null)
                        ExecuteEvents.Execute(cancelButton.gameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
                    else
                        cancelButton.onClick.Invoke();
                    return true;
                }
                return InvokeCancelFallback();
            case CancelMode.InvokeEvent:
                onCancel?.Invoke();
                return true;
            default:
                return InvokeCancelFallback();
        }
    }

    private bool InvokeCancelFallback()
    {
        if (cancelFallback == null) return false;
        cancelFallback.Invoke();
        return true;
    }

    internal void HandleButton(ControllerButton button)
    {
        switch (button)
        {
            case ControllerButton.LB: onTabLeft?.Invoke(); break;
            case ControllerButton.RB: onTabRight?.Invoke(); break;
            case ControllerButton.X: onButtonX?.Invoke(); break;
            case ControllerButton.Y: onButtonY?.Invoke(); break;
            case ControllerButton.Start: onMenu?.Invoke(); break;
        }
        buttonSubject.OnNext(button);
    }
}
