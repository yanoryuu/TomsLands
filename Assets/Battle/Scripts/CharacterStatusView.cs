using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;
using System;
using R3;

public class CharacterStatusView : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text mpText;

    [Header("ポップアップ設定")]
    [SerializeField] private DamagePopup damagePopupPrefab;
    [SerializeField] private Transform popupContainer;

    private SpriteRenderer characterSpriteRenderer;
    private CompositeDisposable subscriptions = new CompositeDisposable(); // 複数の購読をまとめる用

    private void Awake()
    {
        // 親オブジェクトからSpriteRendererを探してキャッシュ
        characterSpriteRenderer = GetComponentInParent<SpriteRenderer>();
    }

    private void OnDestroy()
    {
        // このオブジェクトが破棄される時に、全ての購読をまとめて停止
        subscriptions.Dispose();
    }

    /// <summary>
    /// ViewModelを受け取り、ModelのHPとMPを監視する設定を行う
    /// </summary>
    public void Initialize(IBattleCharacterViewModel viewModel)
    {
        subscriptions.Clear();

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
        PlayDamageFlash().Forget();
    }

    // キャラクターが赤く光るフラッシュ効果
    private async UniTaskVoid PlayDamageFlash()
    {
        if (characterSpriteRenderer == null) return;
        Color originalColor = characterSpriteRenderer.color;
        characterSpriteRenderer.color = Color.red;
        await UniTask.Delay(TimeSpan.FromMilliseconds(150));
        characterSpriteRenderer.color = originalColor;
    }

    // HPテキストを更新する処理
    private void UpdateHpText(int current)
    {
        if (hpText != null)
            hpText.text = $"{current}";
    }

    // MPテキストを更新する処理
    private void UpdateMpText(int current)
    {
        if (mpText != null)
            mpText.text = $"{current}";
    }
}