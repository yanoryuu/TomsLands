﻿﻿using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// シーン遷移を一元管理するサービス。
/// ScriptableObject ベースのデータ受け渡しと組み合わせて使う。
/// </summary>
public class SceneTransitionService
{
    private readonly BattleInputData _battleInputData;
    private readonly BattleOutputData _battleOutputData;

    public SceneTransitionService(BattleInputData battleInputData, BattleOutputData battleOutputData)
    {
        _battleInputData = battleInputData;
        _battleOutputData = battleOutputData;
    }

    /// <summary>
    /// EventScene へ遷移する。事前に EventInputData を書き込んでおくこと。
    /// </summary>
    public void GoToEvent()
    {
        Debug.Log("[SceneTransition] Loading EventScene...");
        SceneManager.LoadScene("EventScene");
    }

    /// <summary>
    /// FightScene へ遷移する。事前に BattleInputData を書き込んでおくこと。
    /// </summary>
    public void GoToBattle()
    {
        _battleOutputData.Clear();
        Debug.Log("[SceneTransition] Loading FightScene...");
        SceneManager.LoadScene("FightScene");
    }

    /// <summary>
    /// TomsShop へ戻る。事前に BattleOutputData を書き込んでおくこと。
    /// </summary>
    public void ReturnToTomsShop()
    {
        Debug.Log("[SceneTransition] Returning to TomsShop...");
        SceneManager.LoadScene("TomsShop");
    }

    /// <summary>
    /// タイトルシーン（Start）へ遷移する。
    /// </summary>
    public void GoToTitle()
    {
        Debug.Log("[SceneTransition] Returning to Title (Start)...");
        SceneManager.LoadScene("Start");
    }
}

