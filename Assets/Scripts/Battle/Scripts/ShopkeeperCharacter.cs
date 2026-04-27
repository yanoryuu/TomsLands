using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using UnityEngine;

/// <summary>
/// バトル中の店主キャラクターを制御する。
/// SPUM_Prefabs でアニメーションを制御し、
/// 販売喜び・敵撃破興奮・ヒーロー被弾心配 の 3 種のリアクションと
/// 定期的な呼び込み吹き出しを持つ。
/// </summary>
public class ShopkeeperCharacter : MonoBehaviour
{
    // ─────────────────────────────────────────
    //  SPUM キャラクター
    // ─────────────────────────────────────────
    [Header("SPUMキャラクター")]
    [Tooltip("SPUM_Prefabs コンポーネントがアタッチされた GameObject")]
    [SerializeField] private SPUM_Prefabs spumCharacter;

    // ─────────────────────────────────────────
    //  各リアクション設定
    // ─────────────────────────────────────────
    [Header("販売リアクション（嬉しい）")]
    [SerializeField] private float happyJumpHeight   = 0.35f;
    [SerializeField] private float happyJumpDuration = 0.18f;

    [Header("撃破リアクション（興奮）")]
    [SerializeField] private float excitedJumpHeight   = 0.5f;
    [SerializeField] private float excitedJumpDuration = 0.15f;
    [Tooltip("興奮時の傾き角度（度）")]
    [SerializeField] private float excitedTiltAngle = 18f;

    [Header("心配リアクション（縮む）")]
    [SerializeField] private float worriedShrinkScale = 0.82f;
    [SerializeField] private float worriedDuration    = 0.18f;

    [Header("リアクション共通")]
    [Tooltip("リアクション後にアイドルに戻るまでの保持秒数")]
    [SerializeField] private float reactionHoldDuration = 0.75f;

    // ─────────────────────────────────────────
    //  吹き出し
    // ─────────────────────────────────────────
    [Header("吹き出し")]
    [SerializeField] private SpeechBubbleManager speechBubbleManager;

    [SerializeField] private List<string> sellingPhrases = new List<string>
        { "いらっしゃい！", "お買い得ですよ！", "品揃え豊富！", "安いよ安いよ！", "今が買い時！" };

    [SerializeField] private List<string> happyPhrases = new List<string>
        { "売れた！", "ありがとう！", "またどうぞ！", "好評です！" };

    [SerializeField] private List<string> excitedPhrases = new List<string>
        { "よっしゃ！", "やったぜ！", "最高だ！", "勇者強い！" };

    [SerializeField] private List<string> worriedPhrases = new List<string>
        { "大丈夫！？", "がんばれ！", "負けるな！" };

    [Tooltip("呼び込みフレーズを表示する間隔（秒）")]
    [SerializeField] private float phraseInterval = 10f;

    // ─────────────────────────────────────────
    //  イベント接続
    // ─────────────────────────────────────────
    [Header("イベント接続")]
    [Tooltip("StreamingSalesController への参照（販売イベント購読用）")]
    [SerializeField] private StreamingSalesController salesController;
    [Tooltip("BattleSequencer への参照（戦闘イベント購読用）")]
    [SerializeField] private BattleSequencer battleSequencer;

    // ─────────────────────────────────────────
    //  内部状態
    // ─────────────────────────────────────────
    private Vector3 _baseLocalPos;
    private CancellationTokenSource _reactionCts;
    private readonly CompositeDisposable _disposables = new CompositeDisposable();

    // ─────────────────────────────────────────
    //  Unity ライフサイクル
    // ─────────────────────────────────────────

    private void Start()
    {
        _baseLocalPos = transform.localPosition;

        if (spumCharacter != null)
            spumCharacter.OverrideControllerInit();

        PlayState(PlayerState.IDLE);
        SubscribeToEvents();

        if (sellingPhrases?.Count > 0)
            CallLoopAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnDestroy()
    {
        if (salesController != null)
            salesController.OnSaleOccurred -= OnSaleOccurred;

        _disposables.Dispose();
        _reactionCts?.Cancel();
        _reactionCts?.Dispose();
        transform.DOKill();
    }

    // ─────────────────────────────────────────
    //  イベント購読
    // ─────────────────────────────────────────

    private void SubscribeToEvents()
    {
        if (salesController != null)
            salesController.OnSaleOccurred += OnSaleOccurred;

        if (battleSequencer == null) return;

        battleSequencer.OnEnemyDefeated
            .Subscribe(_ => TriggerReaction(ReactionKind.Excited))
            .AddTo(_disposables);

        battleSequencer.OnCharacterDamaged
            .Subscribe(pair =>
            {
                if (pair.target.Type == CharacterType.Hero)
                    TriggerReaction(ReactionKind.Worried);
            })
            .AddTo(_disposables);
    }

    private void OnSaleOccurred(RuntimeItemData _) => TriggerReaction(ReactionKind.Happy);

    // ─────────────────────────────────────────
    //  リアクション制御
    // ─────────────────────────────────────────

    private void TriggerReaction(ReactionKind kind)
    {
        _reactionCts?.Cancel();
        _reactionCts?.Dispose();
        _reactionCts = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy());
        ReactionAsync(kind, _reactionCts.Token).Forget();
    }

    private async UniTaskVoid ReactionAsync(ReactionKind kind, CancellationToken token)
    {
        try
        {
            switch (kind)
            {
                case ReactionKind.Happy:   await PlayHappyAsync(token);   break;
                case ReactionKind.Excited: await PlayExcitedAsync(token); break;
                case ReactionKind.Worried: await PlayWorriedAsync(token); break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (this != null && gameObject != null)
            {
                transform.DOKill();
                transform.localPosition    = _baseLocalPos;
                transform.localScale       = Vector3.one;
                transform.localEulerAngles = Vector3.zero;
                PlayState(PlayerState.IDLE);
            }
        }
    }

    // ─────────────────────────────────────────
    //  各リアクション実装
    // ─────────────────────────────────────────

    /// <summary>販売喜び：OTHER アニメーション＋ポンっとジャンプ。</summary>
    private async UniTask PlayHappyAsync(CancellationToken token)
    {
        PlayState(PlayerState.OTHER);
        ShowPhrase(happyPhrases);

        transform.DOKill();
        transform.DOLocalMoveY(_baseLocalPos.y + happyJumpHeight, happyJumpDuration)
            .SetEase(Ease.OutQuad);
        transform.DOScale(1.18f, happyJumpDuration * 0.6f)
            .SetEase(Ease.OutBack);
        await UniTask.Delay(TimeSpan.FromSeconds(happyJumpDuration), cancellationToken: token);

        transform.DOLocalMoveY(_baseLocalPos.y, happyJumpDuration)
            .SetEase(Ease.InQuad);
        transform.DOScale(1f, happyJumpDuration)
            .SetEase(Ease.OutBounce);
        await UniTask.Delay(
            TimeSpan.FromSeconds(happyJumpDuration + reactionHoldDuration),
            cancellationToken: token);
    }

    /// <summary>興奮：ATTACK アニメーション＋高くジャンプして傾く。</summary>
    private async UniTask PlayExcitedAsync(CancellationToken token)
    {
        PlayState(PlayerState.ATTACK);
        ShowPhrase(excitedPhrases);

        transform.DOKill();
        transform.DOLocalMoveY(_baseLocalPos.y + excitedJumpHeight, excitedJumpDuration)
            .SetEase(Ease.OutQuad);
        transform.DOLocalRotate(new Vector3(0f, 0f, excitedTiltAngle), excitedJumpDuration)
            .SetEase(Ease.OutQuad);
        transform.DOScale(1.25f, excitedJumpDuration)
            .SetEase(Ease.OutBack);
        await UniTask.Delay(TimeSpan.FromSeconds(excitedJumpDuration), cancellationToken: token);

        transform.DOLocalMoveY(_baseLocalPos.y, excitedJumpDuration)
            .SetEase(Ease.InQuad);
        transform.DOLocalRotate(Vector3.zero, excitedJumpDuration)
            .SetEase(Ease.OutBounce);
        transform.DOScale(1f, excitedJumpDuration)
            .SetEase(Ease.OutBounce);
        await UniTask.Delay(
            TimeSpan.FromSeconds(excitedJumpDuration + reactionHoldDuration),
            cancellationToken: token);
    }

    /// <summary>心配：DAMAGED アニメーション＋縮んでから戻る。</summary>
    private async UniTask PlayWorriedAsync(CancellationToken token)
    {
        PlayState(PlayerState.DAMAGED);
        ShowPhrase(worriedPhrases);

        transform.DOKill();
        transform.DOScale(worriedShrinkScale, worriedDuration)
            .SetEase(Ease.OutQuad);
        transform.DOLocalMoveY(_baseLocalPos.y - 0.05f, worriedDuration)
            .SetEase(Ease.OutQuad);
        await UniTask.Delay(TimeSpan.FromSeconds(worriedDuration), cancellationToken: token);

        transform.DOScale(1f, worriedDuration * 1.5f)
            .SetEase(Ease.OutBounce);
        transform.DOLocalMoveY(_baseLocalPos.y, worriedDuration * 1.5f)
            .SetEase(Ease.OutBounce);
        await UniTask.Delay(
            TimeSpan.FromSeconds(worriedDuration * 1.5f + reactionHoldDuration),
            cancellationToken: token);
    }

    // ─────────────────────────────────────────
    //  呼び込みループ
    // ─────────────────────────────────────────

    private async UniTaskVoid CallLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(phraseInterval), cancellationToken: token);
                ShowPhrase(sellingPhrases);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // ─────────────────────────────────────────
    //  ヘルパー
    // ─────────────────────────────────────────

    private void PlayState(PlayerState state)
    {
        if (spumCharacter == null) return;
        spumCharacter.PlayAnimation(state, 0);
    }

    private void ShowPhrase(List<string> phrases)
    {
        if (speechBubbleManager == null || phrases == null || phrases.Count == 0) return;
        string phrase = phrases[UnityEngine.Random.Range(0, phrases.Count)];
        speechBubbleManager.RequestBubble(phrase, 1.5f);
    }

    private enum ReactionKind { Happy, Excited, Worried }
}
