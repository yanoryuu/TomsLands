// Steamworks.NET は Standalone（Win/Linux/Mac）でだけ使う。Android などでは型ごと存在しないので触らない。
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
#define TOMS_STEAM
#endif

using System;
using UnityEngine;
#if TOMS_STEAM
using Steamworks;
#endif

/// <summary>
/// Steamworks の初期化・コールバック・終了を受け持つ常駐オブジェクト（1つだけ）。
/// BootLifetimeScope が起動時に1つ作り、DontDestroyOnLoad で全シーンに残す。
///
/// Steam が起動していない（エディタで Steam を落としている・exe を直接起動した・Steam 版でない）ときは
/// <see cref="Initialized"/> が false のまま何もしない。呼び出し側は必ず Initialized を見てから Steam API を使う。
/// 製品ビルドで「Steam 経由で起動し直す」(RestartAppIfNecessary) は、Steam 外の起動でもゲームを止めない方針なので呼ばない
/// （Docs/SteamAchievements.md「起動と steam_appid.txt」）。
/// </summary>
public sealed class SteamManager : MonoBehaviour
{
    /// <summary>トコのトムトムランドの Steam App ID。</summary>
    public const uint AppId = 4535810;

    private static SteamManager instance;

    /// <summary>Steam API が使える状態か。false の間は Steam API を呼ばない。</summary>
    public static bool Initialized { get; private set; }

    /// <summary>初期化できなかった理由（デバッグメニュー表示用）。成功時は空。</summary>
    public static string StatusMessage { get; private set; } = "未起動";

    /// <summary>Steam が有効な間、毎フレーム RunCallbacks の後に呼ばれる（実績の StoreStats をまとめて送るのに使う）。</summary>
    public static event Action Ticked;

    /// <summary>SteamAPI.Shutdown の直前に呼ばれる（送り残しの StoreStats を送る）。</summary>
    public static event Action ShuttingDown;

    // Domain Reload を切った Play でも前回の値を持ち越さない
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        Initialized = false;
        StatusMessage = "未起動";
        Ticked = null;
        ShuttingDown = null;
    }

    private void Awake()
    {
        // Boot に戻ってきた等で2つ目ができたら捨てる（初期化は1回だけ）
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        TryInitialize();
    }

    private static void TryInitialize()
    {
#if TOMS_STEAM
        try
        {
            if (!Packsize.Test())
            {
                StatusMessage = "Packsize の不一致（Steamworks.NET のビルド設定が誤り）";
                Debug.LogError($"[Steam] {StatusMessage}");
                return;
            }
            if (!DllCheck.Test())
            {
                StatusMessage = "steam_api DLL の版が合わない";
                Debug.LogError($"[Steam] {StatusMessage}");
                return;
            }

            var result = SteamAPI.InitEx(out string errorMessage);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                // Steam 外の起動では普通に起きる。ゲームはそのまま続ける
                // 体験版は App ID が未取得（DemoBuild.DemoSteamAppId = 0）の間もここに来る。ゲームは止めない
                StatusMessage = $"無効（{result}: {errorMessage}）";
                Debug.Log($"[Steam] Steam を使わずに起動します: {result} {errorMessage}");
                return;
            }

            Initialized = true;
            StatusMessage = "有効";
            Debug.Log($"[Steam] 初期化しました (AppId={SteamUtils.GetAppID()}{(DemoBuild.IsDemo ? $"、体験版。想定の App ID={DemoBuild.DemoSteamAppId}（0 = 未取得）" : "")})");
        }
        catch (DllNotFoundException e)
        {
            // ネイティブ DLL が無い（Plugins の同梱漏れ）。ゲームは止めない
            StatusMessage = "steam_api DLL が見つからない";
            Debug.LogWarning($"[Steam] {StatusMessage}: {e.Message}");
        }
        catch (Exception e)
        {
            StatusMessage = $"初期化で例外: {e.GetType().Name}";
            Debug.LogException(e);
        }
#else
        StatusMessage = "この環境では Steam を使わない";
#endif
    }

    private void Update()
    {
#if TOMS_STEAM
        if (!Initialized) return;
        SteamAPI.RunCallbacks();
        try
        {
            Ticked?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
#endif
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
#if TOMS_STEAM
        if (Initialized)
        {
            try
            {
                ShuttingDown?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            SteamAPI.Shutdown();
            Initialized = false;
            StatusMessage = "終了";
        }
#endif
    }
}
