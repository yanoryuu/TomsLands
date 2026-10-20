using System.Collections.Generic;
using DG.Tweening;
using R3;
using UnityEngine;
using UnityEngine.UI;

public sealed class TitleView : MonoBehaviour
{
    [Header("画面Group")]
    [Tooltip("TitleView自身ではなく、Start画面の子Groupを設定してください。")]
    [SerializeField] private GameObject startScreenGroup;
    [SerializeField] private GameObject startMethodScreenGroup;
    [SerializeField] private GameObject saveDataScreenGroup;

    [Header("Start画面")]
    [SerializeField] private Button startButton;

    [Header("開始方法選択画面")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button optionButton;
    [Tooltip("ゲームを終了する（確認のあと Application.Quit）。タイトルだけに置く（配信中などは途中経過が保存されないため）")]
    [SerializeField] private Button quitButton;
    [SerializeField] private Button startMethodBackButton;

    [Header("セーブデータ選択画面")]
    [Tooltip("1スロット分のプレハブ（SaveSlotViewをアタッチしたもの）")]
    [SerializeField] private SaveSlotView saveSlotPrefab;
    [Tooltip("スロットを並べる親（LayoutGroup推奨）")]
    [SerializeField] private Transform saveSlotContainer;
    [Tooltip("ロードモード時の見出し（任意）")]
    [SerializeField] private GameObject loadModeHeader;
    [Tooltip("ニューゲームのスロット選択時の見出し（任意）")]
    [SerializeField] private GameObject newGameModeHeader;
    [SerializeField] private Button saveDataBackButton;

    [Header("共通表示")]
    [SerializeField] private GameObject titleIcon;

    [Header("ゲームフロー設定")]
    [Tooltip("ON=自動生成 / OFF=手動(SO)フロー。未設定時は自動生成。")]
    [SerializeField] private Toggle autoGenerationToggle;

    public Subject<Unit> OnStartRequested { get; } = new();
    public Subject<Unit> OnNewGameSelected { get; } = new();
    public Subject<Unit> OnContinueSelected { get; } = new();
    public Subject<Unit> OnOptionSelected { get; } = new();
    public Subject<Unit> OnQuitRequested { get; } = new();
    /// <summary>スロットが選択されたとき（引数=スロット番号）。ロード／上書き先指定の両方で発火。</summary>
    public Subject<int> OnSaveSlotSelected { get; } = new();
    /// <summary>スロットの削除が要求されたとき（引数=スロット番号）。</summary>
    public Subject<int> OnSaveSlotDeleteRequested { get; } = new();
    public Subject<Unit> OnBackRequested { get; } = new();

    public bool UseAutoGeneration => autoGenerationToggle == null || autoGenerationToggle.isOn;

    private Tween _titleTween;
    private TitleType _currentScreen = TitleType.Start;

    // パッド/キー操作用のフォーカス単位（画面グループごと）
    private UIFocusScope _methodScope;
    private UIFocusScope _saveScope;
    private int _focusSlotIndex = -1; // 直前に操作したスロット（再構築後もそこへフォーカスを戻す）

    private readonly List<SaveSlotView> _slotViews = new();
    private readonly CompositeDisposable _slotDisposables = new();

    private void Awake()
    {
        AddClickListener(startButton, () => OnStartRequested.OnNext(Unit.Default));
        AddClickListener(newGameButton, () => OnNewGameSelected.OnNext(Unit.Default));
        AddClickListener(continueButton, () => OnContinueSelected.OnNext(Unit.Default));
        AddClickListener(optionButton, () => OnOptionSelected.OnNext(Unit.Default));
        AddClickListener(quitButton, () => OnQuitRequested.OnNext(Unit.Default));

        AddClickListener(startMethodBackButton, NotifyBackRequested);
        AddClickListener(saveDataBackButton, NotifyBackRequested);

        SetupFocusScopes();
        DisplayScreen(TitleType.Start);

        if (titleIcon != null)
        {
            _titleTween = titleIcon.transform
                .DOScale(new Vector3(7.3f, 7.3f, 1f), 1f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.OutCubic);
        }
    }

    private void SetupFocusScopes()
    {
        // スタート画面は「何かボタンを押す」だけなので何も選ばない
        if (startScreenGroup != null)
            UIFocusScope.GetOrAdd(startScreenGroup).AutoSelect = false;

        if (startMethodScreenGroup != null)
        {
            _methodScope = UIFocusScope.GetOrAdd(startMethodScreenGroup);
            _methodScope.SetCancelButton(startMethodBackButton);
        }
        if (saveDataScreenGroup != null)
        {
            _saveScope = UIFocusScope.GetOrAdd(saveDataScreenGroup);
            _saveScope.SetCancelButton(saveDataBackButton);
        }
    }

    private void Update()
    {
        // スタート画面では任意のキー入力・クリック・パッドのボタンで開始方法選択へ進む（新 Input System で判定）
        if (_currentScreen == TitleType.Start && ControllerInput.AnyPressedThisFrame())
            OnStartRequested.OnNext(Unit.Default);
    }

    private void OnDestroy()
    {
        _titleTween?.Kill();
        ClearSaveSlots();
        _slotDisposables.Dispose();
        OnStartRequested.Dispose();
        OnNewGameSelected.Dispose();
        OnContinueSelected.Dispose();
        OnOptionSelected.Dispose();
        OnQuitRequested.Dispose();
        OnSaveSlotSelected.Dispose();
        OnSaveSlotDeleteRequested.Dispose();
        OnBackRequested.Dispose();
    }

    /// <summary>「続きから」ボタンの有効/無効を切り替える（いずれかのスロットにデータがあるか）。</summary>
    public void SetSaveDataAvailable(bool available)
    {
        if (continueButton != null)
            continueButton.interactable = available;
    }

    /// <summary>
    /// スロット一覧を再構築する。
    /// </summary>
    /// <param name="infos">各スロットのサマリ情報。</param>
    /// <param name="loadMode">true=ロードモード（空きスロットは選択不可）/ false=ニューゲームのスロット選択（全て選択可）。</param>
    public void BuildSaveSlots(IReadOnlyList<SaveSlotInfo> infos, bool loadMode)
    {
        ClearSaveSlots();

        if (loadModeHeader != null) loadModeHeader.SetActive(loadMode);
        if (newGameModeHeader != null) newGameModeHeader.SetActive(!loadMode);

        if (saveSlotPrefab == null || saveSlotContainer == null)
        {
            Debug.LogError("[TitleView] saveSlotPrefab / saveSlotContainer が未設定です。");
            return;
        }
        if (infos == null) return;

        int index = 0;
        foreach (var info in infos)
        {
            var slot = Instantiate(saveSlotPrefab, saveSlotContainer);
            slot.Bind(info);
            // ロードモードでは空きスロットを選択不可にする（村の進行だけのスロットは選べる＝村へ行く）。ニューゲームでは全て選択可。
            slot.SetSelectable(!loadMode || info.HasProfile);

            slot.OnSelect.Subscribe(i => { _focusSlotIndex = i; OnSaveSlotSelected.OnNext(i); }).AddTo(_slotDisposables);
            slot.OnDelete.Subscribe(i => { _focusSlotIndex = i; OnSaveSlotDeleteRequested.OnNext(i); }).AddTo(_slotDisposables);

            _slotViews.Add(slot);

            // 上から順にポップイン
            if (!slot.TryGetComponent<CanvasGroup>(out var cg))
                cg = slot.gameObject.AddComponent<CanvasGroup>();
            float delay = index * 0.07f;
            cg.alpha = 0f;
            cg.DOFade(1f, 0.2f).SetDelay(delay).SetLink(slot.gameObject);
            slot.transform.localScale = Vector3.one * 0.92f;
            slot.transform.DOScale(1f, 0.28f).SetDelay(delay).SetEase(Ease.OutBack).SetLink(slot.gameObject);
            index++;
        }

        // パッド/キー: 直前に操作したスロット（無ければ最初に選べるスロット）へフォーカス
        if (_saveScope != null)
        {
            // 左下の「戻る」から上は一番下の選べるスロットへ（Automatic だと斜め上の先頭スロットへ飛ぶ）
            if (saveDataBackButton != null)
                saveDataBackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = LastUsableSlotButton() };
            _saveScope.FirstSelected = PickFocusSlotButton();
            _saveScope.Refocus(true);
        }
    }

    private Selectable LastUsableSlotButton()
    {
        for (int i = _slotViews.Count - 1; i >= 0; i--)
        {
            var b = _slotViews[i] != null ? _slotViews[i].SelectButton : null;
            if (b != null && b.interactable) return b;
        }
        return null;
    }

    private Selectable PickFocusSlotButton()
    {
        Selectable firstUsable = null;
        for (int i = 0; i < _slotViews.Count; i++)
        {
            var b = _slotViews[i] != null ? _slotViews[i].SelectButton : null;
            if (b == null || !b.interactable) continue;
            if (i == _focusSlotIndex) return b;
            if (firstUsable == null) firstUsable = b;
        }
        return firstUsable != null ? firstUsable : saveDataBackButton;
    }

    private void ClearSaveSlots()
    {
        _slotDisposables.Clear();
        foreach (var view in _slotViews)
        {
            if (view != null)
                Destroy(view.gameObject);
        }
        _slotViews.Clear();
    }

    public void DisplayScreen(TitleType screen)
    {
        _currentScreen = screen;
        if (screen != TitleType.SaveData) _focusSlotIndex = -1;
        // 開始方法の最初のフォーカス: 続きがあれば「続きから」、無ければ「ニューゲーム」
        if (_methodScope != null)
            _methodScope.FirstSelected = continueButton != null && continueButton.interactable ? continueButton : newGameButton;
        SetGroupActive(startScreenGroup, screen == TitleType.Start);
        SetGroupActive(startMethodScreenGroup, screen == TitleType.ContinueOrNewGame);
        SetGroupActive(saveDataScreenGroup, screen == TitleType.SaveData);

        // 画面の切替もフェードで出す（瞬間表示にしない）
        var shown = screen switch
        {
            TitleType.ContinueOrNewGame => startMethodScreenGroup,
            TitleType.SaveData => saveDataScreenGroup,
            _ => null,
        };
        if (shown != null) FadeIn(shown);
    }

    private static void FadeIn(GameObject group)
    {
        if (!group.TryGetComponent<CanvasGroup>(out var cg)) cg = group.AddComponent<CanvasGroup>();
        cg.DOKill();
        cg.alpha = 0f;
        cg.DOFade(1f, 0.18f).SetLink(group);
    }

    private void NotifyBackRequested() => OnBackRequested.OnNext(Unit.Default);

    private static void AddClickListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    private static void SetGroupActive(GameObject group, bool active)
    {
        if (group != null)
            group.SetActive(active);
    }
}
