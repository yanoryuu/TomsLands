using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using R3;

public class CharacterStatusView : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text mpText;
    [Tooltip("数値の前に付けるラベル（例: HP）。空なら数値のみ")]
    [SerializeField] private string hpLabel = "";
    [Tooltip("数値の前に付けるラベル（例: MP）。空なら数値のみ")]
    [SerializeField] private string mpLabel = "";

    [Header("ポップアップ設定")]
    [SerializeField] private DamagePopup damagePopupPrefab;
    [SerializeField] private Transform popupContainer;

    // 赤フラッシュさせるレンダラー。CharacterView があればそこから（SPUM なら子レンダラー全部）、無ければ親の SpriteRenderer 1枚
    private SpriteRenderer[] flashRenderers = Array.Empty<SpriteRenderer>();
    private Color[] originalSpriteColors = Array.Empty<Color>();
    private CancellationTokenSource damageFlashCts;
    private int damageFlashVersion;
    private readonly CompositeDisposable subscriptions = new();
    private IBattleCharacterViewModel boundViewModel;

    /// <summary>
    /// #14 ダメージ数字の層の並び順。配信画面の UI（Canvas 0〜12）より上、ホバー説明（500）より下。
    /// 演出用レイヤーは並び順ごとに 1 つ作られる（DamageNumber.SortingOrder は出す間だけ切り替えて戻す）。
    /// </summary>
    private const int DamageNumberOrder = 400;
    /// <summary>この割合以上を一撃で削ったらクリティカル表示（配信コメントの HeroCritical と同じ目安）。</summary>
    private const float CriticalDamageRatio = 0.5f;

    private void Awake()
    {
        var view = GetComponentInParent<CharacterView>();
        if (view != null) flashRenderers = view.FlashRenderers;
        if (flashRenderers.Length == 0)
        {
            var sr = GetComponentInParent<SpriteRenderer>();
            if (sr != null) flashRenderers = new[] { sr };
        }
        originalSpriteColors = Array.ConvertAll(flashRenderers, r => r != null ? r.color : Color.white);
    }

    private void SetFlash(bool on)
    {
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            var r = flashRenderers[i];
            if (r == null) continue;
            // 赤にする（元の透明度は保つ。1枚絵の白スプライトなら従来どおり Color.red と同じ）
            r.color = on ? new Color(1f, 0f, 0f, originalSpriteColors[i].a) : originalSpriteColors[i];
        }
    }

    private void OnDestroy()
    {
        subscriptions.Dispose();
        damageFlashCts?.Cancel();
        damageFlashCts?.Dispose();
    }

    /// <summary>
    /// ViewModelを受け取り、ModelのHPとMPを監視する設定を行う
    /// </summary>
    public void Initialize(IBattleCharacterViewModel viewModel)
    {
        subscriptions.Clear();
        ResetDamageFlash();
        boundViewModel = viewModel;

        viewModel.CurrentHp.Pairwise()
            .Subscribe(pair => HandleHpChanged(pair.Current, pair.Previous))
            .AddTo(subscriptions);

        viewModel.CurrentMp
            .Subscribe(mp => UpdateMpText(mp))
            .AddTo(subscriptions);

        UpdateHpText(viewModel.CurrentHp.CurrentValue);
        UpdateMpText(viewModel.CurrentMp.CurrentValue);
    }

    private void HandleHpChanged(int current, int previous)
    {
        int damage = previous - current;
        if (damage > 0)
            PlayDamageEffects(damage);
        else if (damage < 0 && previous > 0)
            SpawnNumber(-damage, DamageKind.Heal); // 回復（緑の +N）

        UpdateHpText(current);
    }

    /// <summary>ダメージ演出（ダメージ数字と赤フラッシュ）。</summary>
    public void PlayDamageEffects(int damageAmount)
    {
        // #14 ダメージ数字（共通部品。キャラの頭上に弾んで出る）。従来の DamagePopup（上へ消える「-N」）の置き換え。
        // 省略モードでは出ない（従来のポップアップも 0.5 秒で消えるだけで、残る状態は無い）
        int maxHp = boundViewModel != null ? boundViewModel.MaxHp : 0;
        bool critical = maxHp > 0 && damageAmount >= maxHp * CriticalDamageRatio;
        SpawnNumber(damageAmount, critical ? DamageKind.Critical : DamageKind.Normal);
        damageFlashCts?.Cancel();
        damageFlashCts?.Dispose();
        damageFlashCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        int version = ++damageFlashVersion;
        PlayDamageFlash(damageFlashCts.Token, version).Forget();
    }

    private async UniTaskVoid PlayDamageFlash(CancellationToken token, int version)
    {
        if (flashRenderers.Length == 0) return;

        try
        {
            SetFlash(true);
            await UniTask.Delay(TimeSpan.FromMilliseconds(150), cancellationToken: token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 後から始まったフラッシュがあればそちらに戻しを任せる
            if (version == damageFlashVersion)
                SetFlash(false);
        }
    }

    /// <summary>キャラの絵の頭上（レンダラーの上端の中央）に数字を出す。絵が無ければ自分の位置。</summary>
    private void SpawnNumber(int amount, DamageKind kind)
    {
        if (amount <= 0 || !isActiveAndEnabled || UIMotion.IsSkip) return;
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 pos = transform.position;
        bool hasBounds = false;
        Bounds b = default;
        foreach (var r in flashRenderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!hasBounds) { b = r.bounds; hasBounds = true; }
            else b.Encapsulate(r.bounds);
        }
        if (hasBounds) pos = new Vector3(b.center.x, b.max.y, b.center.z);

        int prev = DamageNumber.SortingOrder;
        DamageNumber.SortingOrder = DamageNumberOrder;
        DamageNumber.SpawnAtWorld(pos, amount, kind, cam);
        DamageNumber.SortingOrder = prev;
    }

    private void ResetDamageFlash()
    {
        damageFlashCts?.Cancel();
        damageFlashCts?.Dispose();
        damageFlashCts = null;
        damageFlashVersion++;

        SetFlash(false);
    }

    private void UpdateHpText(int current)
    {
        if (hpText != null)
            hpText.text = FormatStat(hpLabel, Mathf.Max(0, current)); // 倒れた一撃で HP がマイナスになっても表示は 0 で止める
    }

    private void UpdateMpText(int current)
    {
        if (mpText != null)
            mpText.text = FormatStat(mpLabel, current);
    }

    // ラベルは数値より一回り小さく表示する
    private static string FormatStat(string label, int value)
        => string.IsNullOrEmpty(label) ? $"{value}" : $"<size=70%>{label}</size> {value}";
}
