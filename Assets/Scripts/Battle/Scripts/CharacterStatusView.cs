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

        UpdateHpText(current);
    }

    /// <summary>ダメージ演出（ポップアップと赤フラッシュ）。</summary>
    public void PlayDamageEffects(int damageAmount)
    {
        if (damagePopupPrefab != null && popupContainer != null)
        {
            DamagePopup popup = Instantiate(damagePopupPrefab, popupContainer);
            popup.ShowAsync(damageAmount).Forget();
        }
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
            hpText.text = FormatStat(hpLabel, current);
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
