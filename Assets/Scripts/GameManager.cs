using R3;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    // ReactivePropertyでフェーズを管理
    public ReactiveProperty<GamePhase> CurrentPhase { get; private set; }

    private void Awake()
    {
        // 初期フェーズ設定
        CurrentPhase = new ReactiveProperty<GamePhase>(GamePhase.Preparation);
    }

    public void ProceedToNextPhase()
    {
        switch (CurrentPhase.Value)
        {
            case GamePhase.Preparation:
                CurrentPhase.Value = GamePhase.Battle;
                break;
            case GamePhase.Battle:
                CurrentPhase.Value = GamePhase.End;
                break;
            case GamePhase.End:
                CurrentPhase.Value = GamePhase.Preparation;
                break;
            default:
                Debug.LogWarning("不正なフェーズ遷移");
                break;
        }
    }
}