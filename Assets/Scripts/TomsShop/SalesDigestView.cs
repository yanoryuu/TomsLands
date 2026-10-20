using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 営業ダイジェスト（F-7）。「営業開始」のあと、店の画面のまま客が机へ来て品を買っていく様子を数秒見せる。
/// 客は配信の客と同じ CustomerCharacter（SPUM）を使い、見た目は MobAppearanceCatalog からランダムに選ぶ。
/// 精算は演出の前に済ませてあり（TurnEndSummaryPresenter.ProcessTodayIfNeeded）、ここは結果を見せるだけ。
/// クリック・キー・パッドのどれかで飛ばせる。演出設定「演出を速くする」では長さの上限が半分になる。
/// 未配線（客や立ち位置が無い）なら IsWired=false で、呼び出し側は従来の暗転に戻す。
/// </summary>
public class SalesDigestView : MonoBehaviour
{
    /// <summary>客1人ぶんの表示内容。</summary>
    public readonly struct Entry
    {
        public readonly Sprite Icon;
        public readonly string Label;
        public readonly Color Color;
        public readonly bool Highlight;
        public Entry(Sprite icon, string label, Color color, bool highlight)
        {
            Icon = icon; Label = label; Color = color; Highlight = highlight;
        }
    }

    [Header("客（CustomerCharacter）。子に頭上の値札（TextMeshPro）を1つ持たせる")]
    [SerializeField] private List<CustomerCharacter> customers = new();
    [SerializeField] private MobAppearanceCatalog appearanceCatalog;

    [Header("位置（ワールド座標）")]
    [SerializeField] private Transform spawnLeft;
    [SerializeField] private Transform spawnRight;
    [Tooltip("机の前の立ち位置。左から順に置く")]
    [SerializeField] private List<Transform> standPoints = new();

    [Header("操作を止める透明な板（UI）")]
    [SerializeField] private GameObject inputBlocker;

    [Header("時間")]
    [SerializeField] private int maxCustomers = 6;
    [SerializeField] private float spawnInterval = 0.8f;
    [Tooltip("値札を出してから次へ進むまでの余韻（秒）")]
    [SerializeField] private float labelHold = 1.0f;
    [Tooltip("全体の上限（秒）。演出を速くする設定ではこの半分")]
    [SerializeField] private float maxDuration = 10f;
    [Tooltip("押した「営業開始」で即スキップしないための猶予（秒）")]
    [SerializeField] private float skipGrace = 0.35f;

    public bool IsWired => customers != null && customers.Count > 0 && standPoints != null && standPoints.Count > 0;

    /// <summary>再生中か（AutoPlay や二重起動の判定用）。</summary>
    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        HideAll();
        if (inputBlocker != null) inputBlocker.SetActive(false);
    }

    /// <summary>ダイジェストを再生し、終わる（または飛ばす）まで待つ。</summary>
    public async UniTask PlayAsync(IReadOnlyList<Entry> entries, CancellationToken token)
    {
        if (!IsWired || entries == null || entries.Count == 0) return;

        IsPlaying = true;
        if (inputBlocker != null) inputBlocker.SetActive(true);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(token, destroyCancellationToken);
        var ct = linked.Token;
        float scale = GameSettings.EffectDurationScale;
        float limit = maxDuration * scale;
        float startTime = Time.unscaledTime;
        bool skipped = false;

        try
        {
            var running = new List<UniTask>();
            int count = Mathf.Min(entries.Count, maxCustomers, customers.Count);
            var standOrder = PickStandPoints(count);

            for (int i = 0; i < count; i++)
            {
                running.Add(RunCustomerAsync(customers[i], entries[i], standOrder[i], ct));
                // 次の客まで待つ（飛ばされたら打ち切る）
                if (i < count - 1 && await WaitOrSkipAsync(spawnInterval * scale, startTime, limit, ct)) { skipped = true; break; }
            }

            if (!skipped)
            {
                // 全員の値札が出るまで待つ（上限・スキップあり）
                var all = UniTask.WhenAll(running);
                while (all.Status == UniTaskStatus.Pending)
                {
                    if (SkipPressed(startTime) || Time.unscaledTime - startTime >= limit) break;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                if (!SkipPressed(startTime))
                    await WaitOrSkipAsync(labelHold * scale, startTime, limit, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // シーン破棄・飛ばし
        }
        finally
        {
            linked.Cancel(); // 歩いている客の待ちを止める
            linked.Dispose();
            HideAll();
            if (inputBlocker != null) inputBlocker.SetActive(false);
            IsPlaying = false;
        }
    }

    /// <summary>立ち位置を、左右に散らして count 個選ぶ。</summary>
    private List<Transform> PickStandPoints(int count)
    {
        var pool = new List<Transform>(standPoints);
        pool.RemoveAll(t => t == null);
        var result = new List<Transform>();
        for (int i = 0; i < count; i++)
        {
            if (pool.Count == 0) pool.AddRange(standPoints.FindAll(t => t != null));
            int pick = UnityEngine.Random.Range(0, pool.Count);
            result.Add(pool[pick]);
            pool.RemoveAt(pick);
        }
        return result;
    }

    private async UniTask RunCustomerAsync(CustomerCharacter customer, Entry entry, Transform stand, CancellationToken ct)
    {
        if (customer == null || stand == null) return;

        // 立ち位置に近い側の画面外から来る（主人公の前を横切らないように）
        bool fromLeft = stand.position.x < 0f;
        Vector3 spawn = fromLeft
            ? (spawnLeft != null ? spawnLeft.position : stand.position + Vector3.left * 10f)
            : (spawnRight != null ? spawnRight.position : stand.position + Vector3.right * 10f);
        spawn.y = stand.position.y;
        spawn.z = stand.position.z;

        var label = customer.GetComponentInChildren<TextMeshPro>(true);
        if (label != null) label.gameObject.SetActive(false);

        customer.gameObject.SetActive(true);
        if (appearanceCatalog != null)
        {
            var picked = appearanceCatalog.PickRandom(UsedLooks(customer));
            if (picked != null) customer.SetAppearance(picked);
        }
        customer.Play(entry.Icon, spawn, stand.position);

        // 机の前に着いたら値札を出す
        float timeout = Time.unscaledTime + 8f;
        while (customer != null && customer.gameObject.activeSelf
               && (customer.transform.position - stand.position).sqrMagnitude > 0.01f
               && Time.unscaledTime < timeout)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (customer == null || label == null) return;

        label.text = entry.Label;
        label.color = entry.Color;
        label.gameObject.SetActive(true);
        var t = label.transform;
        t.DOKill();
        Vector3 baseScale = Vector3.one;
        t.localScale = baseScale * 0.4f;
        t.DOScale(baseScale * (entry.Highlight ? 1.15f : 1f), 0.25f).SetEase(Ease.OutBack).SetLink(label.gameObject);
        SoundManager.Instance?.PlaySE("営業/SE_売上音");
        await UniTask.Delay(TimeSpan.FromSeconds(0.6f * GameSettings.EffectDurationScale), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
    }

    private HashSet<SPUM_Prefabs> UsedLooks(CustomerCharacter self)
    {
        var used = new HashSet<SPUM_Prefabs>();
        foreach (var c in customers)
        {
            if (c == null || c == self || !c.gameObject.activeSelf) continue;
            if (c.CurrentAppearance != null) used.Add(c.CurrentAppearance);
        }
        return used;
    }

    /// <summary>seconds 待つ。飛ばされた・上限に達したら true。</summary>
    private async UniTask<bool> WaitOrSkipAsync(float seconds, float startTime, float limit, CancellationToken ct)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end)
        {
            if (SkipPressed(startTime) || Time.unscaledTime - startTime >= limit) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private bool SkipPressed(float startTime) =>
        Time.unscaledTime - startTime >= skipGrace && ControllerInput.AnyPressedThisFrame();

    private void HideAll()
    {
        if (customers == null) return;
        foreach (var c in customers)
        {
            if (c == null) continue;
            var label = c.GetComponentInChildren<TextMeshPro>(true);
            if (label != null) label.gameObject.SetActive(false);
            c.gameObject.SetActive(false);
        }
    }
}
