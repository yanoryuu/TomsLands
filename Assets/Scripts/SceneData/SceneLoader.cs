using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ローディング画面つきのシーン遷移の窓口（静的ファサード）。ゲーム本編のシーン遷移はすべてここを通す。
/// 流れ: ローディング画面をフェードイン → LoadSceneAsync(Single) → 読み込み完了＋最小表示時間 → フェードアウト。
///
/// ローディング画面の実体は Resources/LoadingOverlay.prefab（LoadingView）。初回利用時に生成して DontDestroyOnLoad にするので、
/// どのシーンから Play しても動く。プレハブが無くても遷移だけは行う（演出なし）。
/// - 遷移中（新シーン有効化前）の再呼び出しは無視する（連打・二重遷移の防止）。
/// - 新シーンの有効化後（ローディング画面がまだ出ている間）の呼び出しは「続けて遷移」として受け付ける。
///   未配線シーンが Start で素通り遷移するケース（村→準備→店など）を取りこぼさないため。
/// - 演出はすべて unscaled time。
/// </summary>
public static class SceneLoader
{
    private const string OverlayResourcePath = "LoadingOverlay";
    private const string DefaultStatus = "Now Loading...";
    /// <summary>チラ見え防止の最小表示時間（秒）。フェードインが終わってから数える。</summary>
    public const float MinDisplaySec = 0.5f;

    private static LoadingView overlay;
    private static bool overlayResolved;
    private static string pendingScene;
    private static bool activated;

    /// <summary>遷移処理中（フェードイン〜フェードアウト完了まで）か。</summary>
    public static bool IsLoading { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        overlay = null;
        overlayResolved = false;
        pendingScene = null;
        activated = false;
        IsLoading = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>ローディング画面つきでシーンを読み込む（投げっぱなし版）。</summary>
    public static void Load(string sceneName) => LoadAsync(sceneName).Forget();

    /// <summary>ローディング画面つきでシーンを読み込み、フェードアウトまで待つ。</summary>
    public static async UniTask LoadAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneLoader] シーン名が空です。");
            return;
        }

        if (IsLoading)
        {
            if (activated)
            {
                // 新シーンが有効化された後の呼び出し（素通り遷移など）は続けて読み込む
                Debug.Log($"[SceneLoader] 遷移中に次の遷移を受付: {sceneName}");
                pendingScene = sceneName;
            }
            else
            {
                Debug.LogWarning($"[SceneLoader] 遷移中のため {sceneName} への遷移要求を無視しました。");
            }
            return;
        }

        IsLoading = true;
        var view = GetOverlay();
        // DontDestroyOnLoad のオブジェクトに紐付けて、アプリ終了・プレイ停止時に止まるようにする
        var ct = view != null ? view.destroyCancellationToken : CancellationToken.None;
        SceneManager.sceneLoaded += OnSceneLoaded;
        try
        {
            await RunAsync(sceneName, view, ct);
        }
        catch (OperationCanceledException)
        {
            // プレイ停止・アプリ終了時。何もしない
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            pendingScene = null;
            activated = false;
            IsLoading = false;
            if (view != null) view.Hide();
        }
    }

    /// <summary>
    /// フェード無しで即座にローディング画面を出す（ブート直後など）。
    /// この後の LoadAsync はフェードインを省略してそのまま読み込む。
    /// </summary>
    public static void ShowImmediate(string status = null)
    {
        var view = GetOverlay();
        if (view == null) return;
        view.SetStatus(status ?? DefaultStatus);
        view.ShowImmediate();
    }

    /// <summary>ローディング画面のステータス文言を変える（未生成なら何もしない）。</summary>
    public static void SetStatus(string status)
    {
        if (overlay != null) overlay.SetStatus(status);
    }

    private static async UniTask RunAsync(string sceneName, LoadingView view, CancellationToken ct)
    {
        string next = sceneName;
        while (next != null)
        {
            activated = false;
            pendingScene = null;

            if (view != null)
            {
                view.SetStatus(DefaultStatus);
                if (!view.IsVisible) await view.FadeInAsync(ct);
            }

            float startTime = Time.realtimeSinceStartup;
            Debug.Log($"[SceneLoader] Loading {next}...");
            var op = SceneManager.LoadSceneAsync(next, LoadSceneMode.Single);
            if (op == null)
            {
                // Build Settings に無いシーン名など。Unity 側がエラーを出す
                Debug.LogError($"[SceneLoader] {next} の読み込みを開始できませんでした。");
                return;
            }
            await op.ToUniTask(cancellationToken: ct);

            // 一瞬で消えるのを防ぐ（新シーンの初期化が裏で走る時間にもなる）
            float remain = MinDisplaySec - (Time.realtimeSinceStartup - startTime);
            if (remain > 0f && pendingScene == null)
                await UniTask.WaitUntil(
                    () => pendingScene != null || Time.realtimeSinceStartup - startTime >= MinDisplaySec,
                    cancellationToken: ct);

            if (pendingScene != null)
            {
                next = pendingScene;
                continue;
            }

            if (view != null) await view.FadeOutAsync(ct);
            // フェードアウト中に来た要求は改めてフェードインして読み込む
            next = pendingScene;
            if (next != null && view != null) view.Hide();
        }
    }

    /// <summary>新シーンの Awake/OnEnable 後・Start 前に呼ばれる。以降の遷移要求は「続けて遷移」扱い。</summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) activated = true;
    }

    private static LoadingView GetOverlay()
    {
        if (overlay != null) return overlay;
        if (overlayResolved) return null; // プレハブが無いことは確認済み（毎回 Resources を探さない）
        overlayResolved = true;

        var prefab = Resources.Load<LoadingView>(OverlayResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[SceneLoader] Resources/{OverlayResourcePath} が見つかりません。ローディング画面なしで遷移します（Docs/LoadingScreen_UnityWiring.md 参照）。");
            return null;
        }

        overlay = UnityEngine.Object.Instantiate(prefab);
        overlay.name = prefab.name;
        UnityEngine.Object.DontDestroyOnLoad(overlay.gameObject);
        overlay.Hide();
        overlayResolved = false; // 破棄されたら作り直せるようにする
        return overlay;
    }
}
