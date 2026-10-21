using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// ブートシーンの駆動役。ローディング画面（SceneLoader の常駐オーバーレイ。勇者が歩く）を出しつつ
/// リモートコンフィグの取得を待ち、完了後にタイトルシーンへ遷移する。GameConst はこの遷移後に初めて参照される。
/// </summary>
public sealed class LoadingPresenter : IAsyncStartable
{
    private const string NextSceneName = "TitleScene";
    /// <summary>チラ見え防止の最小表示時間（秒）。</summary>
    private const float MinDisplaySec = 0.6f;

    private readonly RemoteConfigService _service;
    private readonly ItemMasterService _itemMasterService;
    private readonly RemoteBalanceService _balanceService;

    public LoadingPresenter(RemoteConfigService service, ItemMasterService itemMasterService,
        RemoteBalanceService balanceService)
    {
        _service = service;
        _itemMasterService = itemMasterService;
        _balanceService = balanceService;
    }

    public async UniTask StartAsync(CancellationToken ct)
    {
        // 起動直後は裏に何も無いのでフェード無しで出す。以降の TitleScene への遷移はこのまま画面を引き継ぐ
        SceneLoader.ShowImmediate("設定を確認中……");
        float startTime = Time.realtimeSinceStartup;

        try
        {
            // GameConst調整値・アイテムマスター・バランスを並行取得（各3段フォールバックは Service 内で完結）。
            await UniTask.WhenAll(
                _service.InitializeAsync(ct),
                _itemMasterService.InitializeAsync(ct),
                _balanceService.InitializeAsync(ct));
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

        // ローディング画面を出したまま TitleScene を読み込み、読み込み後にフェードアウトする
        SceneLoader.Load(NextSceneName);
    }
}
