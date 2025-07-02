using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class CharacterStatusView : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text attackText;
    [SerializeField] private TMP_Text defenseText;

    // 監視対象のキャラクター
    private BattleCharacter _targetCharacter;

    void OnDestroy()
    {
        UnsubscribeFromTarget();
    }
    
    /// <summary>
    /// 監視対象のキャラクターを設定し、ステータスの表示を初期化します。
    /// 呼び出し元: BattleCharacter.cs の SetupHero() / Setup() メソッド内。
    /// </summary>
    public void Initialize(BattleCharacter target)
    {
        // 以前の購読を解除し、新しいターゲットを設定します。
        UnsubscribeFromTarget();
        _targetCharacter = target;
        
        if (_targetCharacter != null)
        {
            // HP変更イベントを購読します。
            _targetCharacter.OnHpChanged += UpdateHpText;

            // 初期のステータス表示を更新します。
            UpdateHpText(_targetCharacter.CurrentHp, _targetCharacter.MaxHp);
            UpdateAttackText(_targetCharacter.AttackPower);
            UpdateDefenseText(_targetCharacter.DefensePower);
        }
    }

    /// <summary>
    /// HPの表示を更新するメソッド。
    /// </summary>
    private void UpdateHpText(int current, int max)
    {
        if (hpText != null)
        {
            hpText.text = $"{current}";
        }
    }

    /// <summary>
    /// 攻撃力の表示を更新するメソッド。
    /// </summary>
    private void UpdateAttackText(int attack)
    {
        if (attackText != null)
        {
            attackText.text = $"{attack}";
        }
    }

    /// <summary>
    /// 防御力の表示を更新するメソッド。
    /// </summary>
    private void UpdateDefenseText(int defense)
    {
        if (defenseText != null)
        {
            defenseText.text = $"{defense}";
        }
    }

    private void UnsubscribeFromTarget()
    {
        if (_targetCharacter != null)
        {
            _targetCharacter.OnHpChanged -= UpdateHpText;
        }
    }
}
