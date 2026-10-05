using UnityEngine;

/// <summary>
/// どのシーンから起動しても会話システム（Utage の AdvEngine 一式）が存在するよう保証するブートストラップ。
/// SoundManagerLoader と同じく BeforeSceneLoad で常駐プレハブを生成する。
/// </summary>
public static class ScenarioSystemLoader
{
    private const string PrefabAddress = "ScenarioSystem"; // Assets/TomsScenario/Prefabs/ScenarioSystem.prefab

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Instance プロパティは未発見時に LogError を出すため FindFirstObjectByType で直接チェック
        if (Object.FindFirstObjectByType<ScenarioPlayer>() != null) return;

        var prefab = AddressableLoader.Load<GameObject>(PrefabAddress);
        if (prefab == null)
        {
            Debug.LogError($"[ScenarioSystemLoader] アドレス '{PrefabAddress}' のプレハブが見つかりません。" +
                           "Assets/TomsScenario/Prefabs/ScenarioSystem.prefab を Addressables に登録してください。");
            return;
        }

        // Instantiate すると ScenarioPlayer.Awake が走り、DontDestroyOnLoad も適用される
        Object.Instantiate(prefab).name = "ScenarioSystem";
    }
}
