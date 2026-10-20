using UnityEngine;

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

#if UNITY_EDITOR
    /// <summary>
    /// 【エディタ専用・開発用】シーンロードの横取りフック。引数はシーン名、true を返すとロードしない。
    /// Jev AutoPlay のヘッドレス試走で「配信へ / リザルトへ / ゲームオーバーへ」の遷移を
    /// 実際のシーンロード無しに検知するために使う。既定 null（=従来挙動と完全一致）。ビルドには含まれない。
    /// </summary>
    public static System.Func<string, bool> DevSceneLoadInterceptor;
#endif

    private static void LoadScene(string sceneName)
    {
#if UNITY_EDITOR
        if (DevSceneLoadInterceptor != null && DevSceneLoadInterceptor(sceneName)) return;
#endif
        // ローディング画面つきの非同期遷移。旧シーンはフェードイン〜読み込みの間（約0.3〜1秒）生き続ける点に注意
        SceneLoader.Load(sceneName);
    }

    /// <summary>
    /// EventScene へ遷移する。事前に EventInputData を書き込んでおくこと。
    /// 未使用（2026-10-20 確認）: PendingEventData.UseInlineEventPopup が常に true なので呼ばれない。EventScene は Build Settings に無く、
    /// 呼ぶとビルドでは止まる。EventScenePresenter は ChangeMoney/ChangeTrust しか実行しない（AddItem・GrantRelic などが無い）。
    /// </summary>
    public void GoToEvent()
    {
        Debug.Log("[SceneTransition] Loading EventScene...");
        LoadScene("EventScene");
    }

    /// <summary>
    /// FightScene へ遷移する。事前に BattleInputData を書き込んでおくこと。
    /// </summary>
    public void GoToBattle()
    {
        _battleOutputData.Clear();
        Debug.Log("[SceneTransition] Loading FightScene...");
        LoadScene("FightScene");
    }

    /// <summary>
    /// TomsShop へ戻る。事前に BattleOutputData を書き込んでおくこと。
    /// </summary>
    public void ReturnToTomsShop()
    {
        Debug.Log("[SceneTransition] Returning to TomsShop...");
        LoadScene("TomsShop");
    }

    /// <summary>
    /// タイトルシーン（Start）へ遷移する。
    /// </summary>
    public void GoToTitle()
    {
        Debug.Log("[SceneTransition] Returning to Title (Start)...");
        LoadScene("TitleScene");
    }

    /// <summary>
    /// ResultScene へ遷移する。事前にセーブデータを保存しておくこと。
    /// </summary>
    public void GoToResult()
    {
        Debug.Log("[SceneTransition] Loading ResultScene...");
        LoadScene("ResultScene");
    }

    /// <summary>
    /// GameOver シーンへ遷移する。借金返済不能時に呼び出す。
    /// </summary>
    public void GoToGameOver()
    {
        Debug.Log("[SceneTransition] Loading GameOver...");
        LoadScene("GameOver");
    }

    /// <summary>
    /// 村シーン（メタ層）へ遷移する。ラン終了後の帰還先。
    /// 事前に VillageArrivalReport を書き込んでおくと収支ポップが表示される。
    /// </summary>
    public void GoToVillage()
    {
        Debug.Log("[SceneTransition] Loading VillageScene...");
        LoadScene("VillageScene");
    }
}

