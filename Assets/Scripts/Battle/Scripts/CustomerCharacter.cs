using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 商品を購入しに来る客キャラクター。
/// SPUM_Prefabs でアニメーションを制御する。
/// 左右どちらかのスポーンから登場 → 売り場前で停止 → 購入演出 → 退場して非表示に戻る。
/// CustomerManager が Play() を呼ぶ。
/// </summary>
public class CustomerCharacter : MonoBehaviour
{
    [Header("SPUMキャラクター")]
    [Tooltip("SPUM_Prefabs コンポーネントがアタッチされた GameObject")]
    [SerializeField] private SPUM_Prefabs spumCharacter;

    [Header("アイテム表示")]
    [Tooltip("購入アイテムアイコンを表示する SpriteRenderer（itemDisplayRoot の子）")]
    [SerializeField] private SpriteRenderer itemIconRenderer;
    [Tooltip("アイテム表示全体の親オブジェクト（頭上に配置）")]
    [SerializeField] private GameObject itemDisplayRoot;

    [Header("移動設定")]
    [Tooltip("歩行速度（ワールド単位/秒）")]
    [SerializeField] private float walkSpeed = 3f;
    [Tooltip("歩行中の上下ボブ量（ワールド単位）")]
    [SerializeField] private float bobAmplitude = 0.04f;
    [Tooltip("ボブの周波数（Hz）")]
    [SerializeField] private float bobFrequency = 3f;

    [Header("購入停留設定")]
    [Tooltip("売り場前で停留する秒数")]
    [SerializeField] private float stopDuration = 1.5f;

    private Vector3 _spumStartScale;
    private CancellationTokenSource _cts;

    private void Awake()
    {
        if (spumCharacter != null)
        {
            _spumStartScale = spumCharacter.transform.localScale;
            spumCharacter.OverrideControllerInit();
        }
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (itemDisplayRoot != null) itemDisplayRoot.transform.DOKill();
    }

    public void Play(Sprite itemSprite, Vector3 spawnPos, Vector3 destPos)
    {
        if (itemDisplayRoot != null) itemDisplayRoot.SetActive(false);

        transform.position = spawnPos;
        FaceToward(destPos);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        RunAsync(itemSprite, spawnPos, destPos, _cts.Token).Forget();
    }

    private async UniTaskVoid RunAsync(Sprite itemSprite,
                                       Vector3 spawnPos, Vector3 destPos,
                                       CancellationToken token)
    {
        try
        {
            await MoveToAsync(destPos, token);

            PlayState(PlayerState.IDLE);
            ShowItem(itemSprite);
            await UniTask.Delay(TimeSpan.FromSeconds(stopDuration), cancellationToken: token);

            HideItem();
            FaceToward(spawnPos);
            await MoveToAsync(spawnPos, token);
        }
        catch (OperationCanceledException) { }

        // アイドルに戻して非表示（プール待機状態）
        PlayState(PlayerState.IDLE);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 指定座標まで歩く（MOVE アニメーション＋上下ボブ）。
    /// </summary>
    private async UniTask MoveToAsync(Vector3 target, CancellationToken token)
    {
        float dist = Vector3.Distance(transform.position, target);
        if (dist < 0.01f) return;

        PlayState(PlayerState.MOVE);

        float duration = dist / Mathf.Max(0.1f, walkSpeed);
        float elapsed  = 0f;
        Vector3 start  = transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t   = Mathf.Clamp01(elapsed / duration);
            Vector3 pos = Vector3.Lerp(start, target, t);
            float bobWeight = 1f - Mathf.Pow(t, 3f);
            pos.y += Mathf.Sin(elapsed * bobFrequency * Mathf.PI * 2f) * bobAmplitude * bobWeight;
            transform.position = pos;
            await UniTask.Yield(cancellationToken: token);
        }

        transform.position = target;
    }

    private void ShowItem(Sprite itemSprite)
    {
        if (itemIconRenderer != null) itemIconRenderer.sprite = itemSprite;
        if (itemDisplayRoot  == null) return;

        itemDisplayRoot.SetActive(true);
        itemDisplayRoot.transform.localScale = Vector3.zero;
        itemDisplayRoot.transform.DOKill();
        itemDisplayRoot.transform
            .DOScale(1f, 0.3f)
            .SetEase(Ease.OutBack);
    }

    private void HideItem()
    {
        if (itemDisplayRoot == null) return;
        itemDisplayRoot.transform.DOKill();
        itemDisplayRoot.transform
            .DOScale(0f, 0.15f)
            .SetEase(Ease.InBack)
            .OnComplete(() =>
            {
                if (itemDisplayRoot != null) itemDisplayRoot.SetActive(false);
            });
    }

    private void PlayState(PlayerState state)
    {
        if (spumCharacter == null)
        {
            Debug.LogWarning($"[CustomerCharacter] spumCharacter が未設定です: {gameObject.name}", this);
            return;
        }
        if (!spumCharacter.allListsHaveItemsExist())
        {
            Debug.LogWarning($"[CustomerCharacter] SPUM アニメーションリストが空です。SPUM_Prefabs の PopulateAnimationLists を実行してください: {gameObject.name}", this);
            return;
        }
        spumCharacter.PlayAnimation(state, 0);
    }

    /// <summary>
    /// ターゲット方向に SPUM キャラクターを向ける。
    /// PlayerMove と同様に localScale.x の符号で左右反転する。
    /// </summary>
    private void FaceToward(Vector3 target)
    {
        if (spumCharacter == null) return;
        float dx = target.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.01f) return;

        Vector3 s = _spumStartScale;
        // 右向き → X を負に、左向き → X を正に（PlayerMove と同じ規則）
        spumCharacter.transform.localScale = dx > 0
            ? new Vector3(-Mathf.Abs(s.x), s.y, s.z)
            : new Vector3( Mathf.Abs(s.x), s.y, s.z);
    }
}
