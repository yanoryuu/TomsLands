using UnityEngine;
using R3;
using Cysharp.Threading.Tasks;
using System.Threading;
using DG.Tweening;

public class CharacterView : MonoBehaviour
{
    [Header("コンポーネント参照")]
    [SerializeField] private SpriteRenderer characterSpriteRenderer;
    [SerializeField] private CharacterStatusView statusView;
    [Tooltip("SPUM のドットキャラで表示する場合に設定（勇者）。設定時は SPUM の子レンダラー全部を点滅・赤フラッシュさせ、待機/攻撃/被弾アニメを再生する")]
    [SerializeField] private SPUM_Prefabs spumCharacter;

    private SpriteRenderer[] bodyRenderers;
    private SpriteRenderer[] flashRenderers;
    private bool spumReady;
    private readonly CompositeDisposable subscriptions = new();

    /// <summary>キャラ本体の見た目を構成するレンダラー（死亡時の点滅で一緒に消す）。SPUM なら影も含む子レンダラー全部。</summary>
    public SpriteRenderer[] BodyRenderers
    {
        get
        {
            if (bodyRenderers == null) CacheRenderers();
            return bodyRenderers;
        }
    }

    /// <summary>被弾時に赤くするレンダラー（SPUM の影は赤くしない）。</summary>
    public SpriteRenderer[] FlashRenderers
    {
        get
        {
            if (flashRenderers == null) CacheRenderers();
            return flashRenderers;
        }
    }

    private void CacheRenderers()
    {
        if (spumCharacter != null)
        {
            bodyRenderers = spumCharacter.GetComponentsInChildren<SpriteRenderer>(true);
            flashRenderers = System.Array.FindAll(bodyRenderers, r => r.gameObject.name != "Shadow");
        }
        else
        {
            bodyRenderers = characterSpriteRenderer != null ? new[] { characterSpriteRenderer } : System.Array.Empty<SpriteRenderer>();
            flashRenderers = bodyRenderers;
        }
    }

    private void OnDestroy() => subscriptions.Dispose();

    private void PlaySpum(PlayerState state)
    {
        if (!spumReady || spumCharacter == null || !isActiveAndEnabled) return;
        spumCharacter.PlayAnimation(state, 0);
    }

    /// <summary>このキャラがクリックされた（Presenter が購読する）。</summary>
    public Subject<Unit> OnClicked { get; } = new();

    /// <summary>
    /// Presenterからの指示で初期設定を行う
    /// </summary>
    public void Initialize(CharacterPresenter presenter, string characterName, Sprite sprite)
    {
        gameObject.name = characterName;
        if (spumCharacter != null)
        {
            // SPUM 表示: 1枚絵（HeroData.heroSprite）は使わず、待機アニメを流し続ける。被弾時は被弾アニメ（終わると待機に戻る）
            spumCharacter.OverrideControllerInit();
            spumReady = true;
            PlaySpum(PlayerState.IDLE);
            subscriptions.Clear();
            presenter.CurrentHp.Pairwise()
                .Where(pair => pair.Current < pair.Previous && pair.Current > 0)
                .Subscribe(_ => PlaySpum(PlayerState.DAMAGED))
                .AddTo(subscriptions);
        }
        else if (sprite != null && characterSpriteRenderer != null)
        {
            characterSpriteRenderer.sprite = sprite;
        }

        statusView.Initialize(presenter);
    }

    /// <summary>
    /// Presenterからの指示でダメージエフェクトの再生を命令する
    /// </summary>
    public void PlayDamageEffect(int damageAmount) => statusView.PlayDamageEffects(damageAmount);

    /// <summary>
    /// 死亡時の点滅演出を再生し、最後に非表示にする。
    /// </summary>
    public async UniTask PlayDeathEffectAsync(CancellationToken token, int blinkCount = 5, float blinkInterval = 0.15f)
    {
        var renderers = BodyRenderers;
        if (renderers.Length == 0) return;

        blinkCount = Mathf.Max(0, blinkCount);
        blinkInterval = Mathf.Max(0f, blinkInterval);

        // 元から非表示のレンダラー（SPUM の未使用スロット）は点灯させない
        var initiallyEnabled = System.Array.ConvertAll(renderers, r => r != null && r.enabled);
        for (int i = 0; i < blinkCount; i++)
        {
            SetBodyVisible(renderers, initiallyEnabled, false);
            await UniTask.Delay(System.TimeSpan.FromSeconds(blinkInterval), cancellationToken: token);
            SetBodyVisible(renderers, initiallyEnabled, true);
            await UniTask.Delay(System.TimeSpan.FromSeconds(blinkInterval), cancellationToken: token);
        }

        // 最後に非表示
        gameObject.SetActive(false);
    }

    private static void SetBodyVisible(SpriteRenderer[] renderers, bool[] initiallyEnabled, bool visible)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && initiallyEnabled[i]) renderers[i].enabled = visible;
        }
    }

    /// <summary>
    /// 攻撃モーション: 標的の方向へ踏み込み、戻る。
    /// 踏み込み（全体の40%）が終わった時点で返る＝呼び出し側はここでダメージを入れる。戻り（60%）は並行で再生される。
    /// seconds &lt;= 0 または distance &lt;= 0 なら何もしない。
    /// </summary>
    public async UniTask PlayAttackMotionAsync(Vector3 targetPosition, float distance, float seconds, CancellationToken token)
    {
        if (seconds <= 0f || distance <= 0f || !isActiveAndEnabled) return;

        // 連続攻撃で位置がずれないよう、前回のモーションを完了させてから基準位置を取る
        transform.DOKill(complete: true);

        Vector3 origin = transform.position;
        Vector3 dir = targetPosition - origin;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Vector3 peak = origin + dir.normalized * distance;

        float forward = seconds * 0.4f;
        float back = seconds - forward;

        // SPUM 表示なら踏み込みと同時に攻撃アニメ（終わると自動で待機に戻る）
        PlaySpum(PlayerState.ATTACK);

        DOTween.Sequence()
            .Append(transform.DOMove(peak, forward).SetEase(Ease.OutQuad))
            .Append(transform.DOMove(origin, back).SetEase(Ease.InOutQuad))
            .SetTarget(transform)
            .SetLink(gameObject);

        await UniTask.Delay(System.TimeSpan.FromSeconds(forward), cancellationToken: token);
    }

    private void OnMouseDown() => OnClicked.OnNext(Unit.Default);
}
