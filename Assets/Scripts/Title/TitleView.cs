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
    [SerializeField] private GameObject difficultyScreenGroup;
    [SerializeField] private GameObject saveDataScreenGroup;

    [Header("Start画面")]
    [SerializeField] private Button startButton;

    [Header("開始方法選択画面")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button startMethodBackButton;

    [Header("難易度選択画面")]
    [SerializeField] private Button easyDifficultyButton;
    [SerializeField] private Button normalDifficultyButton;
    [SerializeField] private Button hardDifficultyButton;
    [SerializeField] private Button difficultyBackButton;

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
    public Subject<GameModeId> OnDifficultySelected { get; } = new();
    /// <summary>スロットが選択されたとき（引数=スロット番号）。ロード／上書き先指定の両方で発火。</summary>
    public Subject<int> OnSaveSlotSelected { get; } = new();
    /// <summary>スロットの削除が要求されたとき（引数=スロット番号）。</summary>
    public Subject<int> OnSaveSlotDeleteRequested { get; } = new();
    public Subject<Unit> OnBackRequested { get; } = new();

    public bool UseAutoGeneration => autoGenerationToggle == null || autoGenerationToggle.isOn;

    private Tween _titleTween;
    private TitleType _currentScreen = TitleType.Start;

    private readonly List<SaveSlotView> _slotViews = new();
    private readonly CompositeDisposable _slotDisposables = new();

    private void Awake()
    {
        AddClickListener(startButton, () => OnStartRequested.OnNext(Unit.Default));
        AddClickListener(newGameButton, () => OnNewGameSelected.OnNext(Unit.Default));
        AddClickListener(continueButton, () => OnContinueSelected.OnNext(Unit.Default));

        AddClickListener(easyDifficultyButton,
            () => OnDifficultySelected.OnNext(GameModeId.Short));
        AddClickListener(normalDifficultyButton,
            () => OnDifficultySelected.OnNext(GameModeId.Medium));
        AddClickListener(hardDifficultyButton,
            () => OnDifficultySelected.OnNext(GameModeId.Long));

        AddClickListener(startMethodBackButton, NotifyBackRequested);
        AddClickListener(difficultyBackButton, NotifyBackRequested);
        AddClickListener(saveDataBackButton, NotifyBackRequested);

        DisplayScreen(TitleType.Start);

        if (titleIcon != null)
        {
            _titleTween = titleIcon.transform
                .DOScale(new Vector3(7.3f, 7.3f, 1f), 1f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.OutCubic);
        }
    }

    private void Update()
    {
        // スタート画面では任意のキー入力・クリックで開始方法選択へ進む
        if (_currentScreen == TitleType.Start && Input.anyKeyDown)
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
        OnDifficultySelected.Dispose();
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
            // ロードモードでは空きスロットを選択不可にする。ニューゲームでは全て選択可。
            slot.SetSelectable(loadMode ? info.Exists : true);

            slot.OnSelect.Subscribe(OnSaveSlotSelected.OnNext).AddTo(_slotDisposables);
            slot.OnDelete.Subscribe(OnSaveSlotDeleteRequested.OnNext).AddTo(_slotDisposables);

            _slotViews.Add(slot);

            // 上から順にポップイン
            var cg = slot.GetComponent<CanvasGroup>();
            if (cg == null) cg = slot.gameObject.AddComponent<CanvasGroup>();
            float delay = index * 0.07f;
            cg.alpha = 0f;
            cg.DOFade(1f, 0.2f).SetDelay(delay).SetLink(slot.gameObject);
            slot.transform.localScale = Vector3.one * 0.92f;
            slot.transform.DOScale(1f, 0.28f).SetDelay(delay).SetEase(Ease.OutBack).SetLink(slot.gameObject);
            index++;
        }
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
        SetGroupActive(startScreenGroup, screen == TitleType.Start);
        SetGroupActive(startMethodScreenGroup, screen == TitleType.ContinueOrNewGame);
        SetGroupActive(difficultyScreenGroup, screen == TitleType.SelectDifficulty);
        SetGroupActive(saveDataScreenGroup, screen == TitleType.SaveData);
    }

    private void NotifyBackRequested()
    {
        OnBackRequested.OnNext(Unit.Default);
    }

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
