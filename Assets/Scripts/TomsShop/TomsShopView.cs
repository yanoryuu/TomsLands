using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TomsShopView : MonoBehaviour
{
    [SerializeField] private Button BlaskSmithButton;
    [SerializeField] private Button HeroButton;
    [SerializeField] private Button SetItemButton;
    [SerializeField] private Button InfoButton;
    [SerializeField] private Button ToolButton;
    [SerializeField] private Button StartShopButton;
    [SerializeField] private Button MapButton;
    [SerializeField] private Button DungeonLevelUpButton;
    [SerializeField] private Button AdvertisementButton;
    [SerializeField] private Button ProphetButton;
    [SerializeField] private Button ShopUpgradeButton;
    [SerializeField] private Button MachineShopButton;
    /// <summary>朝刊（情報ターミナル）を開くボタン。仕入れフェーズに置く。</summary>
    [SerializeField] private Button NewspaperButton;
    [SerializeField] private TurnAnnounceView turnAnnounceView;
    [SerializeField] private BuzzAnnounceView buzzAnnounceView;

    [Header("バズ中演出")]
    [SerializeField] private BuzzModeOverlayView buzzModeOverlayView;

    [Header("机の陳列")]
    [SerializeField] private ShopDeskDisplay shopDeskDisplay;

    [Header("営業ダイジェスト（客が買っていく演出） ※未配線なら従来の暗転")]
    [SerializeField] private SalesDigestView salesDigest;

    [Header("設置マシンの表示 ※未配線でも動作する")]
    [SerializeField] private ShopMachineDisplay shopMachineDisplay;

    [Header("税金情報")]
    [SerializeField] private TextMeshProUGUI nextDebtText;
    [SerializeField] private Button debtPaymentButton;

    [Header("朝レポート ※未配線でも動作する")]
    [SerializeField] private GameObject morningReportPanel;
    [SerializeField] private TextMeshProUGUI morningReportText;
    [SerializeField] private Button morningReportCloseButton;

    [Header("レリック獲得3択 ※未配線でも動作する")]
    [SerializeField] private GameObject relicChoicePanel;
    [SerializeField] private Button[] relicChoiceButtons;
    [SerializeField] private TextMeshProUGUI[] relicChoiceNameTexts;
    [SerializeField] private TextMeshProUGUI[] relicChoiceDescTexts;
    [SerializeField] private Button relicChoiceSkipButton;
    [Tooltip("カード本体（アイコン・レア度表示・ホバー枠）。未配線なら名前/説明テキストだけ更新する")]
    [SerializeField] private RelicChoiceSlotView[] relicChoiceSlots;
    [Tooltip("開いたときにポップさせる外枠（任意）")]
    [SerializeField] private RectTransform relicChoiceWindow;

    [Header("所持レリックバー（アイコン列） ※未配線でも動作する")]
    [Tooltip("レリックアイコンの親（HorizontalLayoutGroup推奨）。クリックで説明ポップアップ")]
    [SerializeField] private Transform relicBarParent;
    [Tooltip("アイコン1個のサイズ(px)")]
    [SerializeField] private float relicIconSize = 56f;

    //鍛冶屋を開く
    public Subject<Unit> OnBlacksmithClicked { get; } = new();
    public Subject<Unit> OnHeroClicked { get; } = new();
    //商品を陳列
    public Subject<Unit> OnSetItemClicked { get; } = new();
    //情報屋を開く
    public Subject<Unit> OnInfoClicked { get; } = new();
    //道具屋を開く
    public Subject<Unit> OnToolClicked { get; } = new();
    //営業開始
    public Subject<Unit> OnStartShopClicked { get; } = new();
    //マップ画面を開く
    public Subject<Unit> OnMapClicked { get; } = new();
    //ダンジョンレベルアップ画面を開く
    public Subject<Unit> OnDungeonLevelUpClicked { get; } = new();
    //広告購入画面を開く
    public Subject<Unit> OnAdvertisementClicked { get; } = new();
    //預言者画面を開く
    public Subject<Unit> OnProphetClicked { get; } = new();

    public Subject<Unit> OnShopUpgradeClicked { get; } = new();
    //マシンショップ（店カスタマイズ）画面を開く
    public Subject<Unit> OnMachineShopClicked { get; } = new();
    /// <summary>朝刊ボタンがクリックされた</summary>
    public Subject<Unit> OnNewspaperClicked { get; } = new();
    //借金返済パネルを開く
    public Subject<Unit> OnDebtPaymentClicked { get; } = new();
    //レリック3択の選択（index）とスキップ
    public Subject<int> OnRelicChoiceSelected { get; } = new();
    public Subject<Unit> OnRelicChoiceSkipped { get; } = new();
    //レリックバーのアイコンクリック（relicId）
    public Subject<string> OnRelicIconClicked { get; } = new();

    /// <summary>
    /// レリック獲得3択を表示する。パネル未配線なら false を返す
    /// （呼び出し側が自動獲得にフォールバック）。
    /// </summary>
    public bool ShowRelicChoices(IReadOnlyList<RelicDefinition> choices, string skipLabel = null, IReadOnlyList<string> estimates = null)
    {
        if (relicChoicePanel == null || relicChoiceButtons == null || relicChoiceButtons.Length == 0)
            return false;
        choices ??= System.Array.Empty<RelicDefinition>();

        // 辞退ボタンの文言（「辞退して◯◯Gもらう」等）を差し替える
        if (!string.IsNullOrEmpty(skipLabel) && relicChoiceSkipButton != null)
        {
            var label = relicChoiceSkipButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = skipLabel;
        }

        for (int i = 0; i < relicChoiceButtons.Length; i++)
        {
            var relic = i < choices.Count ? choices[i] : null;
            bool hasChoice = relic != null;
            if (relicChoiceButtons[i] != null)
                relicChoiceButtons[i].gameObject.SetActive(hasChoice);
            if (!hasChoice) continue;

            // このランの実績からの目安（F-9）。出せない種類は空
            string estimate = estimates != null && i < estimates.Count ? estimates[i] : null;
            string description = string.IsNullOrEmpty(estimate)
                ? relic.description
                : $"{relic.description}\n<color=#B0601C>{estimate}</color>";

            var slot = relicChoiceSlots != null && i < relicChoiceSlots.Length ? relicChoiceSlots[i] : null;
            if (slot != null)
            {
                // コモンは色を乗せない（レア以上・呪いだけさりげなく色付け）
                Color? rarityColor = relic.isCurse || relic.rarity != RelicRarity.Common
                    ? RarityColor(relic.rarity, relic.isCurse)
                    : null;
                slot.Setup(relic.relicName, description, relic.icon, rarityColor);
                continue;
            }

            // カード未配線: 名前/説明テキストだけ更新する（旧仮UI互換）
            if (relicChoiceNameTexts != null && i < relicChoiceNameTexts.Length && relicChoiceNameTexts[i] != null)
                relicChoiceNameTexts[i].text = relic.relicName;
            if (relicChoiceDescTexts != null && i < relicChoiceDescTexts.Length && relicChoiceDescTexts[i] != null)
            {
                // 説明文の枠超え防止: シーン側の設定に依らず折り返し＋はみ出し分は省略にする
                var desc = relicChoiceDescTexts[i];
                desc.textWrappingMode = TextWrappingModes.Normal;
                desc.overflowMode = TextOverflowModes.Ellipsis;
                desc.text = description;
            }
        }

        relicChoicePanel.SetActive(true);
        UIFx.PanelOpen(relicChoicePanel);
        if (relicChoiceWindow != null) UIFx.Pop(relicChoiceWindow, 0.92f, 0.22f);
        return true;
    }

    public void HideRelicChoices()
    {
        if (relicChoicePanel != null)
            relicChoicePanel.SetActive(false);
    }

    /// <summary>レリック3択を開いているか（朝の通知を1枚ずつ出す待ち合わせ用。F-4）。</summary>
    /// <summary>営業ダイジェスト（未配線なら null）。</summary>
    public SalesDigestView SalesDigest => salesDigest;

    public bool IsRelicChoiceOpen => relicChoicePanel != null && relicChoicePanel.activeSelf;

    /// <summary>朝レポートを開いているか（朝の通知を1枚ずつ出す待ち合わせ用。F-4）。</summary>
    public bool IsMorningReportOpen => morningReportPanel != null && morningReportPanel.activeSelf;

    /// <summary>
    /// 所持レリックバーの該当アイコンを跳ねさせ、上に金額を浮かせる（F-8: レリックが効いた瞬間を見せる）。
    /// 未配線・アイコンが無ければ何もしない。
    /// </summary>
    public void PopRelicIcon(string relicId, string amountText, float delay)
    {
        if (relicBarParent == null || string.IsNullOrEmpty(relicId)) return;
        var icon = relicBarParent.Find($"Relic_{relicId}") as RectTransform;
        if (icon == null || !icon.gameObject.activeInHierarchy) return;

        DOVirtual.DelayedCall(delay, () =>
        {
            if (icon == null) return;
            UIFx.Pop(icon, 1.35f, 0.3f);
            if (string.IsNullOrEmpty(amountText)) return;
            var go = new GameObject("RelicGain", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(icon, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(220f, 40f);
            rt.anchoredPosition = new Vector2(0f, 18f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            var font = morningReportText != null ? morningReportText.font : null;
            if (font != null) tmp.font = font;
            tmp.text = amountText;
            tmp.fontSize = 26f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.91f, 0.64f, 0.24f, 1f);
            tmp.raycastTarget = false;
            rt.DOAnchorPosY(48f, 0.9f).SetEase(Ease.OutCubic).SetLink(go);
            tmp.DOFade(0f, 0.4f).SetDelay(0.6f).SetLink(go).OnComplete(() => Destroy(go));
        }).SetLink(icon.gameObject);
    }

    /// <summary>
    /// 所持レリックバー（アイコン列）を再構築する。未配線なら何もしない。
    /// アイコン未設定のレリックはレア度色の丸+頭文字でフォールバック表示する。
    /// クリックで OnRelicIconClicked（説明ポップアップは Presenter 側）。
    /// </summary>
    public void UpdateRelicBar(IReadOnlyList<RelicDefinition> relics)
    {
        if (relicBarParent == null) return;

        for (int i = relicBarParent.childCount - 1; i >= 0; i--)
        {
            Destroy(relicBarParent.GetChild(i).gameObject);
        }
        if (relics == null) return;

        foreach (var relic in relics)
        {
            if (relic == null) continue;
            var iconObj = new GameObject($"Relic_{relic.relicId}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = iconObj.GetComponent<RectTransform>();
            rt.SetParent(relicBarParent, false);
            rt.sizeDelta = new Vector2(relicIconSize, relicIconSize);

            var img = iconObj.GetComponent<Image>();
            if (relic.icon != null)
            {
                img.sprite = relic.icon;
                img.color = Color.white;
            }
            else
            {
                // アイコン未設定: レア度色の板 + 頭文字（本番アートで置き換わる前提のフォールバック）
                img.color = RarityColor(relic.rarity, relic.isCurse);

                var label = new GameObject("Label", typeof(RectTransform));
                var lrt = label.GetComponent<RectTransform>();
                lrt.SetParent(rt, false);
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
                var tmp = label.AddComponent<TextMeshProUGUI>();
                tmp.text = string.IsNullOrEmpty(relic.relicName) ? "?" : relic.relicName.Substring(0, 1);
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 10f; tmp.fontSizeMax = relicIconSize * 0.6f;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
            }

            string capturedId = relic.relicId;
            iconObj.GetComponent<Button>().onClick.AddListener(() => OnRelicIconClicked.OnNext(capturedId));
        }
    }

    private static Color RarityColor(RelicRarity rarity, bool isCurse)
    {
        if (isCurse) return new Color(0.45f, 0.15f, 0.35f);
        return rarity switch
        {
            RelicRarity.Epic => new Color(0.55f, 0.35f, 0.75f),
            RelicRarity.Rare => new Color(0.25f, 0.5f, 0.8f),
            _ => new Color(0.5f, 0.55f, 0.6f),
        };
    }

    /// <summary>設置済みマシンの見た目を更新する。未配線なら何もしない。</summary>
    public void RefreshMachineDisplay(ShopMachineModel machineModel)
    {
        if (shopMachineDisplay != null)
            shopMachineDisplay.RefreshDisplay(machineModel);
    }

    /// <summary>
    /// 朝レポートを表示する。パネル未配線なら false を返す（呼び出し側がログにフォールバック）。
    /// </summary>
    public bool ShowMorningReport(string text)
    {
        if (morningReportPanel == null || morningReportText == null) return false;
        morningReportText.text = text;
        morningReportPanel.SetActive(true);
        UIFx.PanelOpen(morningReportPanel); // 開く演出（瞬間表示にしない）
        return true;
    }

    public void Awake()
    {
        BlaskSmithButton.onClick.AddListener(() => OnBlacksmithClicked.OnNext(Unit.Default));
        if (HeroButton != null)
            HeroButton.onClick.AddListener(() => OnHeroClicked.OnNext(Unit.Default));
        SetItemButton.onClick.AddListener(() => OnSetItemClicked.OnNext(Unit.Default));
        InfoButton.onClick.AddListener(() => OnInfoClicked.OnNext(Unit.Default));
        // 道具屋は作らないことにした（2026-10-20）。中身の無い画面へ入って戻れなくならないよう、ボタンは常に隠して配線もしない。
        // OnToolClicked・TomsShopGamePhase.ToolShop・TomsModel.ToolShopLevel はシリアライズとセーブの互換のため残す
        if (ToolButton != null) ToolButton.gameObject.SetActive(false);
        StartShopButton.onClick.AddListener(() => OnStartShopClicked.OnNext(Unit.Default));
        MapButton.onClick.AddListener(() => OnMapClicked.OnNext(Unit.Default));
        DungeonLevelUpButton.onClick.AddListener(() => OnDungeonLevelUpClicked.OnNext(Unit.Default));
        if (AdvertisementButton != null)
            AdvertisementButton.onClick.AddListener(() => OnAdvertisementClicked.OnNext(Unit.Default));
        if (ProphetButton != null)
            ProphetButton.onClick.AddListener(() => OnProphetClicked.OnNext(Unit.Default));
        if (ShopUpgradeButton != null)
            ShopUpgradeButton.onClick.AddListener(() => OnShopUpgradeClicked.OnNext(Unit.Default));
        if (MachineShopButton != null)
            MachineShopButton.onClick.AddListener(() => OnMachineShopClicked.OnNext(Unit.Default));
        if (NewspaperButton != null)
            NewspaperButton.onClick.AddListener(() => OnNewspaperClicked.OnNext(Unit.Default));
        if (morningReportCloseButton != null && morningReportPanel != null)
            morningReportCloseButton.onClick.AddListener(() => morningReportPanel.SetActive(false));

        if (relicChoiceButtons != null)
        {
            for (int i = 0; i < relicChoiceButtons.Length; i++)
            {
                int index = i;
                if (relicChoiceButtons[i] != null)
                    relicChoiceButtons[i].onClick.AddListener(() => OnRelicChoiceSelected.OnNext(index));
            }
        }
        if (relicChoiceSkipButton != null)
            relicChoiceSkipButton.onClick.AddListener(() => OnRelicChoiceSkipped.OnNext(Unit.Default));
        if (debtPaymentButton != null)
            debtPaymentButton.onClick.AddListener(() => OnDebtPaymentClicked.OnNext(Unit.Default));

        SetupPadFocusForOverlays();
    }

    /// <summary>
    /// パッド操作: 店ホーム上に重なるパネル（朝レポート・レリック3択）をモーダルのフォーカス単位にする。
    /// パネルはシーン側で配線されるため、コードで付与する（未配線なら何もしない）。
    /// </summary>
    private void SetupPadFocusForOverlays()
    {
        if (morningReportPanel != null)
        {
            var scope = UIFocusScope.GetOrAdd(morningReportPanel);
            scope.Modal = true;
            scope.Priority = Mathf.Max(scope.Priority, 20);
            if (morningReportCloseButton != null)
            {
                scope.FirstSelected = morningReportCloseButton;
                if (!scope.HasCancel) scope.SetCancelButton(morningReportCloseButton);
            }
        }

        if (relicChoicePanel != null)
        {
            // 辞退（ゴールド受け取り）を B に割り当てると誤爆するので Cancel は無し
            var scope = UIFocusScope.GetOrAdd(relicChoicePanel);
            scope.Modal = true;
            scope.Priority = Mathf.Max(scope.Priority, 20);
            scope.RememberLastSelected = false;
            if (relicChoiceButtons != null && relicChoiceButtons.Length > 0 && relicChoiceButtons[0] != null)
                scope.FirstSelected = relicChoiceButtons[0];
        }
    }

    // ========================================
    // 会話チュートリアル：鍛冶屋だけ押せる状態
    // ========================================

    private Tween blacksmithPulseTween;
    private Vector3 blacksmithBaseScale = Vector3.one;

    /// <summary>
    /// 初回チュートリアルの「鍛冶屋以外クリック不可」を切り替える。
    /// 鍛冶屋以外の施設・陳列・営業・朝刊・返済ボタンを押せなくし、鍛冶屋ボタンを脈動させる。
    /// （「次へ」は TurnPhaseView.SetAdvanceLocked で別途止める）
    /// </summary>
    public void SetTutorialBlacksmithOnly(bool on)
    {
        bool free = !on;
        SetInteractableSafe(HeroButton, free);
        SetInteractableSafe(SetItemButton, free);
        SetInteractableSafe(InfoButton, free);
        SetInteractableSafe(ToolButton, free);
        SetInteractableSafe(MapButton, free);
        SetInteractableSafe(DungeonLevelUpButton, free);
        SetInteractableSafe(AdvertisementButton, free);
        SetInteractableSafe(ProphetButton, free);
        SetInteractableSafe(ShopUpgradeButton, free);
        SetInteractableSafe(MachineShopButton, free);
        SetInteractableSafe(NewspaperButton, free);
        SetInteractableSafe(debtPaymentButton, free);
        if (on) SetInteractableSafe(StartShopButton, false);
        SetInteractableSafe(BlaskSmithButton, true);

        if (BlaskSmithButton == null) return;
        var t = BlaskSmithButton.transform;
        if (blacksmithPulseTween != null)
        {
            // 脈動を止めて元の大きさへ戻す（脈動していないときは触らない）
            blacksmithPulseTween.Kill();
            blacksmithPulseTween = null;
            t.localScale = blacksmithBaseScale;
        }
        if (!on) return;
        blacksmithBaseScale = t.localScale;
        blacksmithPulseTween = t.DOScale(blacksmithBaseScale * 1.1f, 0.45f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(BlaskSmithButton.gameObject);
    }

    /// <summary>1周目に段階的に開ける店ホームのボタン（K-7）。</summary>
    public enum ShopFeature { Newspaper, Hero, Info, Map, Prophet, DungeonLevelUp, Advertisement, ShopUpgrade, MachineShop }

    private Button FeatureButton(ShopFeature f) => f switch
    {
        ShopFeature.Newspaper => NewspaperButton,
        ShopFeature.Hero => HeroButton,
        ShopFeature.Info => InfoButton,
        ShopFeature.Map => MapButton,
        ShopFeature.Prophet => ProphetButton,
        ShopFeature.DungeonLevelUp => DungeonLevelUpButton,
        ShopFeature.Advertisement => AdvertisementButton,
        ShopFeature.ShopUpgrade => ShopUpgradeButton,
        ShopFeature.MachineShop => MachineShopButton,
        _ => null,
    };

    /// <summary>
    /// ボタンを出す/隠す。newlyOpened のときは開いたことが分かるよう一度弾ませる。未配線なら何もしない。
    /// </summary>
    public void SetFeatureVisible(ShopFeature feature, bool visible, bool newlyOpened = false)
    {
        var button = FeatureButton(feature);
        if (button == null) return;
        if (button.gameObject.activeSelf != visible) button.gameObject.SetActive(visible);
        if (visible && newlyOpened && button.gameObject.activeInHierarchy)
        {
            var t = button.transform;
            t.DOKill(true);
            t.DOPunchScale(Vector3.one * 0.18f, 0.6f, 4, 0.6f).SetDelay(0.3f).SetLink(button.gameObject);
        }
    }

    private static void SetInteractableSafe(Selectable selectable, bool interactable)
    {
        if (selectable != null) selectable.interactable = interactable;
    }

    /// <summary>営業開始ボタンの押下可否を切り替える（演出中の連打防止用）。</summary>
    public void SetStartShopInteractable(bool interactable)
    {
        if (StartShopButton != null)
            StartShopButton.interactable = interactable;

        // パッド操作: 営業開始演出の間は自動選択を止める（A 連打が隣の「戻る」に移って営業を取り消す誤爆を防ぐ）
        var scope = GetComponentInParent<UIFocusScope>(true);
        if (scope != null) scope.AutoSelect = interactable;
    }

    public void RefreshDeskDisplay(List<RuntimeItemData> runtimeItems)
    {
        if (shopDeskDisplay != null)
            shopDeskDisplay.RefreshDisplay(runtimeItems);
    }

    /// <summary>
    /// ターン切り替え演出を再生する（左→右にスライド）
    /// </summary>
    public void ShowTurnAnnounce(int turn) => turnAnnounceView.Show(turn);

    /// <summary>
    /// 次回税金の納付額と残りターン数をショップ画面内に表示する
    /// </summary>
    public void UpdateNextDebt(int amount, int remainingTurns)
    {
        if (nextDebtText == null) return;
        nextDebtText.text = $"次回納税\n{amount:#,0}G\nあと{remainingTurns}ターン";
    }

    /// <summary>
    /// バズ発生演出を再生する。
    /// ターン開始時にバズが発生した場合に呼び出される。
    /// </summary>
    /// <param name="buzzType">発生したバズの種類</param>
    public void ShowBuzzAnnounce(BuzzType buzzType, string detail = null)
    {
        if (buzzAnnounceView != null)
            buzzAnnounceView.ShowBuzzOccurred(buzzType, detail);
    }

    /// <summary>
    /// バズ終了演出を再生する。
    /// ターン開始時にバズが終了した場合に呼び出される。
    /// </summary>
    /// <param name="endedBuzzType">終了したバズの種類</param>
    public void ShowBuzzEndedAnnounce(BuzzType endedBuzzType)
    {
        if (buzzAnnounceView != null)
            buzzAnnounceView.ShowBuzzEnded(endedBuzzType);
    }

    /// <summary>
    /// バズ中の常時演出（バズモードオーバーレイ）の表示/非表示を切り替える。
    /// バズ発生中はフレーム＋バナーを表示し続ける。
    /// </summary>
    public void SetBuzzModeActive(bool isActive, BuzzType buzzType)
    {
        if (buzzModeOverlayView == null) return;

        if (isActive)
            buzzModeOverlayView.Show(buzzType);
        else
            buzzModeOverlayView.Hide();
    }

    /// <summary>
    /// バズの残りターン数表示を更新する。
    /// </summary>
    public void UpdateBuzzRemainingTurns(int remainingTurns)
    {
        if (buzzModeOverlayView != null)
            buzzModeOverlayView.UpdateRemainingTurns(remainingTurns);
    }
}
