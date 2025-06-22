using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterStatusView : MonoBehaviour
{
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text mpText;

    private BattleCharacter _targetCharacter;
    
    public void SetTargetCharacter(BattleCharacter hero)
    {
        UnsubscribeFromTarget();
        _targetCharacter = hero;
        
        if (_targetCharacter != null)
        {
            _targetCharacter.OnHpChanged += UpdateHpText;
            _targetCharacter.OnMpChanged += UpdateMpText;

            UpdateHpText(_targetCharacter.CurrentHp, _targetCharacter.MaxHp);
            UpdateMpText(_targetCharacter.CurrentMp, _targetCharacter.MaxMp);
        }
    }

    private void UpdateHpText(int current, int max)
    {
        if (hpText != null) hpText.text = $"HP {current}";
    }

    private void UpdateMpText(int current, int max)
    {
        if (mpText != null) mpText.text = $"MP {current}";
    }
    
    private void UnsubscribeFromTarget()
    {
        if (_targetCharacter != null)
        {
            _targetCharacter.OnHpChanged -= UpdateHpText;
            _targetCharacter.OnMpChanged -= UpdateMpText;
        }
    }
}