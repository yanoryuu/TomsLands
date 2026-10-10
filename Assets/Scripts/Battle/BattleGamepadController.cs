using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 配信中（戦闘＋売買）画面のパッド/キー操作（Docs/Controller_Support.md §6）。
/// BattlePanelManager が実行時に付ける（シーン配置不要）。マウス操作には一切関与しない。
///
///   十字/スティック : カードのボタン・タブ・売り場/在庫スロット・一時停止/終了ボタン間を移動
///   A              : 決定（介入ボタン・タブでめくる・スロットは価格グラフ/在庫切れなら補充）
///   LB / RB        : 介入カードをめくる（スワイプの代替）
///   Y              : 狙う敵を選ぶ（敵タップの代替。十字で切替 → A 決定 / B やめる）
///   X              : 選択中のスロットのアイテムを持ち上げる → 置き先で A（ドラッグ＆ドロップの代替）/ B 取消
///   Start          : 一時停止/再開
///   青スパ・赤スパを A で出した直後は、敵が 2 体以上いれば自動で狙う敵の選択を開く
/// </summary>
[DefaultExecutionOrder(-600)] // LateUpdate を UIFocusManager.Tick（-500）より先に回す
public sealed class BattleGamepadController : MonoBehaviour
{
    private GameObject _panel;
    private UIFocusScope _scope;
    private InterventionCardView _card;
    private BattleControlView _control;
    private BattleSequencer _sequencer;
    private BattlePadTargetSelector _targets;
    private readonly CompositeDisposable _disposables = new();
    private bool _flipHold;   // めくり中は自動選択を止めている
    private bool _initialized;

    /// <summary>streamingPanel（配信中パネルのルート）を渡す。</summary>
    public void Initialize(GameObject streamingPanel)
    {
        if (_initialized || streamingPanel == null) return;
        _initialized = true;
        _panel = streamingPanel;

        _card = streamingPanel.GetComponentInChildren<InterventionCardView>(true);
        _control = streamingPanel.GetComponentInChildren<BattleControlView>(true);
        _sequencer = FindFirstObjectByType<BattleSequencer>(FindObjectsInactive.Include);

        // 介入カードのボタン/タブはプレハブ生成時に Navigation=None なので、十字で出入りできるよう Automatic にする
        if (_card != null)
            foreach (var s in _card.GetComponentsInChildren<Selectable>(true))
                if (s.navigation.mode == Navigation.Mode.None)
                    s.navigation = new Navigation { mode = Navigation.Mode.Automatic };

        _scope = UIFocusScope.GetOrAdd(streamingPanel);
        if (_card != null && _card.TabSelectable != null) _scope.FirstSelected = _card.TabSelectable;
        _scope.SetCancelAction(OnCancel);

        _targets = gameObject.AddComponent<BattlePadTargetSelector>();
        _targets.Initialize(_sequencer, CanTarget);

        _scope.OnButton.Subscribe(OnScopeButton).AddTo(_disposables);

        if (_card != null)
        {
            // 青・赤スパを出した直後: 狙う敵を選ぶ（パッド/キーのときだけ）
            _card.OnHeroRequestAccepted
                .Where(t => t == HeroInterventionType.Skill || t == HeroInterventionType.Special)
                .Where(_ => ControllerInput.IsNavigating.CurrentValue)
                .Subscribe(_ => _targets.Open(minEnemies: 2))
                .AddTo(_disposables);
        }

        ControllerInput.IsNavigating
            .Where(on => !on)
            .Subscribe(_ => ItemSlotView.CancelPadHold())
            .AddTo(_disposables);
    }

    private void OnDestroy()
    {
        _disposables.Dispose();
        ItemSlotView.CancelPadHold();
    }

    // ---------------------------------------------------------------- 入力

    private void OnScopeButton(ControllerButton button)
    {
        if (_panel == null || !_panel.activeInHierarchy) return;
        switch (button)
        {
            case ControllerButton.LB:
            case ControllerButton.RB:
                FlipCard();
                break;
            case ControllerButton.Y:
                ItemSlotView.CancelPadHold();
                _targets.Open(minEnemies: 1);
                break;
            case ControllerButton.X:
                TogglePickUp();
                break;
            case ControllerButton.Start:
                PressButton(_control != null ? _control.PauseButton : null);
                break;
        }
    }

    /// <summary>B: 持ち上げ中なら取り消す（それ以外の戻る先は無い）。</summary>
    private void OnCancel()
    {
        if (ItemSlotView.IsPadHolding) ItemSlotView.CancelPadHold();
    }

    private void FlipCard()
    {
        if (_card == null || !_card.isActiveAndEnabled || !_card.IsInteractable || _card.IsFlipping) return;
        _card.Flip();
    }

    private void TogglePickUp()
    {
        if (ItemSlotView.IsPadHolding)
        {
            ItemSlotView.CancelPadHold();
            return;
        }
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        var slot = sel != null ? sel.GetComponent<ItemSlotView>() : null;
        if (slot != null) slot.PadPickUp();
    }

    private static void PressButton(Button button)
    {
        if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) return;
        var es = EventSystem.current;
        if (es != null) ExecuteEvents.Execute(button.gameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
        else button.onClick.Invoke();
    }

    private bool CanTarget()
        => _panel != null && _panel.activeInHierarchy && (_card == null || _card.IsInteractable);

    // ---------------------------------------------------------------- めくり中のフォーカス

    private void LateUpdate()
    {
        if (_scope == null || _card == null) return;

        if (_card.IsFlipping)
        {
            // めくり中はカード全体が操作不可になる。カード内を選んでいたら、
            // 他のボタンへ選択枠が飛ばないよう自動選択を止め、めくり後の面へ戻す
            if (!_flipHold && ControllerInput.IsNavigating.CurrentValue && IsSelectionInCard())
            {
                _flipHold = true;
                _scope.AutoSelect = false;
            }
            return;
        }

        if (_flipHold)
        {
            _flipHold = false;
            _scope.AutoSelect = true;
            var target = _card.GetPadFocusTarget();
            if (target != null) _scope.Select(target);
            else _scope.Refocus();
        }
    }

    private bool IsSelectionInCard()
    {
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        return sel != null && sel.transform.IsChildOf(_card.transform);
    }

    private void OnDisable()
    {
        if (_flipHold && _scope != null) _scope.AutoSelect = true;
        _flipHold = false;
        ItemSlotView.CancelPadHold();
    }
}
