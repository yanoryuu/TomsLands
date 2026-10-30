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
    [Tooltip("任意: メッセージの帯（文字が空のときは隠す）")]
    [SerializeField] private GameObject messageBand;

    [Header("HUD の看板（任意。未配線なら村名の行に格と総合Lvを並べる）")]
    [Tooltip("格と総合Lv（「町・総合Lv18」）。配線すると villageLevelText は村の名前だけになる")]
    [SerializeField] private TextMeshProUGUI rankText;
    [Tooltip("格ごとに見た目を変える看板の板（任意）")]
    [SerializeField] private Image rankBanner;
    [Tooltip("格の番号ごとの看板の絵（足りない分は最後を使う。空なら変えない）")]
    [SerializeField] private Sprite[] rankBannerSprites;
    [Tooltip("格の印（星）。格の番号+1 個を点ける")]
    [SerializeField] private Image[] rankStars;
    [SerializeField] private Sprite rankStarOn;
    [SerializeField] private Sprite rankStarOff;
    [Tooltip("格に応じて出す飾り（旗・冠など）")]
    [SerializeField] private RankOrnament[] rankOrnaments;

    [System.Serializable]
    public struct RankOrnament
    {
        public GameObject target;
        [Tooltip("この格の番号以上で出す")]
        public int minRank;
        [Tooltip("この格の番号以下で出す（-1 なら上限なし）")]
        public int maxRank;
    }

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
    [Tooltip("任意: 費用と比べる今の村資金")]
    [SerializeField] private TextMeshProUGUI detailFundsText;
    [Tooltip("費用が村資金で足りるときの色")]
    [SerializeField] private Color costAffordableColor = new(0.91f, 0.64f, 0.24f); // 金 #E8A33D
    [Tooltip("費用が村資金に足りないときの色")]
    [SerializeField] private Color costShortColor = new(0.66f, 0.2f, 0.17f);       // 赤 #A8332B
    [Tooltip("施設マスターに icon が無いときに使うアイコン（facilityId ごと）")]
    [SerializeField] private FacilityIcon[] facilityIcons;

    [System.Serializable]
    public struct FacilityIcon
    {
        public string facilityId;
        public Sprite icon;
    }
    [SerializeField] private Button investButton;
    [SerializeField] private TextMeshProUGUI investButtonLabel;
    [Tooltip("投資ボタンが押せないときの文字の色（グレーアウト）")]
    [SerializeField] private Color investLabelDisabledColor = new(0.82f, 0.8f, 0.76f);
    [SerializeField] private Button investCloseButton;

    [Header("帰還収支ポップ")]
    [SerializeField] private GameObject conversionPopup;
    [SerializeField] private TextMeshProUGUI conversionTitleText;
    [SerializeField] private TextMeshProUGUI conversionEarnedText;
    [SerializeField] private TextMeshProUGUI conversionConvertedText;
    [SerializeField] private Button conversionCloseButton;
    [Tooltip("任意: 内訳を入れる枠（ポップの子・下寄せ）。配線するとポップの中に内訳を出し、内訳の有無でポップの高さを伸び縮みさせる")]
    [SerializeField] private RectTransform conversionBreakdownArea;

    [Header("自己ベストの掲示板（任意。王都の広場の看板を調べると開く）")]
    [SerializeField] private GameObject recordBoardPopup;
    [SerializeField] private TextMeshProUGUI recordBoardTitleText;
    [SerializeField] private TextMeshProUGUI recordBoardBodyText;
    [SerializeField] private Button recordBoardCloseButton;

    [Header("帰還到着演出（任意。未設定なら子→シーンから自動検索。無ければ上の収支ポップ）")]
    [SerializeField] private VillageArrivalFx arrivalFx;

    [Header("小窓の後ろの幕（任意。投資パネル・帰還/格上げポップ・掲示板を開いている間だけ出す）")]
    [Tooltip("全画面の黒の幕（押すと投資パネル・掲示板は閉じる。帰還/格上げは閉じない）")]
    [SerializeField] private Image modalDim;
    [SerializeField] private float modalDimAlpha = 0.8f; // リニア色空間なので 0.8 で見た目 55% ほど暗くなる

    [Header("投資パネルの追加（任意）")]
    [Tooltip("最大レベルのときに投資ボタンの代わりに出す「MAX」の札")]
    [SerializeField] private GameObject maxLevelPlate;
    [Tooltip("「費用」の見出し（最大レベルのときは費用の行ごと隠す）")]
    [SerializeField] private TextMeshProUGUI costCaptionText;
    [Tooltip("「現在」の行（未建設のときは隠して「次Lv」の行をこの位置へ上げる）")]
    [SerializeField] private RectTransform currentEffectRow;
    [SerializeField] private RectTransform nextEffectRow;

    [Header("格上げポップの飾り（任意。帰還収支ポップの枠を使う）")]
    [Tooltip("格上げのときだけ使う金寄りの枠（null なら変えない）")]
    [SerializeField] private Sprite rankUpPopupSprite;
    [Tooltip("ポップの後ろで回る放射状の光（ポップの兄弟・ポップより手前の順）")]
    [SerializeField] private Image rankUpRays;
    [Tooltip("ポップの上の紋章（格の番号ごとの絵は rankUpEmblems）")]
    [SerializeField] private Image rankUpEmblem;
    [SerializeField] private Sprite[] rankUpEmblems;
    [Tooltip("星の「前→後」の行")]
    [SerializeField] private GameObject rankUpStarsRow;
    [SerializeField] private Image[] rankUpStarsBefore;
    [SerializeField] private Image[] rankUpStarsAfter;
    [Tooltip("格上げのときにポップを縦に伸ばす量（星の行のぶん）")]
    [SerializeField] private float rankUpExtraHeight = 76f;

    [Header("自己ベストの表（任意。未配線なら recordBoardBodyText に1行ずつ並べる）")]
    [SerializeField] private TextMeshProUGUI recordBoardAmountText;
    [SerializeField] private TextMeshProUGUI recordBoardRankText;

    [Header("施設へのショートカット（任意。画面左端のつまみで開く帯）")]
    [Tooltip("左から滑り出る帯（左端・縦中央。閉じているときは帯の幅だけ左へ隠す）")]
    [SerializeField] private RectTransform shortcutDrawer;
    [Tooltip("帯の右に付いたつまみ（常に見える）")]
    [SerializeField] private Button shortcutKnob;
    [Tooltip("つまみの矢印（閉じているときは右向き、開いているときは左向き）")]
    [SerializeField] private RectTransform shortcutKnobArrow;
    [Tooltip("施設ボタンの雛形（非アクティブ。子に Icon / Name / Level / Badge）")]
    [SerializeField] private Button shortcutButtonTemplate;
    [Tooltip("施設ボタンを並べる入れ物（閉じている間は押せなくする）")]
    [SerializeField] private CanvasGroup shortcutListGroup;
    [Tooltip("記録掲示板のボタンのアイコン")]
    [SerializeField] private Sprite recordBoardIcon;
    [SerializeField] private float shortcutSlideDuration = 0.25f;

    [Header("町人（任意。未設定ならシーンから自動検索。総合Lvに応じて増える）")]
    [SerializeField] private VillagePopulationView population;

    public Subject<Unit> OnDepart { get; } = new();
    public Subject<Unit> OnGoTitle { get; } = new();
    public Subject<Unit> OnInvest { get; } = new();
    public Subject<Unit> OnPanelClosed { get; } = new();
    public Subject<Unit> OnConversionClosed { get; } = new();
    /// <summary>ショートカットの帯で施設（または記録掲示板 = VillagePresenter.RecordBoardId）が選ばれた。</summary>
    public Subject<string> OnShortcutFacility { get; } = new();

    /// <summary>true の間はショートカットのつまみ・ボタンを受け付けない（Presenter が演出・会話中を返す）。</summary>
    public System.Func<bool> ShortcutBlocked { get; set; }

    private const string SpecialCaption = "特殊な効果";
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
        {
            departButton.onClick.AddListener(() => OnDepart.OnNext(Unit.Default));
            ButtonFeel.SetPrimary(departButton); // #4 この画面で次に押してほしいボタン
        }
        if (titleButton != null)
            titleButton.onClick.AddListener(() => OnGoTitle.OnNext(Unit.Default));
        if (investButton != null)
            investButton.onClick.AddListener(() => OnInvest.OnNext(Unit.Default));
        if (investCloseButton != null)
            investCloseButton.onClick.AddListener(() =>
            {
                if (UIMotion.IsClosing(investPanel)) return; // 閉じ演出中の 2 度押し（Cancel の連打など）
                HideInvestPanel();
                OnPanelClosed.OnNext(Unit.Default);
            });
        if (conversionCloseButton != null)
            conversionCloseButton.onClick.AddListener(() =>
            {
                if (UIMotion.IsClosing(conversionPopup)) return; // 閉じ演出中の 2 度押しで OnConversionClosed を 2 回出さない
                // #1 縮んで消える。格上げの飾りを外すのは消えてから（消える途中で枠の絵・高さが変わらないように）
                ClosePanel(conversionPopup, () => ApplyRankUpDecor(false, -1, -1));
                RefreshModalDim();
                OnConversionClosed.OnNext(Unit.Default);
            });
        if (recordBoardCloseButton != null)
            recordBoardCloseButton.onClick.AddListener(HideRecordBoard);
        if (modalDim != null && modalDim.TryGetComponent<Button>(out var dimButton))
            dimButton.onClick.AddListener(OnModalDimClicked);
        SetupShortcuts();

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
        ApplyRankUpDecor(false, -1, -1);
        RefreshModalDim(false);
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
        UpdateShortcuts();
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
        // 小窓をどこから閉じても（AutoPlay・チュートリアル等）幕が残らないように合わせる（消えていく途中の幕は「出ていない」とみなす）
        if (modalDim != null && (modalDim.gameObject.activeSelf && !dimFadingOut) != AnyModalOpen) RefreshModalDim();
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
        OnShortcutFacility.Dispose();
    }

    /// <summary>
    /// HUD を更新する。村の名前に格を添え（「トコの村（町・総合Lv12）」）、次の格の条件を出す（nextRankLine が空なら出さない）。
    /// </summary>
    public void UpdateHud(int villageFunds, int villageLevel, string rankName = null, string nextRankLine = null)
    {
        AnimateFunds(villageFunds);
        BumpLevelIfChanged(villageLevel);
        // metaCurrencyText（旧・銀行預金/のれん表示）は村資金へ統合したため使わない
        if (metaCurrencyText != null) metaCurrencyText.gameObject.SetActive(false);
        string head = string.IsNullOrEmpty(rankName)
            ? $"トコの村（総合Lv{villageLevel}）"
            : $"トコの村（{rankName}・総合Lv{villageLevel}）";
        if (rankText != null)
        {
            rankText.text = string.IsNullOrEmpty(rankName) ? $"総合Lv{villageLevel}" : $"{rankName}・総合Lv{villageLevel}";
            head = "トコの村";
        }
        ApplyRankStyle(RankIndexOf(rankName));
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

    /// <summary>格の名前から格の番号を引く（見つからなければ 0）。</summary>
    private static int RankIndexOf(string rankName)
    {
        if (string.IsNullOrEmpty(rankName)) return 0;
        for (int i = 0; i < VillageRank.Count; i++)
        {
            if (VillageRank.Name(i) == rankName) return i;
        }
        return 0;
    }

    /// <summary>看板の絵・星・飾りを格に合わせる（未配線の部品は飛ばす）。</summary>
    private void ApplyRankStyle(int rank)
    {
        if (rankBanner != null && rankBannerSprites != null && rankBannerSprites.Length > 0)
        {
            var sprite = rankBannerSprites[Mathf.Clamp(rank, 0, rankBannerSprites.Length - 1)];
            if (sprite != null) rankBanner.sprite = sprite;
        }
        if (rankStars != null)
        {
            for (int i = 0; i < rankStars.Length; i++)
            {
                var star = rankStars[i];
                if (star == null) continue;
                bool on = i <= rank;
                var sprite = on ? rankStarOn : rankStarOff;
                if (sprite != null) star.sprite = sprite;
                star.gameObject.SetActive(i < VillageRank.Count);
            }
        }
        if (rankOrnaments != null)
        {
            foreach (var o in rankOrnaments)
            {
                if (o.target == null) continue;
                o.target.SetActive(rank >= o.minRank && (o.maxRank < 0 || rank <= o.maxRank));
            }
        }
    }

    /// <summary>村の格が上がったことを帰還収支ポップの枠で出す（格の名前を大きく。閉じると OnConversionClosed）。</summary>
    public bool ShowRankUpPopup(string title, string rankLine, string unlockLine, int fromRank = -1, int toRank = -1)
    {
        if (conversionPopup == null) return false;
        OpenPanel(conversionPopup); // #1 ぽよんと開く
        if (conversionTitleText != null) conversionTitleText.text = title;
        // 格の名前は大きい金の行（村資金の額を出す行）に出して上へ、解放の説明は小さい行に出して下へ入れ替える
        SwapConversionRows(true);
        SetConversionBreakdown(null);
        ApplyRankUpDecor(true, fromRank, toRank);
        RefreshModalDim();
        PlayRankUpDecor(fromRank, toRank);
        if (conversionConvertedText != null)
        {
            DOTween.Kill(conversionConvertedText.gameObject);
            conversionConvertedText.text = rankLine;
            var t = conversionConvertedText.transform;
            t.DOKill();
            t.localScale = Vector3.one * 0.6f;
            UIMotion.Run(t.DOScale(1f, 0.45f).SetEase(Ease.OutBack).SetDelay(0.15f), conversionConvertedText.gameObject);
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

    /// <summary>帰還/格上げのポップが開いているか（閉じ演出中は閉じたものとみなす）。</summary>
    public bool IsConversionPopupOpen => IsShown(conversionPopup);

    // ───────── 格上げポップの飾り ─────────

    private Sprite conversionDefaultSprite;
    private bool conversionDefaultSpriteKnown;

    /// <summary>
    /// 格上げのときだけ、枠を金寄りにし・紋章・星の「前→後」・後ろの光を出す（on=false で帰還ポップの見た目へ戻す）。
    /// 動きは付けない（動きは PlayRankUpDecor。撮影ツールは編集モードでこちらだけ呼ぶ）。
    /// </summary>
    private void ApplyRankUpDecor(bool on, int fromRank, int toRank)
    {
        var popupImage = conversionPopup != null ? conversionPopup.GetComponent<Image>() : null;
        if (popupImage != null && !conversionDefaultSpriteKnown)
        {
            conversionDefaultSprite = popupImage.sprite;
            conversionDefaultSpriteKnown = true;
        }
        if (popupImage != null)
            popupImage.sprite = on && rankUpPopupSprite != null ? rankUpPopupSprite : conversionDefaultSprite;

        if (rankUpRays != null)
        {
            KillTweens(rankUpRays.transform);
            rankUpRays.transform.localRotation = Quaternion.identity;
            rankUpRays.gameObject.SetActive(on);
        }
        if (rankUpCelebrationDelay != null && rankUpCelebrationDelay.IsActive()) rankUpCelebrationDelay.Kill();
        rankUpCelebrationDelay = null;

        Sprite emblem = null;
        if (on && rankUpEmblems != null && rankUpEmblems.Length > 0 && toRank >= 0)
            emblem = rankUpEmblems[Mathf.Clamp(toRank, 0, rankUpEmblems.Length - 1)];
        if (rankUpEmblem != null)
        {
            KillTweens(rankUpEmblem.transform);
            rankUpEmblem.transform.localScale = Vector3.one;
            rankUpEmblem.sprite = emblem;
            rankUpEmblem.preserveAspect = true;
            rankUpEmblem.gameObject.SetActive(emblem != null);
        }

        bool stars = on && toRank >= 0 && rankUpStarsRow != null;
        if (rankUpStarsRow != null) rankUpStarsRow.SetActive(stars);
        if (stars)
        {
            int from = fromRank >= 0 ? fromRank : toRank - 1;
            SetRankUpStars(rankUpStarsBefore, from, -1);
            SetRankUpStars(rankUpStarsAfter, toRank, from);
        }

        // 星の行のぶんポップを伸ばす（内訳の無い高さが基準）
        var popupRect = conversionPopup != null ? (RectTransform)conversionPopup.transform : null;
        if (popupRect != null)
        {
            conversionPopupBaseHeight ??= popupRect.sizeDelta.y;
            float h = conversionPopupBaseHeight.Value + (stars ? rankUpExtraHeight : 0f);
            if (on || !ConversionBreakdownAreaShown)
                popupRect.sizeDelta = new Vector2(popupRect.sizeDelta.x, h);
        }
    }

    private bool ConversionBreakdownAreaShown => conversionBreakdownArea != null && conversionBreakdownArea.gameObject.activeSelf;

    /// <summary>動きを止める（撮影ツールが編集モードで見た目だけ作るときは DOTween を起こさない）。</summary>
    private static void KillTweens(Object target)
    {
        if (target != null && Application.isPlaying) DOTween.Kill(target);
    }

    /// <summary>格の星を点ける（rank 以下を点灯。newerThan より上の星は新しく増えた星として大きく）。</summary>
    private void SetRankUpStars(Image[] stars, int rank, int newerThan)
    {
        if (stars == null) return;
        for (int i = 0; i < stars.Length; i++)
        {
            var s = stars[i];
            if (s == null) continue;
            s.gameObject.SetActive(i < VillageRank.Count);
            bool lit = i <= rank;
            var sprite = lit ? rankStarOn : rankStarOff;
            if (sprite != null) s.sprite = sprite;
            KillTweens(s.transform);
            s.transform.localScale = Vector3.one * (newerThan >= 0 && lit && i > newerThan ? 1.3f : 1f);
        }
    }

    private Tween rankUpCelebrationDelay;

    /// <summary>
    /// 格上げポップの動き（ポップはぽよんと開く・紋章が祝いのハンコで押されてからポップに収まる・増えた星が順に弾む・後ろの光がゆっくり回る）。
    /// #5 祝い: 紋章を Celebration のハンコとして押し（白い光・放射光・紙吹雪）、ハンコが引くのと入れ替わりにポップの紋章が弾んで出る。
    /// 祝いを別にもう 1 つ重ねるのではなく、ポップの紋章の登場をそれに置き換える（紋章が二重に出ない）。
    /// 動きは UIMotion を通すので、省略モードでは最終状態（紋章・星が出た状態）に即なる。
    /// </summary>
    private void PlayRankUpDecor(int fromRank, int toRank)
    {
        if (rankUpRays != null && rankUpRays.gameObject.activeSelf)
        {
            var rt = rankUpRays.transform;
            rt.localScale = Vector3.one * 0.6f;
            UIMotion.Run(rt.DOScale(1f, 0.5f).SetEase(Ease.OutCubic), rankUpRays.gameObject);
            UIMotion.RunLoop(rt.DOLocalRotate(new Vector3(0f, 0f, -360f), 16f, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear).SetLoops(-1), rankUpRays.gameObject);
        }
        if (rankUpEmblem != null && rankUpEmblem.gameObject.activeSelf)
        {
            var et = rankUpEmblem.transform;
            et.localScale = Vector3.zero;
            float emblemDelay = 0.1f;
            if (!UIMotion.IsSkip && rankUpEmblem.sprite != null)
            {
                // ポップが開き終わって紋章の位置が決まってから押す（開く途中は拡縮で位置がずれる）
                const float openWait = 0.25f;
                var emblemRect = rankUpEmblem.rectTransform;
                var sprite = rankUpEmblem.sprite;
                rankUpCelebrationDelay = UIMotion.Delay(openWait, () =>
                {
                    rankUpCelebrationDelay = null;
                    if (emblemRect != null && emblemRect.gameObject.activeInHierarchy)
                        Celebration.Play(null, sprite, null, emblemRect);
                }, rankUpEmblem.gameObject);
                emblemDelay = openWait + 1.3f; // ハンコが縮んで消えるのと入れ替わり
            }
            UIMotion.Run(et.DOScale(1f, 0.45f).SetEase(Ease.OutBack).SetDelay(emblemDelay), rankUpEmblem.gameObject);
        }
        if (rankUpStarsAfter != null && toRank >= 0)
        {
            int from = fromRank >= 0 ? fromRank : toRank - 1;
            float delay = 0.4f;
            for (int i = from + 1; i <= toRank && i < rankUpStarsAfter.Length; i++)
            {
                var s = rankUpStarsAfter[i];
                if (s == null || i < 0) continue;
                var st = s.transform;
                st.localScale = Vector3.zero;
                UIMotion.Run(st.DOScale(1.3f, 0.4f).SetEase(Ease.OutBack).SetDelay(delay), s.gameObject);
                delay += 0.15f;
            }
        }
    }

    // ───────── 自己ベストの掲示板 ─────────

    /// <summary>自己ベストの表の1行（難易度・金額・ランク）。</summary>
    public readonly struct RecordRow
    {
        public readonly string Mode;
        public readonly string Amount;
        public readonly string Rank;

        public RecordRow(string mode, string amount, string rank)
        {
            Mode = mode;
            Amount = amount;
            Rank = rank;
        }
    }

    /// <summary>自己ベストの掲示板を開く（未配線ならメッセージ欄に出す）。</summary>
    public void ShowRecordBoard(string title, string body)
    {
        if (recordBoardPopup == null)
        {
            ShowMessage($"{title}\n{body}");
            return;
        }
        OpenPanel(recordBoardPopup); // #1 ぽよんと開く
        if (recordBoardTitleText != null) recordBoardTitleText.text = title;
        if (recordBoardBodyText != null) recordBoardBodyText.text = body;
        if (recordBoardAmountText != null) recordBoardAmountText.text = "";
        if (recordBoardRankText != null) recordBoardRankText.text = "";
        RefreshModalDim();
    }

    /// <summary>自己ベストの掲示板を3列の表（難易度・金額・ランク）で開く。列が未配線なら1行ずつ並べる。</summary>
    public void ShowRecordBoard(string title, IReadOnlyList<RecordRow> rows)
    {
        if (recordBoardPopup == null || recordBoardAmountText == null || recordBoardRankText == null)
        {
            var sb = new System.Text.StringBuilder();
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(r.Mode).Append('　').Append(r.Amount);
                    if (!string.IsNullOrEmpty(r.Rank)) sb.Append('　').Append("ランク ").Append(r.Rank);
                }
            }
            ShowRecordBoard(title, sb.ToString());
            return;
        }
        OpenPanel(recordBoardPopup); // #1 ぽよんと開く
        if (recordBoardTitleText != null) recordBoardTitleText.text = title;
        SetRecordRows(rows);
        RefreshModalDim();
    }

    /// <summary>表の3列に文字を入れる（動きなし。撮影ツールも使う）。</summary>
    private void SetRecordRows(IReadOnlyList<RecordRow> rows)
    {
        var modes = new System.Text.StringBuilder();
        var amounts = new System.Text.StringBuilder();
        var ranks = new System.Text.StringBuilder();
        if (rows != null)
        {
            foreach (var r in rows)
            {
                if (modes.Length > 0) { modes.Append('\n'); amounts.Append('\n'); ranks.Append('\n'); }
                modes.Append(r.Mode);
                amounts.Append(r.Amount);
                ranks.Append(string.IsNullOrEmpty(r.Rank) ? "-" : "ランク " + r.Rank);
            }
        }
        if (recordBoardBodyText != null) recordBoardBodyText.text = modes.ToString();
        if (recordBoardAmountText != null) recordBoardAmountText.text = amounts.ToString();
        if (recordBoardRankText != null) recordBoardRankText.text = ranks.ToString();
    }

    public void HideRecordBoard()
    {
        ClosePanel(recordBoardPopup); // #1 縮んで消える
        RefreshModalDim();
    }

    // ───────── 小窓の後ろの幕 ─────────

    private bool AnyModalOpen => IsInvestPanelOpen || IsConversionPopupOpen || IsRecordBoardOpen;

    private bool dimFadingOut; // 幕が消えていく途中（見た目はまだ出ている）

    /// <summary>
    /// 小窓が1つでも開いていれば幕を出す（animate なら薄く入る／消える）。動きは UIMotion を通す（省略モードでは即）。
    /// 消えるときは 0.15 秒（小窓の閉じ演出と同じ長さ）で薄くしてから隠す。
    /// </summary>
    private void RefreshModalDim(bool animate = true)
    {
        bool open = AnyModalOpen;
        if (rankUpRays != null && !IsConversionPopupOpen && rankUpRays.gameObject.activeSelf)
        {
            KillTweens(rankUpRays.transform);
            rankUpRays.gameObject.SetActive(false);
        }
        if (modalDim == null) return;
        bool wasShown = modalDim.gameObject.activeSelf && !dimFadingOut;
        KillTweens(modalDim);
        dimFadingOut = false;
        bool tween = animate && Application.isPlaying && !UIMotion.IsSkip;
        var c = modalDim.color;
        if (!open)
        {
            if (tween && wasShown)
            {
                dimFadingOut = true;
                UIMotion.Run(modalDim.DOFade(0f, 0.15f).OnComplete(() =>
                {
                    dimFadingOut = false;
                    if (modalDim != null && !AnyModalOpen) modalDim.gameObject.SetActive(false);
                }), modalDim.gameObject);
                return;
            }
            modalDim.gameObject.SetActive(false);
            return;
        }
        bool wasActive = modalDim.gameObject.activeSelf; // 消えていく途中なら、今の濃さから戻す
        modalDim.gameObject.SetActive(true);
        if (tween && !wasShown)
        {
            if (!wasActive) modalDim.color = new Color(c.r, c.g, c.b, 0f);
            UIMotion.Run(modalDim.DOFade(modalDimAlpha, 0.15f), modalDim.gameObject);
        }
        else
        {
            modalDim.color = new Color(c.r, c.g, c.b, modalDimAlpha);
        }
    }

    /// <summary>幕を押した: 投資パネル・掲示板は閉じる（帰還/格上げのポップはボタンで閉じる）。</summary>
    private void OnModalDimClicked()
    {
        if (IsConversionPopupOpen) return;
        if (IsInvestPanelOpen)
        {
            HideInvestPanel();
            OnPanelClosed.OnNext(Unit.Default);
            return;
        }
        if (IsRecordBoardOpen) HideRecordBoard();
    }

    // ───────── 施設へのショートカット（左端のつまみで開く帯） ─────────

    /// <summary>ショートカットの帯の1項目。</summary>
    public sealed class ShortcutEntry
    {
        /// <summary>施設ID（記録掲示板は VillagePresenter.RecordBoardId）。</summary>
        public string Id;
        public string Name;
        /// <summary>アイコンを引く施設（記録掲示板は null）。</summary>
        public VillageFacilityData Facility;
        /// <summary>「Lv3」「MAX」「未建設」など。</summary>
        public string LevelLabel;
        /// <summary>今の村資金で拡張・建設できる（金の「！」を付ける）。</summary>
        public bool CanUpgrade;
    }

    private readonly List<Button> shortcutButtons = new();
    private readonly List<string> shortcutIds = new();
    private bool shortcutOpen;
    private Tween shortcutTween;

    public bool IsShortcutOpen => shortcutOpen;

    private void SetupShortcuts()
    {
        if (shortcutButtonTemplate != null) shortcutButtonTemplate.gameObject.SetActive(false);
        if (shortcutKnob != null) shortcutKnob.onClick.AddListener(ToggleShortcuts);
        SetShortcutOpen(false, false);
    }

    private bool ShortcutInputBlocked =>
        uiLocked || ArrivalPlaying || (ShortcutBlocked != null && ShortcutBlocked());

    private void UpdateShortcuts()
    {
        if (shortcutDrawer == null) return;
        bool blocked = ShortcutInputBlocked;
        if (shortcutKnob != null && shortcutKnob.interactable == blocked) shortcutKnob.interactable = !blocked;
        if (blocked)
        {
            if (shortcutOpen) SetShortcutOpen(false, true); // 演出・会話が始まったら帯をしまう
            return;
        }
        if (AnyModalOpen || ControllerInput.IsTextInputFocused()) return;
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool pressed = (kb != null && kb.tabKey.wasPressedThisFrame)
                       || (pad != null && pad.leftShoulder.wasPressedThisFrame);
        if (pressed) ToggleShortcuts();
    }

    /// <summary>つまみ（Tab / パッドの LB）で帯を開け閉めする。演出・会話中と小窓を開いている間は無視。</summary>
    public void ToggleShortcuts()
    {
        if (shortcutDrawer == null || ShortcutInputBlocked || AnyModalOpen) return;
        SetShortcutOpen(!shortcutOpen, true);
        if (!ControllerInput.IsNavigating.CurrentValue || fieldScope == null)
        {
            // マウスで押したつまみが選ばれたままだと、Space/Enter（区画の「調べる」）でも開け閉めしてしまうので外す
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && shortcutKnob != null && es.currentSelectedGameObject == shortcutKnob.gameObject)
                es.SetSelectedGameObject(null);
            return;
        }
        // パッド/キーで開いたら最初のボタンを選ぶ（閉じたら歩く操作へ戻す）
        if (shortcutOpen)
        {
            var first = shortcutButtons.Find(b => b != null && b.gameObject.activeSelf);
            if (first != null)
            {
                hudMode = true;
                fieldScope.Select(first);
            }
        }
        else if (hudMode)
        {
            ExitHudMode();
        }
    }

    /// <summary>帯を開く/閉じる（animate=false ならすぐその位置へ。撮影ツールも使う）。連打しても今の位置から向きを変えるだけ。</summary>
    public void SetShortcutOpen(bool open, bool animate)
    {
        shortcutOpen = open;
        if (shortcutDrawer == null) return;
        float width = shortcutDrawer.rect.width > 0f ? shortcutDrawer.rect.width : shortcutDrawer.sizeDelta.x;
        float target = open ? 0f : -width;
        shortcutTween?.Kill();
        shortcutTween = null;
        if (shortcutKnobArrow != null)
        {
            var s = shortcutKnobArrow.localScale;
            shortcutKnobArrow.localScale = new Vector3(open ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
        }
        if (open) SetShortcutListInteractable(true);
        if (!animate)
        {
            shortcutDrawer.anchoredPosition = new Vector2(target, shortcutDrawer.anchoredPosition.y);
            if (!open) SetShortcutListInteractable(false);
            return;
        }
        // UIMotion を通す（省略モードでは即その位置へ。OnComplete も同期で呼ばれる）
        var tween = shortcutDrawer.DOAnchorPosX(target, shortcutSlideDuration)
            .SetEase(open ? Ease.OutCubic : Ease.InCubic)
            .OnComplete(() =>
            {
                shortcutTween = null;
                if (!shortcutOpen) SetShortcutListInteractable(false);
            });
        UIMotion.Run(tween, shortcutDrawer.gameObject);
        shortcutTween = tween.IsActive() ? tween : null;
        if (open) StaggerShortcutButtons();
    }

    /// <summary>#3 帯を開いたとき、施設のボタンを上から順に出す（alpha と位置だけ。押せる状態は変えない）。</summary>
    private void StaggerShortcutButtons()
    {
        var rows = new List<RectTransform>(shortcutButtons.Count);
        foreach (var b in shortcutButtons)
            if (b != null && b.gameObject.activeSelf) rows.Add((RectTransform)b.transform);
        UIMotion.Stagger(rows, step: 0.04f, distance: 24f, duration: 0.22f, delay: 0.05f);
    }

    private void SetShortcutListInteractable(bool on)
    {
        if (shortcutListGroup == null) return;
        shortcutListGroup.interactable = on;
        shortcutListGroup.blocksRaycasts = on;
    }

    /// <summary>帯のボタンを並べ直す（村資金・Lv が変わるたびに Presenter が呼ぶ）。足りない分は雛形から作る。</summary>
    public void SetShortcuts(IReadOnlyList<ShortcutEntry> entries)
    {
        if (shortcutButtonTemplate == null) return;
        int count = entries?.Count ?? 0;
        var parent = shortcutButtonTemplate.transform.parent;
        while (shortcutButtons.Count < count)
        {
            var b = Instantiate(shortcutButtonTemplate, parent);
            int index = shortcutButtons.Count;
            b.name = $"Shortcut{index}";
            b.onClick.AddListener(() => OnShortcutButton(index));
            shortcutButtons.Add(b);
        }
        shortcutIds.Clear();
        for (int i = 0; i < shortcutButtons.Count; i++)
        {
            var b = shortcutButtons[i];
            bool on = i < count;
            b.gameObject.SetActive(on);
            if (!on) continue;
            var e = entries[i];
            shortcutIds.Add(e.Id);
            var icon = FindChild<Image>(b.transform, "Icon");
            if (icon != null)
            {
                var sprite = e.Facility != null ? IconOf(e.Facility) : recordBoardIcon;
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.enabled = sprite != null;
            }
            var nameText = FindChild<TMP_Text>(b.transform, "Name");
            if (nameText != null) nameText.text = e.Name;
            var level = FindChild<TMP_Text>(b.transform, "Level");
            if (level != null)
            {
                level.text = e.LevelLabel ?? "";
                level.gameObject.SetActive(!string.IsNullOrEmpty(e.LevelLabel));
            }
            var badge = b.transform.Find("Badge");
            if (badge != null)
            {
                badge.gameObject.SetActive(e.CanUpgrade);
                // #8 拡張できる施設の「！」をぷるっと揺らす（表示中だけ動く。撮影ツールの編集モードでは付けない）
                if (e.CanUpgrade && Application.isPlaying) VillageBadgeWobble.Ensure(badge);
            }
        }
    }

    private void OnShortcutButton(int index)
    {
        if (ShortcutInputBlocked || index < 0 || index >= shortcutIds.Count) return;
        OnShortcutFacility.OnNext(shortcutIds[index]);
    }

    private static T FindChild<T>(Transform t, string name) where T : Component
    {
        var c = t.Find(name);
        return c != null ? c.GetComponent<T>() : null;
    }

    /// <summary>掲示板が開いているか（閉じ演出中は閉じたものとみなす）。</summary>
    public bool IsRecordBoardOpen => IsShown(recordBoardPopup);

    // ───────── #1 小窓の開閉（UIMotion。撮影ツールの編集モードでは演出なしで出し入れする） ─────────

    private static void OpenPanel(GameObject panel)
    {
        if (panel == null) return;
        if (Application.isPlaying) UIMotion.Open(panel);
        else panel.SetActive(true);
    }

    /// <summary>縮んで消える（0.15 秒）。onDone は消えたあと（省略モード・非表示中は即）。</summary>
    private static void ClosePanel(GameObject panel, System.Action onDone = null)
    {
        if (panel == null)
        {
            onDone?.Invoke();
            return;
        }
        if (Application.isPlaying)
        {
            UIMotion.Close(panel, onDone);
            return;
        }
        panel.SetActive(false);
        onDone?.Invoke();
    }

    private static bool IsShown(GameObject panel) => panel != null && panel.activeSelf && !UIMotion.IsClosing(panel);

    // ───────── #2 村資金の数字・総合Lv ─────────

    private int? lastVillageLevel;

    /// <summary>
    /// 村資金が変わったら（投資で減った・デバッグで増えた）数字を回して財布を弾ませ、差額を浮かせる。
    /// 帰還の到着演出（VillageArrivalFx）の最中は、演出が SetFundsDisplay で数えるので触らない（二重に数えない）。
    /// 初回・編集モード（撮影ツール）・同じ額は即表示。
    /// </summary>
    private void AnimateFunds(int villageFunds)
    {
        int? prev = lastFunds;
        bool animate = Application.isPlaying && villageFundsText != null && prev.HasValue && prev.Value != villageFunds
                       && !ArrivalPlaying;
        if (!animate)
        {
            SetFundsDisplay(villageFunds);
            return;
        }
        lastFunds = villageFunds;
        int delta = villageFunds - prev.Value;
        UIMotion.CountTo(villageFundsText, prev.Value, villageFunds, v => FundsLabel(v), 0.6f);
        UIMotion.Bump(villageFundsText.transform);
        MoneyFx.FloatText(villageFundsText.rectTransform,
            delta > 0 ? $"+{delta:N0}G" : $"-{-delta:N0}G",
            delta > 0 ? UIMotionFx.Gold2 : UIMotionFx.Red, 36f);
    }

    /// <summary>総合Lvが変わったら、格と総合Lvの看板の文字を弾ませる（初回・編集モードは何もしない）。</summary>
    private void BumpLevelIfChanged(int villageLevel)
    {
        int? prev = lastVillageLevel;
        lastVillageLevel = villageLevel;
        if (!Application.isPlaying || !prev.HasValue || prev.Value == villageLevel) return;
        var target = rankText != null ? rankText : villageLevelText;
        if (target != null) UIMotion.Bump(target.transform, 1.15f, 0.28f);
    }

    /// <summary>村資金HUDの数値だけ更新する（到着演出のカウントアップ用）。</summary>
    public void SetFundsDisplay(int villageFunds)
    {
        lastFunds = villageFunds;
        // 見出しを小さく上に、額を下に（7桁でも文字を縮めない幅に箱を合わせてある）
        if (villageFundsText != null) villageFundsText.text = FundsLabel(villageFunds);
    }

    private static string FundsLabel(long villageFunds) => $"<line-height=72%><size=52%>村資金</size>\n</line-height>{villageFunds:N0}G";

    private int? lastFunds; // 投資パネルで費用と比べる（UpdateHud・到着演出のカウントアップで更新）

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
        if (messageBand != null) messageBand.SetActive(!string.IsNullOrEmpty(message));
    }

    /// <summary>
    /// 投資パネルを表示する。nextIsSpecial=true なら「次Lv」の欄を Lv5 の特殊な効果として金の枠で出す
    /// （Lv4 のときは次に付く効果、Lv5 のときは今付いている効果）。
    /// </summary>
    public void ShowInvestPanel(VillageFacilityData facility, int level, string currentEffect, string nextEffect,
        int cost, bool canInvest, string blockedReason, bool nextIsSpecial = false)
    {
        if (investPanel == null) return;
        // 閉じている（または閉じ演出中）なら #1 ぽよんと開く。開いたままの更新（投資の直後）は開き直さない
        bool reopened = IsInvestPanelOpen;
        if (reopened) investPanel.SetActive(true);
        else OpenPanel(investPanel);
        // 開いたまま同じ施設の Lv が上がった（投資した）ら、Lv の行を弾ませる
        bool leveledUp = reopened && Application.isPlaying && facility != null && facility.facilityId == shownFacilityId && level > shownLevel;
        shownFacilityId = facility != null ? facility.facilityId : null;
        shownLevel = level;

        if (detailIcon != null)
        {
            var icon = IconOf(facility);
            detailIcon.sprite = icon;
            detailIcon.preserveAspect = true;
            detailIcon.enabled = icon != null;
        }
        if (detailNameText != null) detailNameText.text = facility.facilityName;
        if (detailLevelText != null)
        {
            detailLevelText.text = level >= facility.MaxLevel
                ? $"Lv{level}（MAX）"
                : (level == 0 ? "未建設" : $"Lv{level} → Lv{level + 1}");
        }
        if (currentEffectText != null)
            currentEffectText.text = string.IsNullOrEmpty(currentEffect) ? "-" : KeepPhrases(currentEffect);
        if (nextEffectText != null)
            nextEffectText.text = string.IsNullOrEmpty(nextEffect) ? "これ以上の拡張はない" : KeepPhrases(nextEffect);
        if (nextCaptionText != null)
        {
            // 「特殊な効果」も「現在」「次Lv」と同じ大きさ・色（強調は左の金の帯 specialFrame）
            nextCaptionText.text = nextIsSpecial ? SpecialCaption : defaultNextCaption;
            nextCaptionText.color = defaultNextCaptionColor;
            nextCaptionText.fontSizeMax = defaultNextCaptionMaxSize;
            nextCaptionText.horizontalAlignment = defaultNextCaptionAlign;
        }
        if (specialFrame != null) specialFrame.SetActive(nextIsSpecial);

        // 未建設なら「現在」の行は隠し、「次Lv」の行をその位置へ上げる
        if (currentEffectRow != null && nextEffectRow != null)
        {
            currentRowHome ??= currentEffectRow.anchoredPosition;
            nextRowHome ??= nextEffectRow.anchoredPosition;
            bool unbuilt = level <= 0;
            currentEffectRow.gameObject.SetActive(!unbuilt);
            nextEffectRow.anchoredPosition = unbuilt ? currentRowHome.Value : nextRowHome.Value;
        }

        // 村資金が分かっていれば費用と比べる（足りないときは赤、足りるときは緑。ボタンも押せない見た目に）
        bool isMax = cost < 0;
        bool shortOfFunds = cost >= 0 && lastFunds.HasValue && lastFunds.Value < cost;
        if (shortOfFunds) canInvest = false;
        if (isMax) canInvest = false;
        if (detailCostText != null)
        {
            detailCostText.text = isMax ? "-" : $"{cost:N0}G";
            if (!isMax) detailCostText.color = shortOfFunds ? costShortColor : costAffordableColor;
            detailCostText.gameObject.SetActive(!isMax); // 最大レベルは「費用 -」の行ごと隠す
        }
        if (costCaptionText != null) costCaptionText.gameObject.SetActive(!isMax);
        if (detailFundsText != null)
        {
            detailFundsText.text = lastFunds.HasValue ? $"村資金 {lastFunds.Value:N0}G" : "";
            detailFundsText.gameObject.SetActive(lastFunds.HasValue && !isMax);
        }

        // 最大レベルは押せるボタンに見えないよう、ボタンの代わりに「MAX」の札を出す
        bool showPlate = isMax && maxLevelPlate != null;
        if (maxLevelPlate != null) maxLevelPlate.SetActive(showPlate);
        if (investButton != null)
        {
            investButton.gameObject.SetActive(!showPlate);
            investButton.interactable = canInvest;
        }
        if (investButtonLabel != null)
        {
            investLabelColor ??= investButtonLabel.color;
            investButtonLabel.color = canInvest ? investLabelColor.Value : investLabelDisabledColor;
            if (!canInvest && string.IsNullOrEmpty(blockedReason) && shortOfFunds) blockedReason = "村資金が足りない";
            investButtonLabel.text = isMax
                ? "最大レベル"
                : canInvest ? (level == 0 ? "建設する" : "拡張する") : blockedReason;
        }
        RefreshModalDim();
        if (leveledUp && detailLevelText != null) UIMotion.Bump(detailLevelText.transform, 1.18f, 0.3f);
    }

    private string shownFacilityId;
    private int shownLevel;

    private Vector2? currentRowHome, nextRowHome;

    /// <summary>
    /// 効果文を言葉の切れ目（「・」「、」「：」の後、助詞の後）でだけ折り返すようにする。
    /// 切れ目ごとに &lt;nobr&gt; で包み、間に幅のない改行候補（U+200B）を入れる（「+5 / 種」「危ないぶ / ん」のような折り返しを防ぐ）。
    /// </summary>
    public static string KeepPhrases(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') >= 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + 32);
        var chunk = new System.Text.StringBuilder();
        void Flush()
        {
            if (chunk.Length == 0) return;
            if (sb.Length > 0) sb.Append('\u200B');
            sb.Append("<nobr>").Append(chunk).Append("</nobr>");
            chunk.Clear();
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '（' || c == '(') Flush(); // 括弧の前で切れてよい
            chunk.Append(c);
            bool breakAfter = c is '・' or '、' or '。' or '：' or ':' or ' ' or '\u3000' or '）' or ')' or '→'
                              || (IsParticle(c) && chunk.Length >= 3 && next != '\0' && !IsHiragana(next) && !IsClosing(next));
            if (breakAfter) Flush();
        }
        Flush();
        return sb.ToString();
    }

    private static bool IsParticle(char c) => c is 'の' or 'に' or 'が' or 'を' or 'は' or 'で' or 'と' or 'へ' or 'も';
    private static bool IsHiragana(char c) => c >= '\u3041' && c <= '\u309F';
    private static bool IsClosing(char c) => c is '、' or '。' or '・' or '）' or ')' or '：' or ':' or '」' or '』';

    private Color? investLabelColor; // 押せるときの文字の色（最初に開いたときに覚える）

    private Sprite IconOf(VillageFacilityData facility)
    {
        if (facility == null) return null;
        if (facility.icon != null) return facility.icon;
        if (facilityIcons == null) return null;
        foreach (var f in facilityIcons)
        {
            if (f.facilityId == facility.facilityId) return f.icon;
        }
        return null;
    }

    public void HideInvestPanel()
    {
        ClosePanel(investPanel); // #1 縮んで消える（閉じているときは何もしない）
        RefreshModalDim();
    }

    /// <summary>投資パネルが開いているか（閉じ演出中は閉じたものとみなす。AutoPlay もこれを見る）。</summary>
    public bool IsInvestPanelOpen => IsShown(investPanel);

    /// <summary>帰還収支ポップを表示する（村資金のカウントアップ演出付き）。</summary>
    public void ShowConversionPopup(bool cleared, int earned, int converted, VillageFundsBreakdown breakdown = null)
    {
        if (conversionPopup == null) return;
        OpenPanel(conversionPopup); // #1 ぽよんと開く
        SwapConversionRows(false); // 格上げで入れ替えた行を戻す
        ApplyRankUpDecor(false, -1, -1);
        SetConversionBreakdown(breakdown);
        RefreshModalDim();
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
            // UIMotion を通す（省略モードでは即「村へ +○G」）
            UIMotion.Run(DOTween.To(() => shown, x =>
                {
                    shown = x;
                    conversionConvertedText.text = $"村へ +{x:N0}G";
                }, converted, 0.8f)
                .SetEase(Ease.OutCubic), conversionConvertedText.gameObject);
        }
    }

    private TextMeshProUGUI conversionBreakdownText;

    /// <summary>
    /// 帰還収支ポップの下に、村へ入った額の内訳を出す（null なら隠す）。
    /// ポップの枠（720x380）には空きが無いので、ポップの子として枠のすぐ下に出す（実行時に作る。背景の帯は付けない）。
    /// </summary>
    private void SetConversionBreakdown(VillageFundsBreakdown breakdown)
    {
        var lines = breakdown?.BuildLines();
        var popupRect = conversionPopup != null ? (RectTransform)conversionPopup.transform : null;
        if (conversionBreakdownArea != null && popupRect != null) conversionPopupBaseHeight ??= popupRect.sizeDelta.y;
        if (lines == null || lines.Count == 0)
        {
            if (conversionBreakdownText != null) conversionBreakdownText.transform.parent.gameObject.SetActive(false);
            if (conversionBreakdownArea != null)
            {
                conversionBreakdownArea.gameObject.SetActive(false);
                if (popupRect != null && conversionPopupBaseHeight.HasValue)
                    popupRect.sizeDelta = new Vector2(popupRect.sizeDelta.x, conversionPopupBaseHeight.Value);
            }
            return;
        }

        if (conversionBreakdownText == null)
        {
            var popupRt = (RectTransform)conversionPopup.transform;
            var bg = new GameObject("Breakdown", typeof(RectTransform), typeof(Image));
            bg.layer = conversionPopup.layer;
            var bgRt = (RectTransform)bg.transform;
            if (conversionBreakdownArea != null)
            {
                // ポップの中の枠いっぱいに並べる
                bgRt.SetParent(conversionBreakdownArea, false);
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            }
            else
            {
                bgRt.SetParent(popupRt, false);
                bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0f);
                bgRt.pivot = new Vector2(0.5f, 1f);
                bgRt.anchoredPosition = new Vector2(0f, -8f);
            }
            var img = bg.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);   // 背景の帯は出さない（並べる場所としてだけ使う）
            img.raycastTarget = false;

            var src = conversionEarnedText != null ? conversionEarnedText : conversionTitleText;
            conversionBreakdownText = CreateBreakdownColumn(bgRt, src, TextAlignmentOptions.Left, new Color(1f, 0.95f, 0.84f, 1f));
            conversionBreakdownAmounts = CreateBreakdownColumn(bgRt, src, TextAlignmentOptions.Right, new Color(0.91f, 0.64f, 0.24f, 1f));
        }

        if (conversionBreakdownArea != null) conversionBreakdownArea.gameObject.SetActive(true);
        var labels = new System.Text.StringBuilder();
        var amounts = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (labels.Length > 0) { labels.Append('\n'); amounts.Append('\n'); }
            labels.Append(line.Label);
            amounts.Append(line.Amount);
        }
        conversionBreakdownText.text = labels.ToString();
        conversionBreakdownAmounts.text = amounts.ToString();
        var panel = (RectTransform)conversionBreakdownText.transform.parent;
        panel.gameObject.SetActive(true);
        conversionBreakdownText.ForceMeshUpdate();
        float textHeight = Mathf.Max(conversionBreakdownText.preferredHeight, lines.Count * 40f);
        if (conversionBreakdownArea != null)
        {
            // 枠を内訳の高さにし、そのぶんポップを伸ばす（下寄せの枠とボタンの上に入る）
            conversionBreakdownArea.gameObject.SetActive(true);
            float areaHeight = 24f + textHeight;
            conversionBreakdownArea.sizeDelta = new Vector2(conversionBreakdownArea.sizeDelta.x, areaHeight);
            if (popupRect != null && conversionPopupBaseHeight.HasValue)
                popupRect.sizeDelta = new Vector2(popupRect.sizeDelta.x, conversionPopupBaseHeight.Value + areaHeight + ConversionBreakdownGap);
            return;
        }
        panel.sizeDelta = new Vector2(720f, 24f + textHeight);
        panel.gameObject.SetActive(true);
    }

    private const float ConversionBreakdownGap = 16f;
    private float? conversionPopupBaseHeight; // 内訳なしのポップの高さ（最初に覚える）

    private TextMeshProUGUI conversionBreakdownAmounts;

    private static TextMeshProUGUI CreateBreakdownColumn(RectTransform parent, TMP_Text fontSource,
        TextAlignmentOptions alignment, Color color)
    {
        var go = new GameObject(alignment == TextAlignmentOptions.Right ? "Amounts" : "Labels", typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var trt = (RectTransform)go.transform;
        trt.SetParent(parent, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(28f, 10f);
        trt.offsetMax = new Vector2(-28f, -10f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (fontSource != null) tmp.font = fontSource.font;
        tmp.fontSize = 30f;
        tmp.lineSpacing = 0f;
        tmp.color = color;
        tmp.alignment = alignment == TextAlignmentOptions.Right ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }
}
