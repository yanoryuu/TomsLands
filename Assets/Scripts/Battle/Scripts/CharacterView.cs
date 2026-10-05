using UnityEngine;
using R3;
using Cysharp.Threading.Tasks;
using System.Threading;
using DG.Tweening;

public class CharacterView : MonoBehaviour
{
    [Header("コンポーネント参照")]
    [SerializeField] private SpriteRenderer characterSpriteRenderer;
    [SerializeField] private CharacterStatusView statusView; // ★ CharacterStatusViewへの参照を持つ

    // このViewがクリックされたことをPresenterに通知するための仕組み
    public Subject<Unit> OnClicked { get; } = new Subject<Unit>();

    /// <summary>
    /// Presenterからの指示で初期設定を行う
    /// </summary>
    public void Initialize(CharacterPresenter presenter, string characterName, Sprite sprite)
    {
        gameObject.name = characterName;
        if (sprite != null)
        {
            characterSpriteRenderer.sprite = sprite;
        }

        // ★ CharacterStatusViewにも初期化を指示
        //    Presenterを渡して、UIのイベント購読などを設定させる
        statusView.Initialize(presenter);
    }

    /// <summary>
    /// Presenterからの指示でダメージエフェクトの再生を命令する
    /// </summary>
    public void PlayDamageEffect(int damageAmount)
    {
        // ★ CharacterStatusViewが持つエフェクト再生処理を呼び出す
        statusView.PlayDamageEffects(damageAmount);
    }

    /// <summary>
    /// 死亡時の点滅演出を再生し、最後に非表示にする。
    /// </summary>
    public async UniTask PlayDeathEffectAsync(CancellationToken token, int blinkCount = 5, float blinkInterval = 0.15f)
    {
        if (characterSpriteRenderer == null) return;

        blinkCount = Mathf.Max(0, blinkCount);
        blinkInterval = Mathf.Max(0f, blinkInterval);

        for (int i = 0; i < blinkCount; i++)
        {
            characterSpriteRenderer.enabled = false;
            await UniTask.Delay(System.TimeSpan.FromSeconds(blinkInterval), cancellationToken: token);
            characterSpriteRenderer.enabled = true;
            await UniTask.Delay(System.TimeSpan.FromSeconds(blinkInterval), cancellationToken: token);
        }

        // 最後に非表示
        gameObject.SetActive(false);
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

        DOTween.Sequence()
            .Append(transform.DOMove(peak, forward).SetEase(Ease.OutQuad))
            .Append(transform.DOMove(origin, back).SetEase(Ease.InOutQuad))
            .SetTarget(transform)
            .SetLink(gameObject);

        await UniTask.Delay(System.TimeSpan.FromSeconds(forward), cancellationToken: token);
    }

    // マウスクリックを検知してPresenterに通知する
    private void OnMouseDown()
    {
        OnClicked.OnNext(Unit.Default);
    }
}
