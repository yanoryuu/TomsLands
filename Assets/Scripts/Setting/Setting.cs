using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// オプション画面（加算シーン「Setting」）。
/// タイトル画面・プレイ中メニューのどちらからも additive ロードで開かれる。
/// タブ構成: サウンド / 画面 / 演出 / 会話 / 情報。
/// 音量・演出・会話の設定は GameSettings（PlayerPrefs）に保存し、閉じるときにディスクへ書き出す。
/// 画面（表示モード・解像度）は Unity 自身が保存するので、ここでは Screen を直接読み書きするだけ（二重に管理しない）。
/// </summary>
public class Setting : MonoBehaviour
{
    [Header("サウンド")]
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider seSlider;

    [Header("画面設定")]
    [Tooltip("旧: フルスクリーンのオン/オフ。表示モードのプルダウンがあれば隠す")]
    [SerializeField] private Toggle fullscreenToggle;
    [Tooltip("表示モード（ボーダーレス / フルスクリーン / ウィンドウ）")]
    [SerializeField] private TMP_Dropdown displayModeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    [Header("演出・快適性")]
    [SerializeField] private Toggle fastEffectsToggle;
    [SerializeField] private Toggle reduceShakeToggle;

    [Header("会話")]
    [Tooltip("文字の速さ（左=遅い / 右端=瞬時）")]
    [SerializeField] private Slider textSpeedSlider;
    [SerializeField] private Toggle autoAdvanceToggle;
    [SerializeField] private Toggle skipReadToggle;

    [Header("情報表示")]
    [SerializeField] private TextMeshProUGUI versionText;

    [Header("タブ")]
    [SerializeField] private Button soundTabButton;
    [SerializeField] private Button displayTabButton;
    [SerializeField] private Button effectTabButton;
    [SerializeField] private Button conversationTabButton;
    [SerializeField] private Button infoTabButton;
    [SerializeField] private GameObject soundGroup;
    [SerializeField] private GameObject displayGroup;
    [SerializeField] private GameObject effectGroup;
    [SerializeField] private GameObject conversationGroup;
    [SerializeField] private GameObject infoGroup;

    [Header("タブの見た目（選択中のタブ）")]
    [Tooltip("選択中のタブの絵（空なら絵は替えない）")]
    [SerializeField] private Sprite tabSelectedSprite;
    [SerializeField] private Color tabSelectedLabelColor = new(1f, 0.82f, 0.3f, 1f);

    [Header("共通")]
    [SerializeField] private Button closeBtn;
    [Tooltip("タイトルへ戻るボタン。タイトル画面から開いた場合は自動で非表示になる")]
    [SerializeField] private Button toTitleBtn;

    public Subject<Unit> OnCloseButtonClicked { get; } = new();

    // 閉じる処理の二重実行ガード
    private bool isClosing;

    // 解像度ドロップダウンの選択肢（重複除去済み）
    private readonly List<Vector2Int> resolutionOptions = new();

    // 表示モード（プルダウンの並び順）
    private static readonly FullScreenMode[] DisplayModes =
        { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
    private static readonly string[] DisplayModeLabels = { "ボーダーレス", "フルスクリーン", "ウィンドウ" };

    // SE 音量を変えたあと、少し止まったら試し鳴らしする
    private const float SePreviewDelay = 0.15f;
    private float sePreviewAt = -1f;

    // ---- タブ ----
    private sealed class Tab
    {
        public Button Button;
        public GameObject Group;
        public Func<Selectable> FirstControl;
        public Image Image;
        public Sprite NormalSprite;
        public TMP_Text Label;
        public Color NormalLabelColor;
    }

    private readonly List<Tab> tabs = new();

    // ---- パッド/キー ----
    // 加算シーンのため、シーンファイルではなくここでフォーカス範囲を構成する。
    // モーダル（設定を閉じるまで下の画面へフォーカスが出ない）・B/Esc で閉じる・LB/RB でタブ切替。
    private UIFocusScope focusScope;
    private int currentTab;

    private void Awake()
    {
        BuildTabs();

        var canvas = GetComponentInParent<Canvas>();
        var host = canvas != null ? canvas.rootCanvas.gameObject : gameObject;
        focusScope = UIFocusScope.GetOrAdd(host);
        if (focusScope == null) return;
        focusScope.Modal = true;
        focusScope.Priority = Mathf.Max(focusScope.Priority, 100);
        if (closeBtn != null) focusScope.SetCancelButton(closeBtn);
        if (bgmSlider != null) focusScope.FirstSelected = bgmSlider;
        focusScope.OnButton
            .Subscribe(b =>
            {
                if (b == ControllerButton.LB) ShowTabByNav(currentTab - 1);
                else if (b == ControllerButton.RB) ShowTabByNav(currentTab + 1);
            })
            .AddTo(this);
    }

    private void BuildTabs()
    {
        tabs.Clear();
        AddTab(soundTabButton, soundGroup, () => bgmSlider);
        AddTab(displayTabButton, displayGroup, () => displayModeDropdown != null ? displayModeDropdown : fullscreenToggle);
        AddTab(effectTabButton, effectGroup, () => fastEffectsToggle);
        AddTab(conversationTabButton, conversationGroup, () => textSpeedSlider);
        AddTab(infoTabButton, infoGroup, () => null);
    }

    private void AddTab(Button button, GameObject group, Func<Selectable> firstControl)
    {
        if (button == null && group == null) return;
        var image = button != null ? button.targetGraphic as Image : null;
        var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        tabs.Add(new Tab
        {
            Button = button,
            Group = group,
            FirstControl = firstControl,
            Image = image,
            NormalSprite = image != null ? image.sprite : null,
            Label = label,
            NormalLabelColor = label != null ? label.color : Color.white,
        });
    }

    private void ShowTabByNav(int index)
    {
        if (tabs.Count == 0) return;
        index = (index % tabs.Count + tabs.Count) % tabs.Count;
        ShowTab(index);
        var target = TabFocusTarget(index);
        if (target != null && focusScope != null) focusScope.Select(target);
    }

    /// <summary>タブを開いたとき最初に選ぶ要素（中身が無い「情報」タブはそのタブのボタン）。</summary>
    private Selectable TabFocusTarget(int index)
    {
        if (index < 0 || index >= tabs.Count) return closeBtn;
        var first = tabs[index].FirstControl?.Invoke();
        if (first != null && first.isActiveAndEnabled && first.IsInteractable()) return first;
        var tabButton = tabs[index].Button;
        if (tabButton != null && tabButton.isActiveAndEnabled && tabButton.IsInteractable()) return tabButton;
        return closeBtn;
    }

    private void Start()
    {
        InitSound();
        InitDisplay();
        InitEffects();
        InitConversation();
        InitInfo();
        InitTabs();
        Bind();

        ShowTab(0); // 初期表示はサウンド

        // #1 窓がぽよんと開く（この GameObject が 1000x720 の窓。省略モードでは即）
        UIMotion.Open(gameObject);
        // #7 選んでいるタブの下に金の下線（タブの並びが決まってから置く）
        CreateTabUnderline();
    }

    // ---- #7 タブ切替の動き ----

    private RectTransform tabUnderline;

    private void CreateTabUnderline()
    {
        if (currentTab < 0 || currentTab >= tabs.Count) return;
        var tabButton = tabs[currentTab].Button;
        if (tabButton == null) return;
        var tabRect = (RectTransform)tabButton.transform;
        if (tabRect.parent is RectTransform row) LayoutRebuilder.ForceRebuildLayoutImmediate(row);
        tabUnderline = TabSwitcher.CreateUnderline(tabRect);
    }

    /// <summary>下線を選んだタブへ動かし、中身を横から滑り込ませる（右のタブへ移ったら右から）。</summary>
    private void PlayTabSwitch(int from, int to)
    {
        if (to < 0 || to >= tabs.Count) return;
        var tab = tabs[to];
        if (tabUnderline != null && tab.Button != null)
            TabSwitcher.MoveUnderline(tabUnderline, (RectTransform)tab.Button.transform);
        if (from != to && tab.Group != null && tab.Group.transform is RectTransform content)
            TabSwitcher.SlideContent(content, to > from ? 1 : -1);
    }

    private void Update()
    {
        if (sePreviewAt > 0f && Time.unscaledTime >= sePreviewAt)
        {
            sePreviewAt = -1f;
            // 試し鳴らし（UISound の同フレーム抑止とは別に、必ず1回鳴らす）
            SoundManager.Instance?.PlaySE(UISound.DecideKey);
        }
    }

    private void OnDestroy()
    {
        // 閉じるボタン以外（タイトルへ・シーン破棄）で閉じても、変えた設定をディスクへ書き出す
        GameSettings.Save();
    }

    // ---- 初期化 ----

    private void InitSound()
    {
        if (bgmSlider) bgmSlider.value = SoundManager.Instance.GetBGMVolume();
        if (seSlider) seSlider.value = SoundManager.Instance.GetSEVolume();
    }

    private void InitDisplay()
    {
        if (displayModeDropdown)
        {
            displayModeDropdown.ClearOptions();
            displayModeDropdown.AddOptions(new List<string>(DisplayModeLabels));
            displayModeDropdown.SetValueWithoutNotify(ModeIndex(Screen.fullScreenMode));
            // 表示モードのプルダウンがあるときは旧トグルを出さない
            if (fullscreenToggle) fullscreenToggle.gameObject.SetActive(false);
        }
        else if (fullscreenToggle)
        {
            fullscreenToggle.isOn = Screen.fullScreenMode != FullScreenMode.Windowed;
        }

        RefreshResolutionOptions(new Vector2Int(Screen.width, Screen.height));
    }

    /// <summary>
    /// 解像度の選択肢を作り直す。モニターの大きさ以下だけを、大きい順に並べる（リフレッシュレート違いは1つにまとめる）。
    /// 一覧に無い大きさ（ウィンドウの端をつかんで変えたときなど）は「（現在）」として足す。
    /// </summary>
    private void RefreshResolutionOptions(Vector2Int current)
    {
        if (!resolutionDropdown) return;

        var native = NativeSize();
        resolutionOptions.Clear();
        foreach (var res in Screen.resolutions)
        {
            var size = new Vector2Int(res.width, res.height);
            if (size.x > native.x || size.y > native.y) continue;
            if (!resolutionOptions.Contains(size)) resolutionOptions.Add(size);
        }
        resolutionOptions.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));

        var labels = new List<string>();
        foreach (var size in resolutionOptions) labels.Add($"{size.x} × {size.y}");

        int index = resolutionOptions.IndexOf(current);
        if (index < 0)
        {
            resolutionOptions.Add(current);
            labels.Add($"{current.x} × {current.y}（現在）");
            index = resolutionOptions.Count - 1;
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);
        resolutionDropdown.SetValueWithoutNotify(index);
    }

    private void InitEffects()
    {
        if (fastEffectsToggle) fastEffectsToggle.isOn = GameSettings.FastEffects;
        if (reduceShakeToggle) reduceShakeToggle.isOn = GameSettings.ReduceShake;
    }

    private void InitConversation()
    {
        if (textSpeedSlider)
        {
            textSpeedSlider.minValue = 0f;
            textSpeedSlider.maxValue = 1f;
            textSpeedSlider.wholeNumbers = false;
            textSpeedSlider.SetValueWithoutNotify(GameSettings.TextSpeed);
        }
        if (autoAdvanceToggle) autoAdvanceToggle.SetIsOnWithoutNotify(GameSettings.AutoAdvance);
        if (skipReadToggle) skipReadToggle.SetIsOnWithoutNotify(GameSettings.SkipRead);
    }

    private void InitInfo()
    {
        if (versionText) versionText.text = $"トコのトムトムランド  Ver {Application.version}";
    }

    private void InitTabs()
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            if (tabs[i].Button) tabs[i].Button.onClick.AddListener(() => ShowTab(index));
        }
    }

    private void ShowTab(int index)
    {
        int previous = currentTab;
        currentTab = index;
        for (int i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            bool selected = i == index;
            if (tab.Group) tab.Group.SetActive(selected);

            // 選択中のタブは「押し込んだ絵＋金色の文字」で示す。押せないようにはしない
            // （以前は interactable=false にしていたため、選んでいるタブが半透明で一番薄く見えていた）
            if (tab.Image != null && tabSelectedSprite != null)
                tab.Image.sprite = selected ? tabSelectedSprite : tab.NormalSprite;
            if (tab.Label != null)
                tab.Label.color = selected ? tabSelectedLabelColor : tab.NormalLabelColor;
        }

        if (focusScope != null)
        {
            var target = TabFocusTarget(index);
            if (target != null) focusScope.FirstSelected = target;
        }

        PlayTabSwitch(previous, index); // 見た目だけ（タブの中身の表示は上で決めてある）
    }

    // ---- 変更の反映 ----

    private void Bind()
    {
        // サウンド: 即時反映（ディスクへは閉じるときに書き出す）
        if (bgmSlider)
            bgmSlider.onValueChanged.AddListener(value =>
            {
                SoundManager.Instance.SetBGMVolume(value);
                GameSettings.BgmVolume = value;
            });

        if (seSlider)
            seSlider.onValueChanged.AddListener(value =>
            {
                SoundManager.Instance.SetSEVolume(value);
                GameSettings.SeVolume = value;
                sePreviewAt = Time.unscaledTime + SePreviewDelay; // 動かし終わったら試し鳴らし
            });

        // 画面: 即時反映（保存は Unity が行う）
        if (displayModeDropdown)
            displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);
        else if (fullscreenToggle)
            fullscreenToggle.onValueChanged.AddListener(isOn =>
                OnDisplayModeChanged(isOn ? ModeIndex(FullScreenMode.FullScreenWindow) : ModeIndex(FullScreenMode.Windowed)));

        if (resolutionDropdown)
            resolutionDropdown.onValueChanged.AddListener(index =>
            {
                if (index < 0 || index >= resolutionOptions.Count) return;
                ApplyScreen(resolutionOptions[index], Screen.fullScreenMode);
            });

        // 演出・快適性: 参照側が都度 GameSettings を見る
        if (fastEffectsToggle)
            fastEffectsToggle.onValueChanged.AddListener(isOn => GameSettings.FastEffects = isOn);

        if (reduceShakeToggle)
            reduceShakeToggle.onValueChanged.AddListener(isOn => GameSettings.ReduceShake = isOn);

        // 会話: 宴（ScenarioPlayer）が GameSettings.Changed を受けて反映する
        if (textSpeedSlider)
            textSpeedSlider.onValueChanged.AddListener(value => GameSettings.TextSpeed = value);
        if (autoAdvanceToggle)
            autoAdvanceToggle.onValueChanged.AddListener(isOn => GameSettings.AutoAdvance = isOn);
        if (skipReadToggle)
            skipReadToggle.onValueChanged.AddListener(isOn => GameSettings.SkipRead = isOn);

        // 閉じる
        closeBtn.onClick.AddListener(() => CloseSelf());

        // タイトルへ戻る。タイトル画面と配信中（FightScene）では出さない
        // （配信は途中で抜けると決着が付かない。配信中に終了した場合は「続きから」でその配信をやり直す）
        if (toTitleBtn != null)
        {
            string activeScene = SceneManager.GetActiveScene().name;
            bool hide = activeScene == TitleSceneName || activeScene == FightSceneName;
            toTitleBtn.gameObject.SetActive(!hide);
            toTitleBtn.onClick.AddListener(RequestReturnToTitle);
        }
    }

    // ---- 画面 ----

    private static int ModeIndex(FullScreenMode mode)
    {
        if (mode == FullScreenMode.MaximizedWindow) mode = FullScreenMode.Windowed;
        int i = Array.IndexOf(DisplayModes, mode);
        return i >= 0 ? i : 0;
    }

    /// <summary>モニターの大きさ（デスクトップの解像度）。</summary>
    private static Vector2Int NativeSize()
    {
        var display = Display.main;
        if (display != null && display.systemWidth > 0 && display.systemHeight > 0)
            return new Vector2Int(display.systemWidth, display.systemHeight);
        var cur = Screen.currentResolution;
        return new Vector2Int(cur.width, cur.height);
    }

    /// <summary>
    /// 表示モードを変える。フルスクリーン系はモニターの大きさ、ウィンドウはモニターの約8割（16:9）にする
    /// （モニターと同じ大きさのウィンドウはタイトルバーとタスクバーで下が切れるため）。
    /// </summary>
    private void OnDisplayModeChanged(int index)
    {
        if (index < 0 || index >= DisplayModes.Length) return;
        var mode = DisplayModes[index];
        var native = NativeSize();
        var size = new Vector2Int(Screen.width, Screen.height);
        if (mode == FullScreenMode.Windowed)
        {
            if (size.x >= native.x * 0.95f || size.y >= native.y * 0.95f) size = WindowedDefaultSize(native);
        }
        else
        {
            size = native;
        }
        ApplyScreen(size, mode);
    }

    private static Vector2Int WindowedDefaultSize(Vector2Int native)
    {
        int w = Mathf.RoundToInt(native.x * 0.8f);
        int h = Mathf.RoundToInt(w * 9f / 16f);
        if (h > native.y * 0.85f)
        {
            h = Mathf.RoundToInt(native.y * 0.8f);
            w = Mathf.RoundToInt(h * 16f / 9f);
        }
        return new Vector2Int(Mathf.Max(640, w), Mathf.Max(360, h));
    }

    private void ApplyScreen(Vector2Int size, FullScreenMode mode)
    {
        Screen.SetResolution(size.x, size.y, mode);
        // Screen.width などは次のフレームまで変わらないので、選択肢は変更先の値で作り直す
        RefreshResolutionOptions(size);
        if (displayModeDropdown) displayModeDropdown.SetValueWithoutNotify(ModeIndex(mode));
    }

    private const string TitleSceneName = "TitleScene";
    private const string FightSceneName = "FightScene";

    /// <summary>
    /// 閉じる。animate なら #1 窓が縮んで消えてからシーンを外す（0.15 秒。省略モードでは即）。
    /// 保存と OnCloseButtonClicked は今までどおり押した瞬間に行う。
    /// </summary>
    private void CloseSelf(bool animate = true)
    {
        if (isClosing) return;
        isClosing = true;
        GameSettings.Save();
        OnCloseButtonClicked.OnNext(Unit.Default);
        if (!animate)
        {
            SceneManager.UnloadSceneAsync("Setting");
            return;
        }
        UIMotion.Close(gameObject, () =>
        {
            var scene = SceneManager.GetSceneByName("Setting");
            if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        });
    }

    /// <summary>
    /// 「タイトルへ」: 設定画面を閉じてから、店の確認ポップアップで聞く（誤タップ1回で戻らないように）。
    /// 設定画面の上にポップアップを重ねるとフォーカスと表示順が設定画面と取り合うので、先に閉じる。
    /// ポップアップを出せない構成（店以外のシーン）では従来どおりすぐ戻る。
    /// </summary>
    private void RequestReturnToTitle()
    {
        if (isClosing) return;

        var scope = LifetimeScope.Find<GameLifetimeScope>();
        PopUpManager popUpManager = null;
        GameFlowManager gameFlow = null;
        if (scope != null && scope.Container != null)
        {
            scope.Container.TryResolve(out popUpManager);
            scope.Container.TryResolve(out gameFlow);
        }

        if (popUpManager == null)
        {
            isClosing = true;
            // Single ロードで Setting（加算シーン）ごと破棄される。
            // ゲーム側の保存は GameLifecycleHandler.Dispose（スコープ破棄時）が行う。
            SceneLoader.Load(TitleSceneName);
            return;
        }

        bool beforeStream = gameFlow != null && gameFlow.IsAwaitingStream.Value;
        string message = beforeStream
            ? "タイトルへ戻りますか？\n店の状態は保存されます。\n「続きから」では、今日の配信から始まります。"
            : "タイトルへ戻りますか？\n店の状態は保存されます。";

        CloseSelf(animate: false); // すぐ上に確認を出すので、閉じ演出で重ならないよう即外す
        popUpManager.Show(new PopUpData
        {
            Title = "タイトルへ戻る",
            Message = message,
            ConfirmButtonText = "タイトルへ",
            CancelButtonText = "やめる",
            Size = PopupSizeEnum.Medium,
            // ゲーム側の保存は GameLifecycleHandler.Dispose（スコープ破棄時）が行う
            OnConfirm = () => SceneLoader.Load(TitleSceneName),
        });
    }
}
