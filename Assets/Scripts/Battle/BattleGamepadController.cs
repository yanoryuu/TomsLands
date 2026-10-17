using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 配信中（戦闘＋売買）画面のパッド/キー操作（Docs/Controller_Support.md §6）。
/// BattlePanelManager が実行時に付ける（シーン配置不要）。マウス操作には一切関与しない。
///
///   十字/スティック : カードのボタン・タブ・売り場/在庫スロット・一時停止/終了ボタン間を移動
///   A              : 決定（介入ボタン・タブでめくる・スロットは価格グラフ/在庫切れなら補充）
///   LB / RB        : 介入カードをめくる（スワイプの代替）
///   Y              : 狙う敵を選ぶ（敵タップの代替。十字で切替 → A 決定 / B やめる）
///   X              : 在庫のアイテム → 売り場の先頭の空き枠へ並べる（空き無しなら持ち上げて売り場へ → A で入替 / B 取消）
///                    売り場のアイテム → 持ち上げる → 置き先で A（ドラッグ＆ドロップの代替）/ B 取消
///   Start          : 一時停止して設定画面（加算シーン Setting）を開く。閉じたら再開する
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
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
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
                OpenSettingsPaused();
                break;
        }
    }

    // ---------------------------------------------------------------- 設定画面

    private const string SettingSceneName = "Setting";
    private bool _settingLoading;
    private bool _resumeOnSettingClosed; // 設定を開くために一時停止した（閉じたら再開する）

    private BattlePauseController PauseController => _sequencer != null ? _sequencer.PauseController : null;

    /// <summary>Start: 一時停止してから設定画面を開く（既に一時停止中ならそのまま開き、閉じても停止を保つ）。</summary>
    private void OpenSettingsPaused()
    {
        if (_settingLoading || SceneManager.GetSceneByName(SettingSceneName).isLoaded) return;
        ItemSlotView.CancelPadHold();

        var pause = PauseController;
        _resumeOnSettingClosed = false;
        if (pause != null && !pause.IsPaused)
        {
            PressButton(_control != null ? _control.PauseButton : null);
            _resumeOnSettingClosed = pause.IsPaused;
        }

        _settingLoading = true;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        var op = SceneManager.LoadSceneAsync(SettingSceneName, LoadSceneMode.Additive);
        if (op == null) { _settingLoading = false; return; }
        op.completed += _ => _settingLoading = false;
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.name != SettingSceneName) return;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (!_resumeOnSettingClosed) return;
        _resumeOnSettingClosed = false;
        var pause = PauseController;
        if (this != null && pause != null && pause.IsPaused)
            PressButton(_control != null ? _control.PauseButton : null);
    }

    /// <summary>B: 持ち上げ中なら取り消す（それ以外の戻る先は無い）。</summary>
    private void OnCancel()
    {
        if (ItemSlotView.IsPadHolding) CancelHold();
    }

    private void FlipCard()
    {
        if (_card == null || !_card.isActiveAndEnabled || !_card.IsInteractable || _card.IsFlipping) return;
        _card.Flip();
    }

    /// <summary>
    /// X:
    ///  - 持ち上げ中 → 取り消す
    ///  - 在庫（手持ち）のアイテム → 売り場の先頭の空き枠へ並べる（D&amp;D で空き枠に置くのと同じ処理）。
    ///    フォーカスは在庫側に残し、そのスロットが空になったら近くの在庫アイテムへ移して連続で並べられるようにする。
    ///    売り場に空きが無ければ持ち上げて売り場の先頭へフォーカスを移す（A で置いた先と入替 / B・X で取消）
    ///  - 売り場のアイテム → 従来どおり持ち上げ（売り場内の並べ替え・在庫へ戻す）
    /// </summary>
    private void TogglePickUp()
    {
        if (ItemSlotView.IsPadHolding)
        {
            CancelHold();
            return;
        }
        _returnOnCancel = null;
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        var slot = sel != null ? sel.GetComponent<ItemSlotView>() : null;
        if (slot == null) return;

        var sales = SalesView;
        bool isInventory = sales != null && sales.GetSlotIndex(slot) < 0;
        if (!isInventory || slot.CurrentItem == null)
        {
            slot.PadPickUp();
            return;
        }

        var empty = FindFirstEmptySellSlot(sales);
        if (empty != null)
        {
            if (!slot.PadPlaceOnto(empty)) return;
            FocusAfterPlace(slot, sales);
            return;
        }

        // 空きが無い: 持ち上げて置き先（入替相手）を選ばせる
        var first = FindFirstSellSlot(sales);
        if (slot.PadPickUp() && first != null)
        {
            var s = first.GetComponent<Selectable>();
            if (s != null)
            {
                _returnOnCancel = slot.GetComponent<Selectable>();
                _scope.Select(s);
            }
        }
    }

    // 空き無しで売り場へフォーカスを移した持ち上げを取り消したとき、元の在庫スロットへ戻すための記憶
    private Selectable _returnOnCancel;

    /// <summary>持ち上げを取り消す。売り場へ自動でフォーカスを移していたなら元の在庫スロットへ戻す。</summary>
    private void CancelHold()
    {
        bool wasHolding = ItemSlotView.IsPadHolding;
        ItemSlotView.CancelPadHold();
        var back = _returnOnCancel;
        _returnOnCancel = null;
        if (wasHolding && back != null && back.isActiveAndEnabled) _scope.Select(back);
    }

    private StreamingSalesView _salesView;
    private StreamingSalesView SalesView
    {
        get
        {
            if (_salesView == null)
                _salesView = FindFirstObjectByType<StreamingSalesView>(FindObjectsInactive.Include);
            return _salesView;
        }
    }

    private static ItemSlotView FindFirstEmptySellSlot(StreamingSalesView sales)
    {
        var slots = sales.ItemSlots;
        if (slots == null) return null;
        foreach (var s in slots)
            if (s != null && s.isActiveAndEnabled && s.CurrentItem == null) return s;
        return null;
    }

    private static ItemSlotView FindFirstSellSlot(StreamingSalesView sales)
    {
        var slots = sales.ItemSlots;
        if (slots == null) return null;
        foreach (var s in slots)
            if (s != null && s.isActiveAndEnabled) return s;
        return null;
    }

    /// <summary>
    /// 並べた後のフォーカス: 元の在庫スロットにまだアイテムがあればそのまま（入替で別アイテムが来た場合など）。
    /// 空になったら、同じ在庫欄で最も近いアイテム入りスロットへ移す。無ければその場に残す（ホバー表示だけ更新）。
    /// </summary>
    private void FocusAfterPlace(ItemSlotView from, StreamingSalesView sales)
    {
        if (from.CurrentItem == null)
        {
            var root = from.GetComponentInParent<Canvas>()?.rootCanvas;
            ItemSlotView best = null;
            float bestDist = float.MaxValue;
            foreach (var s in FindObjectsByType<ItemSlotView>(FindObjectsSortMode.None))
            {
                if (s == from || !s.isActiveAndEnabled || s.CurrentItem == null) continue;
                if (sales.GetSlotIndex(s) >= 0) continue;
                if (root != null && s.GetComponentInParent<Canvas>()?.rootCanvas != root) continue;
                float d = (s.transform.position - from.transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            var target = best != null ? best.GetComponent<Selectable>() : null;
            if (target != null)
            {
                _scope.Select(target);
                return;
            }
        }

        // 同じスロットに留まる: 置いたアイテムの価格表示が残らないようホバー表示を出し直す
        var es = EventSystem.current;
        if (es == null) return;
        var ev = new PointerEventData(es);
        ExecuteEvents.Execute(from.gameObject, ev, ExecuteEvents.pointerExitHandler);
        ExecuteEvents.Execute(from.gameObject, ev, ExecuteEvents.pointerEnterHandler);
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
