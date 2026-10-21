using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 村シーンのView（歩ける村のHUD・投資パネル・帰還収支ポップ）。
/// 参照は全てnull-safe。departButton が未配線の間は IsInteractiveReady=false となり、
/// Presenter が PreparationScene へ素通りする（既存規約）。
/// </summary>
public class VillageView : MonoBehaviour
{
    [Header("HUD")]
    [SerializeField] private TextMeshProUGUI villageFundsText;
    [SerializeField] private TextMeshProUGUI metaCurrencyText;
    [SerializeField] private TextMeshProUGUI villageLevelText;
    [Tooltip("任意: 次の村の格の条件（未配線なら villageLevelText の2行目に小さく出す）")]
    [SerializeField] private TextMeshProUGUI rankNextText;
    [SerializeField] private Button departButton;     // 出店準備へ（必須。未配線なら素通り）
    [SerializeField] private Button titleButton;      // タイトルへ
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("施設区画（シーン手置き）")]
    [SerializeField] private FacilityPlot[] plots;

    [Header("投資パネル")]
    [SerializeField] private GameObject investPanel;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TextMeshProUGUI detailNameText;
    [SerializeField] private TextMeshProUGUI detailLevelText;
    [SerializeField] private TextMeshProUGUI currentEffectText;
    [SerializeField] private TextMeshProUGUI nextEffectText;
    [Tooltip("次Lv の見出し（Lv5 の欄では「特殊な効果」に変わる）")]
    [SerializeField] private TextMeshProUGUI nextCaptionText;
    [Tooltip("Lv5（特殊な効果）の欄だけ出す金の枠（任意）")]
    [SerializeField] private GameObject specialFrame;
    [SerializeField] private TextMeshProUGUI detailCostText;
    [SerializeField] private Button investButton;
    [SerializeField] private TextMeshProUGUI investButtonLabel;
    [SerializeField] private Button investCloseButton;

    [Header("帰還収支ポップ")]
    [SerializeField] private GameObject conversionPopup;
    [SerializeField] private TextMeshProUGUI conversionTitleText;
    [SerializeField] private TextMeshProUGUI conversionEarnedText;
    [SerializeField] private TextMeshProUGUI conversionConvertedText;
    [SerializeField] private Button conversionCloseButton;

    [Header("自己ベストの掲示板（任意。王都の広場の看板を調べると開く）")]
    [SerializeField] private GameObject recordBoardPopup;
    [SerializeField] private TextMeshProUGUI recordBoardTitleText;
    [SerializeField] private TextMeshProUGUI recordBoardBodyText;
    [SerializeField] private Button recordBoardCloseButton;

    [Header("帰還到着演出（任意。未設定なら子→シーンから自動検索。無ければ上の収支ポップ）")]
    [SerializeField] private VillageArrivalFx arrivalFx;

    [Header("町人（任意。未設定ならシーンから自動検索。総合Lvに応じて増える）")]
    [SerializeField] private VillagePopulationView population;

    public Subject<Unit> OnDepart { get; } = new();
    public Subject<Unit> OnGoTitle { get; } = new();
    public Subject<Unit> OnInvest { get; } = new();
    public Subject<Unit> OnPanelClosed { get; } = new();
    public Subject<Unit> OnConversionClosed { get; } = new();

    private const string SpecialCaption = "特殊な効果";
    private static readonly Color SpecialCaptionColor = new(0.91f, 0.64f, 0.24f); // ui-rules の強調の金 #E8A33D
    private const float SpecialCaptionMaxSize = 20f; // 「特殊な効果」は5文字なので、見出しの枠（幅120）に収まる大きさにする
    private string defaultNextCaption;
    private Color defaultNextCaptionColor;
    private float defaultNextCaptionMaxSize;
    private HorizontalAlignmentOptions defaultNextCaptionAlign;

    public IReadOnlyList<FacilityPlot> Plots => plots ?? System.Array.Empty<FacilityPlot>();

    /// <summary>出店ボタンが配線済みか（falseならPresenterが準備シーンへ素通りする）。</summary>
    public bool IsInteractiveReady => departButton != null;

    // ───────── ゲームパッド/キー操作（フォーカス） ─────────
    private static VillageView instance;
    private UIFocusScope fieldScope;
    private UIFocusScope investScope;
    private UIFocusScope conversionScope;
    private bool hudMode;               // 「歩く」から「HUDボタンを選ぶ」へ切り替え中
    private bool fieldFreeLastFrame;    // 直前フレームの終わりに、画面が歩ける状態だったか
    private readonly List<PlayerInput> lockedInputs = new();

    /// <summary>
    /// 区画の「調べる」を決定キー/ボタンで受け付けてよいか。UI操作中（パネルを開いている・HUD選択中）と、
    /// UIを閉じた同じフレームは false（閉じる決定が区画の調べるにも届いて再度開くのを防ぐ）。
    /// </summary>
    public static bool FieldInputAllowed => instance == null || (instance.IsFieldFreeNow && instance.fieldFreeLastFrame);

    private bool IsFieldFreeNow => !hudMode && (fieldScope == null || UIFocusManager.Top == fieldScope);

    private void Awake()
    {
        instance = this;
        SetupFocusScopes();
        if (departButton != null)
            departButton.onClick.AddListener(() => OnDepart.OnNext(Unit.Default));
        if (titleButton != null)
            titleButton.onClick.AddListener(() => OnGoTitle.OnNext(Unit.Default));
        if (investButton != null)
            investButton.onClick.AddListener(() => OnInvest.OnNext(Unit.Default));
        if (investCloseButton != null)
            investCloseButton.onClick.AddListener(() =>
            {
                HideInvestPanel();
                OnPanelClosed.OnNext(Unit.Default);
            });
        if (conversionCloseButton != null)
            conversionCloseButton.onClick.AddListener(() =>
            {
                if (conversionPopup != null) conversionPopup.SetActive(false);
                OnConversionClosed.OnNext(Unit.Default);
            });
        if (recordBoardCloseButton != null)
            recordBoardCloseButton.onClick.AddListener(HideRecordBoard);

        if (nextCaptionText != null)
        {
            defaultNextCaption = nextCaptionText.text;
            defaultNextCaptionColor = nextCaptionText.color;
            defaultNextCaptionMaxSize = nextCaptionText.fontSizeMax;
            defaultNextCaptionAlign = nextCaptionText.horizontalAlignment;
        }
        if (specialFrame != null) specialFrame.SetActive(false);

        if (investPanel != null) investPanel.SetActive(false);
        if (conversionPopup != null) conversionPopup.SetActive(false);
        if (recordBoardPopup != null) recordBoardPopup.SetActive(false);
    }

    private void SetupFocusScopes()
    {
        // 村はフォールバック自動選択が無効（歩く画面）。何も選ばない親スコープを置き、Start/Esc で HUD ボタン選択に入る
        fieldScope = UIFocusScope.GetOrAdd(gameObject);
        fieldScope.AutoSelect = false;
        fieldScope.RememberLastSelected = false;
        fieldScope.SetCancelAction(ToggleHudMode);
        fieldScope.OnMenuEvent.AddListener(ToggleHudMode);

        if (investPanel != null)
        {
            investScope = UIFocusScope.GetOrAdd(investPanel);
            investScope.Modal = true;
            investScope.Priority = 10;
            investScope.SetCancelButton(investCloseButton);
            investScope.FirstSelected = investCloseButton; // 投資の決定は誤爆しにくいよう、最初は閉じるに置く
        }
        if (conversionPopup != null)
        {
            conversionScope = UIFocusScope.GetOrAdd(conversionPopup);
            conversionScope.Modal = true;
            conversionScope.Priority = 10;
            conversionScope.SetCancelButton(conversionCloseButton);
            conversionScope.FirstSelected = conversionCloseButton;
        }
        if (recordBoardPopup != null)
        {
            var recordScope = UIFocusScope.GetOrAdd(recordBoardPopup);
            recordScope.Modal = true;
            recordScope.Priority = 10;
            recordScope.SetCancelButton(recordBoardCloseButton);
            recordScope.FirstSelected = recordBoardCloseButton;
        }
    }

    private bool ArrivalPlaying => ArrivalFx != null && ArrivalFx.IsPlaying;

    private void ToggleHudMode()
    {
        if (hudMode) { ExitHudMode(); return; }
        if (ArrivalPlaying) return;
        var first = departButton != null && departButton.IsInteractable() ? departButton : titleButton;
        if (first == null || !ControllerInput.IsNavigating.CurrentValue) return;
        hudMode = true;
        fieldScope.Select(first);
    }

    private void ExitHudMode()
    {
        hudMode = false;
        UIFocusManager.Select(null);
    }

    private void Update()
    {
        if (ArrivalPlaying) return; // 到着演出は自前で入力をロックする

        bool navigating = ControllerInput.IsNavigating.CurrentValue;
        if (hudMode && !navigating) hudMode = false; // マウスに切り替えたら歩く操作へ戻す

        // パッド/キー操作でUIを触っている間は、スティックで村の主人公が歩かないようにする
        bool uiFocused = navigating && (hudMode || (UIFocusManager.Top != null && UIFocusManager.Top != fieldScope));
        if (uiFocused) LockPlayerInput(); else UnlockPlayerInput();
    }

    private void LateUpdate()
    {
        fieldFreeLastFrame = IsFieldFreeNow;
    }

    private void LockPlayerInput()
    {
        if (lockedInputs.Count > 0) return;
        foreach (var pi in PlayerInput.all)
        {
            if (pi == null || !pi.inputIsActive) continue;
            pi.DeactivateInput(); // 進行中の移動はキャンセルされゼロに戻る
            lockedInputs.Add(pi);
        }
    }

    private void UnlockPlayerInput()
    {
        if (lockedInputs.Count == 0) return;
        foreach (var pi in lockedInputs)
            if (pi != null) pi.ActivateInput();
        lockedInputs.Clear();
    }

    private void OnDestroy()
    {
        UnlockPlayerInput();
        if (instance == this) instance = null;
        OnDepart.Dispose();
        OnGoTitle.Dispose();
        OnInvest.Dispose();
        OnPanelClosed.Dispose();
        OnConversionClosed.Dispose();
    }

    /// <summary>
    /// HUD を更新する。村の名前に格を添え（「トコの村（町・総合Lv12）」）、次の格の条件を出す（nextRankLine が空なら出さない）。
    /// </summary>
    public void UpdateHud(int villageFunds, int villageLevel, string rankName = null, string nextRankLine = null)
    {
        SetFundsDisplay(villageFunds);
        // metaCurrencyText（旧・銀行預金/のれん表示）は村資金へ統合したため使わない
        if (metaCurrencyText != null) metaCurrencyText.gameObject.SetActive(false);
        string head = string.IsNullOrEmpty(rankName)
            ? $"トコの村（総合Lv{villageLevel}）"
            : $"トコの村（{rankName}・総合Lv{villageLevel}）";
        if (rankNextText != null)
        {
            rankNextText.text = nextRankLine ?? "";
            rankNextText.gameObject.SetActive(!string.IsNullOrEmpty(nextRankLine));
            if (villageLevelText != null) villageLevelText.text = head;
        }
        else if (villageLevelText != null)
        {
            villageLevelText.text = string.IsNullOrEmpty(nextRankLine)
                ? head
                : $"{head}\n<size=62%>{nextRankLine}</size>";
        }
    }

    /// <summary>村の格が上がったことを帰還収支ポップの枠で出す（格の名前を大きく。閉じると OnConversionClosed）。</summary>
    public bool ShowRankUpPopup(string title, string rankLine, string unlockLine)
    {
        if (conversionPopup == null) return false;
        conversionPopup.SetActive(true);
        UIFx.PanelOpen(conversionPopup);
        if (conversionTitleText != null) conversionTitleText.text = title;
        // 格の名前は大きい金の行（村資金の額を出す行）に出して上へ、解放の説明は小さい行に出して下へ入れ替える
        SwapConversionRows(true);
        if (conversionConvertedText != null)
        {
            DOTween.Kill(conversionConvertedText.gameObject);
            conversionConvertedText.text = rankLine;
            var t = conversionConvertedText.transform;
            t.DOKill();
            t.localScale = Vector3.one * 0.6f;
            t.DOScale(1f, 0.45f).SetEase(Ease.OutBack).SetDelay(0.15f).SetLink(conversionConvertedText.gameObject);
        }
        if (conversionEarnedText != null) conversionEarnedText.text = unlockLine ?? "";
        if (conversionCloseButton != null) UIFx.Pop(conversionCloseButton.transform);
        return true;
    }

    private Vector2? earnedHome, convertedHome;

    /// <summary>帰還収支ポップの2行（儲けの行・村資金の行）の位置を入れ替える／戻す。</summary>
    private void SwapConversionRows(bool swapped)
    {
        if (conversionEarnedText == null || conversionConvertedText == null) return;
        var e = conversionEarnedText.rectTransform;
        var c = conversionConvertedText.rectTransform;
        earnedHome ??= e.anchoredPosition;
        convertedHome ??= c.anchoredPosition;
        e.anchoredPosition = swapped ? convertedHome.Value : earnedHome.Value;
        c.anchoredPosition = swapped ? earnedHome.Value : convertedHome.Value;
    }

    public bool IsConversionPopupOpen => conversionPopup != null && conversionPopup.activeSelf;

    /// <summary>自己ベストの掲示板を開く（未配線ならメッセージ欄に出す）。</summary>
    public void ShowRecordBoard(string title, string body)
    {
        if (recordBoardPopup == null)
        {
            ShowMessage($"{title}\n{body}");
            return;
        }
        recordBoardPopup.SetActive(true);
        UIFx.PanelOpen(recordBoardPopup);
        if (recordBoardTitleText != null) recordBoardTitleText.text = title;
        if (recordBoardBodyText != null) recordBoardBodyText.text = body;
    }

    public void HideRecordBoard()
    {
        if (recordBoardPopup != null) recordBoardPopup.SetActive(false);
    }

    public bool IsRecordBoardOpen => recordBoardPopup != null && recordBoardPopup.activeSelf;

    /// <summary>村資金HUDの数値だけ更新する（到着演出のカウントアップ用）。</summary>
    public void SetFundsDisplay(int villageFunds)
    {
        if (villageFundsText != null) villageFundsText.text = $"村資金 {villageFunds:N0}G";
    }

    /// <summary>村資金HUDのテキスト（到着演出のコイン到達先）。</summary>
    public TMP_Text FundsText => villageFundsText;

    /// <summary>帰還到着演出（未配線なら子→シーンから探す。無ければnull）。</summary>
    public VillageArrivalFx ArrivalFx
    {
        get
        {
            if (arrivalFx == null) arrivalFx = GetComponentInChildren<VillageArrivalFx>(true);
            if (arrivalFx == null) arrivalFx = FindFirstObjectByType<VillageArrivalFx>(FindObjectsInactive.Include);
            return arrivalFx;
        }
    }

    /// <summary>総合Lvに応じて増える町人（未配線ならシーンから探す。無ければnull）。</summary>
    public VillagePopulationView Population
    {
        get
        {
            if (population == null) population = FindFirstObjectByType<VillagePopulationView>(FindObjectsInactive.Include);
            return population;
        }
    }

    /// <summary>
    /// 村HUDのキャンバスを描いているカメラ（Screen Space - Camera の場合）。
    /// チュートリアルの暗幕を宴の立ち絵より下に描くために使う。取れなければ null。
    /// </summary>
    public Camera UiCamera
    {
        get
        {
            var canvas = RootCanvas;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
                return canvas.worldCamera;
            return Camera.main;
        }
    }

    private Canvas RootCanvas
    {
        get
        {
            var anchor = departButton != null ? departButton.transform : transform;
            var canvas = anchor.GetComponentInParent<Canvas>(true);
            return canvas != null ? canvas.rootCanvas : null;
        }
    }

    private CanvasGroup uiLockGroup;
    private bool uiLockPrevBlocks = true;
    private bool uiLocked;

    /// <summary>
    /// 村HUD（出店・タイトル・投資パネル等）のクリックを止める/戻す。会話チュートリアル中に使う。
    /// 見た目を変えないよう interactable ではなく blocksRaycasts を切り替える。
    /// </summary>
    public void SetUiInputLocked(bool locked)
    {
        if (locked == uiLocked) return;
        if (locked)
        {
            var canvas = RootCanvas;
            if (canvas == null) return;
            if (!canvas.TryGetComponent(out uiLockGroup)) uiLockGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            uiLockPrevBlocks = uiLockGroup.blocksRaycasts;
            uiLockGroup.blocksRaycasts = false;
            uiLocked = true;
        }
        else
        {
            if (uiLockGroup != null) uiLockGroup.blocksRaycasts = uiLockPrevBlocks;
            uiLockGroup = null;
            uiLocked = false;
        }
    }

    public void ShowMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }

    /// <summary>
    /// 投資パネルを表示する。nextIsSpecial=true なら「次Lv」の欄を Lv5 の特殊な効果として金の枠で出す
    /// （Lv4 のときは次に付く効果、Lv5 のときは今付いている効果）。
    /// </summary>
    public void ShowInvestPanel(VillageFacilityData facility, int level, string currentEffect, string nextEffect,
        int cost, bool canInvest, string blockedReason, bool nextIsSpecial = false)
    {
        if (investPanel == null) return;
        investPanel.SetActive(true);

        if (detailIcon != null)
        {
            detailIcon.sprite = facility.icon;
            detailIcon.enabled = facility.icon != null;
        }
        if (detailNameText != null) detailNameText.text = facility.facilityName;
        if (detailLevelText != null)
        {
            detailLevelText.text = level >= facility.MaxLevel
                ? $"Lv{level}（MAX）"
                : (level == 0 ? "未建設" : $"Lv{level} → Lv{level + 1}");
        }
        if (currentEffectText != null)
            currentEffectText.text = string.IsNullOrEmpty(currentEffect) ? "-" : currentEffect;
        if (nextEffectText != null)
            nextEffectText.text = string.IsNullOrEmpty(nextEffect) ? "これ以上の拡張はない" : nextEffect;
        if (nextCaptionText != null)
        {
            nextCaptionText.text = nextIsSpecial ? SpecialCaption : defaultNextCaption;
            nextCaptionText.color = nextIsSpecial ? SpecialCaptionColor : defaultNextCaptionColor;
            nextCaptionText.fontSizeMax = nextIsSpecial ? Mathf.Min(SpecialCaptionMaxSize, defaultNextCaptionMaxSize) : defaultNextCaptionMaxSize;
            nextCaptionText.horizontalAlignment = nextIsSpecial ? HorizontalAlignmentOptions.Center : defaultNextCaptionAlign;
        }
        if (specialFrame != null) specialFrame.SetActive(nextIsSpecial);

        if (detailCostText != null)
            detailCostText.text = cost < 0 ? "-" : $"{cost:N0}G";

        if (investButton != null) investButton.interactable = canInvest;
        if (investButtonLabel != null)
        {
            investButtonLabel.text = cost < 0
                ? "最大レベル"
                : canInvest ? (level == 0 ? "建設する" : "拡張する") : blockedReason;
        }
    }

    public void HideInvestPanel()
    {
        if (investPanel != null) investPanel.SetActive(false);
    }

    public bool IsInvestPanelOpen => investPanel != null && investPanel.activeSelf;

    /// <summary>帰還収支ポップを表示する（村資金のカウントアップ演出付き）。</summary>
    public void ShowConversionPopup(bool cleared, int earned, int converted)
    {
        if (conversionPopup == null) return;
        conversionPopup.SetActive(true);
        SwapConversionRows(false); // 格上げで入れ替えた行を戻す
        if (conversionConvertedText != null) conversionConvertedText.transform.localScale = Vector3.one;

        if (conversionTitleText != null)
            conversionTitleText.text = cleared ? "ランクリア！　村への還元" : "破産……　それでも村は待っている";
        if (conversionEarnedText != null)
        {
            string sign = earned >= 0 ? "+" : "−";
            conversionEarnedText.text = cleared
                ? $"今回の儲け {sign}{Mathf.Abs(earned):N0}G"
                : $"いちばん儲かっていたとき {sign}{Mathf.Abs(earned):N0}G";
        }

        if (conversionConvertedText != null)
        {
            int shown = 0;
            conversionConvertedText.text = "村へ +0G";
            DOTween.To(() => shown, x =>
                {
                    shown = x;
                    conversionConvertedText.text = $"村へ +{x:N0}G";
                }, converted, 0.8f)
                .SetEase(Ease.OutCubic)
                .SetLink(conversionConvertedText.gameObject);
        }
    }
}
