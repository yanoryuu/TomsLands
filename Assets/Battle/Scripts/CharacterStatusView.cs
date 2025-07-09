using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;
using System;

/// <summary>
/// キャラクターのステータス表示とダメージポップアップ管理
/// </summary>
public class CharacterStatusView : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text attackText;
    [SerializeField] private TMP_Text defenseText;

    [Header("ポップアップ設定")]
    [SerializeField] private DamagePopup damagePopupPrefab;
    [SerializeField] private Transform popupContainer;

    private SpriteRenderer characterSpriteRenderer;

    private BattleCharacter target;
    private int prevHp;
    
    private void Awake()
    {
        characterSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnDestroy()
    {
        UnsubscribeFromTarget();
    }

    public void Initialize(BattleCharacter character)
    {
        UnsubscribeFromTarget();

        target = character;
        if (target != null)
        {
            prevHp = target.CurrentHp;
            target.OnHpChanged += HandleHpChanged;
            UpdateHpText(target.CurrentHp);
            UpdateAttackText(target.AttackPower);
            UpdateDefenseText(target.DefensePower);
        }
    }

    // HP変更時のコールバック
    private void HandleHpChanged(int current, int max ,BattleCharacter character)
    {
        int delta = prevHp - current;
        
        

        // ダメージを受けた場合
        if (delta > 0)
        {
            // ダメージポップアップ表示を開始（完了を待たない）
            if (damagePopupPrefab != null && popupContainer != null)
            {
                DamagePopup popup = Instantiate(damagePopupPrefab, popupContainer);
                popup.ShowAsync(delta).Forget(); // .Forget()で「撃ちっぱなし」にする
            }

            // ダメージフラッシュ効果を開始（完了を待たない）
            PlayDamageFlash().Forget();
        }

        // HPテキストは即座に更新する
        UpdateHpText(current);
        prevHp = current;
    }

    /// <summary>
    /// キャラクターを短時間赤く点滅させる
    /// </summary>
    private async UniTaskVoid PlayDamageFlash()
    {
        // スプライトが設定されていなければ何もしない
        if (characterSpriteRenderer == null) return;

        // 元の色を保存しておく（通常は白）
        Color originalColor = characterSpriteRenderer.color;

        // 赤色に変更
        characterSpriteRenderer.color = Color.red;

        // 0.15秒待機
        await UniTask.Delay(TimeSpan.FromMilliseconds(150));

        // 元の色に戻す
        characterSpriteRenderer.color = originalColor;
    }

    private void UpdateHpText(int current)
    {
        if (hpText != null)
            hpText.text = $"{current}";
    }

    private void UpdateAttackText(int attack)
    {
        if (attackText != null)
            attackText.text = $"{attack}";
    }

    private void UpdateDefenseText(int defense)
    {
        if (defenseText != null)
            defenseText.text = $"{defense}";
    }
    
    private void UnsubscribeFromTarget()
    {
        if (target != null)
        {
            target.OnHpChanged -= HandleHpChanged;
        }
    }
}
