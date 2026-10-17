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
    private SpriteRenderer[] flashRenderers = new SpriteRenderer[0];
    private Color[] originalSpriteColors = new Color[0];
    private CancellationTokenSource damageFlashCts;
    private int damageFlashVersion;
    private CompositeDisposable subscriptions = new CompositeDisposable(); // 複数の購読をまとめる用

    private void Awake()
    {
        var view = GetComponentInParent<CharacterView>();
        if (view != null) flashRenderers = view.FlashRenderers;
        if (flashRenderers.Length == 0)
        {
            var sr = GetComponentInParent<SpriteRenderer>();
            if (sr != null) flashRenderers = new[] { sr };
        }
        originalSpriteColors = System.Array.ConvertAll(flashRenderers, r => r != null ? r.color : Color.white);
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
        // このオブジェクトが破棄される時に、全ての購読をまとめて停止
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

        // viewModelが誰であろうと、契約通りにHPとMPのデータをもらうだけです
        viewModel.CurrentHp.Pairwise()
            .Subscribe(pair => HandleHpChanged(pair.Current, pair.Previous))
            .AddTo(subscriptions);

        viewModel.CurrentMp
            .Subscribe(mp => UpdateMpText(mp))
            .AddTo(subscriptions);

        UpdateHpText(viewModel.CurrentHp.CurrentValue);
        UpdateMpText(viewModel.CurrentMp.CurrentValue);
    }

    // HPが変更された時に呼ばれる処理
    private void HandleHpChanged(int current, int previous)
    {
        int damage = previous - current;
        if (damage > 0)
        {
            // ダメージポップアップとフラッシュ効果を再生
            PlayDamageEffects(damage);
        }

        // HPテキストを更新
        UpdateHpText(current);
    }

    // ダメージ演出（ポップアップとフラッシュ）
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

    // キャラクターが赤く光るフラッシュ効果
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
            if (version == damageFlashVersion)
            {
                SetFlash(false);
            }
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

    // HPテキストを更新する処理
    private void UpdateHpText(int current)
    {
        if (hpText != null)
            hpText.text = FormatStat(hpLabel, current);
    }

    // MPテキストを更新する処理
    private void UpdateMpText(int current)
    {
        if (mpText != null)
            mpText.text = FormatStat(mpLabel, current);
    }

    // ラベルは数値より一回り小さく表示する
    private static string FormatStat(string label, int value)
        => string.IsNullOrEmpty(label) ? $"{value}" : $"<size=70%>{label}</size> {value}";
}
