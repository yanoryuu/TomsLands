using System.Collections.Generic;
using DG.Tweening;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 裏表フリップの介入カード（Docs/Streaming_Redesign.md §4-3）。
/// 表=チャット欄＋勇者側3ボタン / 裏=前列魔物＋ダンジョン側4ボタン。
/// 右上タブのタップ、またはカード面の横スワイプでめくる（めくり中は入力無効・戦闘は止めない）。
/// 支払い・上限・クールダウン管理はロジック側（InterventionPresenter）。ここは表示と入力の通知だけ。
/// 未配線の参照があっても例外を出さない。
/// </summary>
public class InterventionCardView : MonoBehaviour, IInterventionCardView,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("めくり")]
    [SerializeField] private RectTransform flipRoot;
    [SerializeField] private CanvasGroup flipGroup;
    [SerializeField] private GameObject heroFace;
    [SerializeField] private GameObject dungeonFace;
    [SerializeField] private float flipOutDuration = 0.14f;
    [SerializeField] private float flipInDuration = 0.18f;
    [SerializeField] private float flipPopScale = 1.04f;
    [Tooltip("スワイプと判定する横移動量（スクリーン px）")]
    [SerializeField] private float swipeThreshold = 60f;

    [Header("タブ（勇者アイコン⇔魔物アイコン）")]
    [SerializeField] private Button tabButton;
    [SerializeField] private RectTransform tabRoot;
    [SerializeField] private UnityEngine.UI.Image tabIcon;
    [Tooltip("表を見ているときに出すアイコン（＝めくった先の魔物）")]
    [SerializeField] private Sprite tabIconOnHeroFace;
    [Tooltip("裏を見ているときに出すアイコン（＝めくった先の勇者）")]
    [SerializeField] private Sprite tabIconOnDungeonFace;
    [Tooltip("裏を見ている間に表へスパチャが届いたら点く小さな丸")]
    [SerializeField] private GameObject tabNotifyDot;

    [Header("勇者側ボタン（Heal / Skill / Special）")]
    [SerializeField] private InterventionButtonWidget healButton;
    [SerializeField] private InterventionButtonWidget skillButton;
    [SerializeField] private InterventionButtonWidget specialButton;

    [Header("ダンジョン側ボタン（Trap / Curse / Reinforce / BossBuff）")]
    [SerializeField] private InterventionButtonWidget trapButton;
    [SerializeField] private InterventionButtonWidget curseButton;
    [SerializeField] private InterventionButtonWidget reinforceButton;
    [SerializeField] private InterventionButtonWidget bossBuffButton;

    [Header("チャット欄（表）")]
    [SerializeField] private RectTransform chatContent;
    [SerializeField] private SuperChatCardWidget superChatTemplate;
    [SerializeField] private int maxSuperChats = 4;

    [Header("前列の魔物（裏）")]
    [SerializeField] private RectTransform enemyRow;
    [SerializeField] private EnemyBadgeWidget enemyBadgeTemplate;

    private readonly Subject<HeroInterventionType> _onHero = new();
    private readonly Subject<DungeonInterventionType> _onDungeon = new();

    private readonly InterventionButtonState[] _heroStates = new InterventionButtonState[3];
    private readonly InterventionButtonState[] _dungeonStates = new InterventionButtonState[4];
    private readonly bool[] _heroKnown = new bool[3];
    private readonly bool[] _dungeonKnown = new bool[4];

    private readonly List<SuperChatCardWidget> _chatCards = new();
    private readonly List<EnemyBadgeWidget> _badges = new();

    private bool _pending;
    private float _cooldown;
    private bool _interactable = true;
    private bool _flipping;
    private bool _dragging;
    private Vector2 _dragStart;
    private InterventionButtonWidget _lastPressed;
    private Sequence _flipSeq;
    private Tween _tabBounce;

    public Observable<HeroInterventionType> OnHeroRequested => _onHero;
    public Observable<DungeonInterventionType> OnDungeonRequested => _onDungeon;
    public InterventionFace CurrentFace { get; private set; } = InterventionFace.Hero;

    private void Awake()
    {
        if (superChatTemplate != null) superChatTemplate.gameObject.SetActive(false);
        if (enemyBadgeTemplate != null) enemyBadgeTemplate.gameObject.SetActive(false);

        Hook(healButton, () => RequestHero(HeroInterventionType.Heal, healButton));
        Hook(skillButton, () => RequestHero(HeroInterventionType.Skill, skillButton));
        Hook(specialButton, () => RequestHero(HeroInterventionType.Special, specialButton));
        Hook(trapButton, () => RequestDungeon(DungeonInterventionType.Trap, trapButton));
        Hook(curseButton, () => RequestDungeon(DungeonInterventionType.Curse, curseButton));
        Hook(reinforceButton, () => RequestDungeon(DungeonInterventionType.Reinforce, reinforceButton));
        Hook(bossBuffButton, () => RequestDungeon(DungeonInterventionType.BossBuff, bossBuffButton));

        if (tabButton != null) tabButton.onClick.AddListener(Flip);

        ShowFaceImmediate(InterventionFace.Hero);
        if (tabNotifyDot != null) tabNotifyDot.SetActive(false);
        foreach (var w in AllButtons()) w?.SetCooldown(0f);
        RefreshButtons();
    }

    private void OnDestroy()
    {
        _onHero.Dispose();
        _onDungeon.Dispose();
    }

    private static void Hook(InterventionButtonWidget w, UnityEngine.Events.UnityAction a)
    {
        if (w != null && w.Button != null) w.Button.onClick.AddListener(a);
    }

    // ---------------------------------------------------------------- IInterventionCardView

    public void SetHeroButton(HeroInterventionType type, InterventionButtonState state)
    {
        int i = (int)type;
        if (i < 0 || i >= _heroStates.Length) return;
        _heroStates[i] = state;
        _heroKnown[i] = true;
        var w = HeroWidget(type);
        if (w != null)
        {
            w.SetPrice(state.Price);
            w.SetUsesLeft(state.UsesLeft);
        }
        RefreshButtons();
    }

    public void SetDungeonButton(DungeonInterventionType type, InterventionButtonState state)
    {
        int i = (int)type;
        if (i < 0 || i >= _dungeonStates.Length) return;
        _dungeonStates[i] = state;
        _dungeonKnown[i] = true;
        var w = DungeonWidget(type);
        if (w != null)
        {
            w.SetPrice(state.Price);
            w.SetUsesLeft(state.UsesLeft);
        }
        RefreshButtons();
    }

    public void SetPending(bool pending)
    {
        if (_pending == pending) return;
        _pending = pending;
        if (!pending) _lastPressed = null;
        RefreshButtons();
    }

    public void SetCooldown(float remaining01)
    {
        float r = Mathf.Clamp01(remaining01);
        bool changedGate = (r > 0f) != (_cooldown > 0f);
        _cooldown = r;
        foreach (var w in AllButtons()) w?.SetCooldown(r);
        if (changedGate) RefreshButtons();
    }

    public void PushSuperChat(SuperChatInfo info)
    {
        if (chatContent != null && superChatTemplate != null)
        {
            SuperChatCardWidget card;
            if (_chatCards.Count >= Mathf.Max(1, maxSuperChats))
            {
                // 一番古いもの（上端）を再利用して末尾へ
                card = _chatCards[0];
                _chatCards.RemoveAt(0);
            }
            else
            {
                card = Instantiate(superChatTemplate, chatContent);
            }
            card.gameObject.SetActive(true);
            card.transform.SetAsLastSibling();
            card.Bind(info);
            _chatCards.Add(card);
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
            if (CurrentFace == InterventionFace.Hero && isActiveAndEnabled)
                UIFx.Pop(card.transform, info.Color == SuperChatColor.Red ? 0.8f : 0.88f, 0.2f);
        }

        if (CurrentFace == InterventionFace.Dungeon) NotifyTab();
    }

    public void SetEnemies(IReadOnlyList<EnemyBadgeInfo> enemies)
    {
        if (enemyRow == null || enemyBadgeTemplate == null) return;
        int n = enemies?.Count ?? 0;
        while (_badges.Count < n)
        {
            var b = Instantiate(enemyBadgeTemplate, enemyRow);
            b.ResetState();
            _badges.Add(b);
        }
        for (int i = 0; i < _badges.Count; i++)
        {
            var b = _badges[i];
            bool on = i < n;
            if (on && !b.gameObject.activeSelf)
            {
                b.ResetState();
                b.gameObject.SetActive(true);
            }
            else if (!on && b.gameObject.activeSelf)
            {
                b.gameObject.SetActive(false);
            }
            if (on) b.Bind(enemies[i]);
        }
        FitEnemyRow(enemies, n);
    }

    /// <summary>魔物が多くて枠に収まらないときは列ごと縮める（文字が無いので縮小で十分読める）。</summary>
    private void FitEnemyRow(IReadOnlyList<EnemyBadgeInfo> enemies, int n)
    {
        var parent = enemyRow.parent as RectTransform;
        if (parent == null) return;
        float badgeW = enemyBadgeTemplate.GetComponent<RectTransform>().rect.width;
        var hlg = enemyRow.GetComponent<HorizontalLayoutGroup>();
        float spacing = hlg != null ? hlg.spacing : 0f;
        float content = n * badgeW + Mathf.Max(0, n - 1) * spacing;
        for (int i = 0; i < n; i++)
            if (enemies[i].IsBoss) content += badgeW * 0.25f;

        float avail = parent.rect.width - EnemyRowInset * 2f;
        float scale = content > avail && content > 0f ? avail / content : 1f;
        float extra = scale < 1f ? (content - avail) * 0.5f : 0f;
        enemyRow.anchorMin = Vector2.zero;
        enemyRow.anchorMax = Vector2.one;
        enemyRow.offsetMin = new Vector2(EnemyRowInset - extra, EnemyRowInset);
        enemyRow.offsetMax = new Vector2(-(EnemyRowInset - extra), -EnemyRowInset);
        enemyRow.localScale = new Vector3(scale, scale, 1f);
    }

    private const float EnemyRowInset = 8f;

    public void PlayAutoTrigger(HeroInterventionType type)
    {
        var w = HeroWidget(type);
        if (w != null) w.PlayAutoTrigger();
        if (CurrentFace == InterventionFace.Dungeon) NotifyTab();
    }

    public void SetInteractable(bool interactable)
    {
        _interactable = interactable;
        if (tabButton != null) tabButton.interactable = interactable;
        RefreshButtons();
    }

    // ---------------------------------------------------------------- めくり

    /// <summary>表⇔裏をめくる。めくり中・入力停止中は無視。</summary>
    public void Flip()
    {
        if (_flipping || !_interactable) return;
        var next = CurrentFace == InterventionFace.Hero ? InterventionFace.Dungeon : InterventionFace.Hero;
        if (flipRoot == null || !isActiveAndEnabled)
        {
            ShowFaceImmediate(next);
            return;
        }

        _flipping = true;
        SetFlipBlocking(true);
        _flipSeq?.Kill();
        flipRoot.localRotation = Quaternion.identity;
        flipRoot.localScale = Vector3.one;

        _flipSeq = DOTween.Sequence()
            .Append(flipRoot.DOLocalRotate(new Vector3(0f, 90f, 0f), flipOutDuration).SetEase(Ease.InQuad))
            .AppendCallback(() => ShowFaceImmediate(next, keepRotation: true))
            .Append(flipRoot.DOLocalRotate(Vector3.zero, flipInDuration).SetEase(Ease.OutBack))
            .Join(flipRoot.DOScale(flipPopScale, flipInDuration * 0.5f).SetEase(Ease.OutQuad))
            .Insert(flipOutDuration + flipInDuration * 0.5f,
                flipRoot.DOScale(1f, flipInDuration * 0.5f).SetEase(Ease.InOutQuad))
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnComplete(EndFlip)
            .OnKill(() =>
            {
                if (_flipping) EndFlip();
            });
    }

    /// <summary>配信開始時など。アニメなしで面を決める。</summary>
    public void ShowFaceImmediate(InterventionFace face) => ShowFaceImmediate(face, false);

    private void ShowFaceImmediate(InterventionFace face, bool keepRotation)
    {
        CurrentFace = face;
        if (heroFace != null) heroFace.SetActive(face == InterventionFace.Hero);
        if (dungeonFace != null) dungeonFace.SetActive(face == InterventionFace.Dungeon);
        if (tabIcon != null)
        {
            var s = face == InterventionFace.Hero ? tabIconOnHeroFace : tabIconOnDungeonFace;
            if (s != null) tabIcon.sprite = s;
        }
        if (face == InterventionFace.Hero && tabNotifyDot != null) tabNotifyDot.SetActive(false);
        if (!keepRotation && flipRoot != null)
        {
            flipRoot.localRotation = Quaternion.identity;
            flipRoot.localScale = Vector3.one;
        }
        RefreshButtons();
    }

    private void EndFlip()
    {
        _flipping = false;
        if (flipRoot != null)
        {
            flipRoot.localRotation = Quaternion.identity;
            flipRoot.localScale = Vector3.one;
        }
        SetFlipBlocking(false);
        RefreshButtons();
    }

    private void SetFlipBlocking(bool blocking)
    {
        if (flipGroup != null) flipGroup.interactable = !blocking;
    }

    private void NotifyTab()
    {
        if (tabNotifyDot != null && !tabNotifyDot.activeSelf)
        {
            tabNotifyDot.SetActive(true);
            UIFx.Pop(tabNotifyDot.transform, 0.4f, 0.2f);
        }
        if (tabRoot == null || !isActiveAndEnabled) return;
        if (_tabBounce != null && _tabBounce.IsActive() && _tabBounce.IsPlaying()) return;
        tabRoot.localScale = Vector3.one;
        _tabBounce = tabRoot.DOPunchScale(Vector3.one * 0.22f, 0.35f, 6, 0.6f)
            .SetUpdate(true).SetLink(gameObject);
    }

    // ---------------------------------------------------------------- スワイプ

    public void OnBeginDrag(PointerEventData e)
    {
        _dragging = !_flipping && _interactable;
        _dragStart = e.position;
    }

    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e)
    {
        if (!_dragging) return;
        _dragging = false;
        var d = e.position - _dragStart;
        if (Mathf.Abs(d.x) >= swipeThreshold && Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.2f) Flip();
    }

    // ---------------------------------------------------------------- ボタン

    private void RequestHero(HeroInterventionType type, InterventionButtonWidget w)
    {
        if (!CanPress() || CurrentFace != InterventionFace.Hero) return;
        int i = (int)type;
        if (_heroKnown[i] && !_heroStates[i].Interactable) return;
        _lastPressed = w;
        w?.PlayPressed();
        _onHero.OnNext(type);
    }

    private void RequestDungeon(DungeonInterventionType type, InterventionButtonWidget w)
    {
        if (!CanPress() || CurrentFace != InterventionFace.Dungeon) return;
        int i = (int)type;
        if (_dungeonKnown[i] && !_dungeonStates[i].Interactable) return;
        _lastPressed = w;
        w?.PlayPressed();
        _onDungeon.OnNext(type);
    }

    private bool CanPress() => _interactable && !_pending && !_flipping && _cooldown <= 0f;

    private void RefreshButtons()
    {
        bool gate = _interactable && !_pending && _cooldown <= 0f;
        for (int i = 0; i < 3; i++)
            Apply(HeroWidget((HeroInterventionType)i), !_heroKnown[i] || _heroStates[i].Interactable, gate);
        for (int i = 0; i < 4; i++)
            Apply(DungeonWidget((DungeonInterventionType)i), !_dungeonKnown[i] || _dungeonStates[i].Interactable, gate);
    }

    private void Apply(InterventionButtonWidget w, bool available, bool gate)
    {
        if (w == null) return;
        bool isPendingOne = _pending && w == _lastPressed;
        // クールダウン中は暗幕で示すので色は残す。実行待ち・使えない・配信終了はグレー。
        bool visual = available && _interactable && (!_pending || isPendingOne);
        w.SetVisualEnabled(visual);
        w.SetClickable(available && gate && !_flipping);
        w.SetPendingPulse(isPendingOne);
    }

    private InterventionButtonWidget HeroWidget(HeroInterventionType t) => t switch
    {
        HeroInterventionType.Heal => healButton,
        HeroInterventionType.Skill => skillButton,
        HeroInterventionType.Special => specialButton,
        _ => null,
    };

    private InterventionButtonWidget DungeonWidget(DungeonInterventionType t) => t switch
    {
        DungeonInterventionType.Trap => trapButton,
        DungeonInterventionType.Curse => curseButton,
        DungeonInterventionType.Reinforce => reinforceButton,
        DungeonInterventionType.BossBuff => bossBuffButton,
        _ => null,
    };

    private IEnumerable<InterventionButtonWidget> AllButtons()
    {
        yield return healButton;
        yield return skillButton;
        yield return specialButton;
        yield return trapButton;
        yield return curseButton;
        yield return reinforceButton;
        yield return bossBuffButton;
    }
}
