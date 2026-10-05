using UnityEngine;

/// <summary>
/// どのシーンから起動しても SoundManager が存在するよう保証するブートストラップ。
/// BeforeSceneLoad のタイミングで Resources/SoundManager プレハブを生成する。
/// プレハブの SoundManager コンポーネントに SoundLibrary をアサインしておくこと。
/// </summary>
public static class SoundManagerLoader
{
    private const string PrefabPath = "SoundManager"; // Assets/Resources/SoundManager.prefab

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Instance プロパティは未発見時に LogError を出すため FindFirstObjectByType で直接チェック
        if (Object.FindFirstObjectByType<SoundManager>() != null) return;

        var prefab = AddressableLoader.Load<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[SoundManagerLoader] Resources/SoundManager プレハブが見つかりません。" +
                           "Assets/Resources/SoundManager.prefab を作成し、SoundLibrary を設定してください。");
            return;
        }

        // Instantiate すると SoundManager.Awake が走り、DontDestroyOnLoad も適用される
        Object.Instantiate(prefab).name = "SoundManager";
    }
}
