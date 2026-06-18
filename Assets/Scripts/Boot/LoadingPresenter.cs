using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// ブートシーンの駆動役。ローディング画面を出しつつリモートコンフィグの取得を待ち、
/// 完了後にタイトルシーンへ遷移する。GameConst はこの遷移後に初めて参照される。
/// </summary>
public sealed class LoadingPresenter : IAsyncStartable
{
    private const string NextSceneName = "TitleScene";
    /// <summary>チラ見え防止の最小表示時間（秒）。</summary>
    private const float MinDisplaySec = 0.6f;

    private readonly RemoteConfigService _service;
    private readonly LoadingView _view;

    public LoadingPresenter(RemoteConfigService service, LoadingView view)
    {
        _service = service;
        _view = view;
    }

    public async UniTask StartAsync(CancellationToken ct)
    {
        _view?.SetStatus("設定を確認中...");
        float startTime = Time.realtimeSinceStartup;

        try
        {
            // 成功→キャッシュ→ベイク済みデフォルト の3段フォールバックは Service 内で完結。
            await _service.InitializeAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            // 取得層は例外を投げない設計だが、保険として握り潰して起動を継続する。
            Debug.LogError($"[Loading] リモートコンフィグ初期化で例外: {e.Message}");
        }

        // 最小表示時間を満たすまで待つ（一瞬で消えるのを防ぐ）
        float remain = MinDisplaySec - (Time.realtimeSinceStartup - startTime);
        if (remain > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(remain), ignoreTimeScale: true, cancellationToken: ct);

        _view?.SetStatus("読み込み完了");
        if (_view != null)
            await _view.FadeOutAsync(ct);

        SceneManager.LoadScene(NextSceneName);
    }
}
