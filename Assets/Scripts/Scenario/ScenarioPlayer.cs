using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using Utage;

/// <summary>
/// Utage（宴）の AdvEngine をゲーム側から使うためのファサード。
/// ゲームコードが AdvEngine を直接参照しないよう、会話の再生はすべてこのクラス越しに行う。
/// ScenarioSystem プレハブのルートにアタッチし、DontDestroyOnLoad で常駐する。
/// </summary>
public class ScenarioPlayer : SingletonMonoBehaviour<ScenarioPlayer>
{
    [Header("Utage")]
    [Tooltip("同じプレハブ内の AdvEngine。普段は非アクティブで、会話中だけ有効化する。")]
    [SerializeField] private AdvEngine engine;

    [Header("待機のタイムアウト（秒）")]
    [Tooltip("シナリオが開始しない／終わらない場合に無限待ちを避けるための保険。")]
    [SerializeField] private float startTimeoutSeconds = 15f;
    [SerializeField] private float playTimeoutSeconds = 600f;

    private readonly ReactiveProperty<bool> isPlaying = new(false);
    private bool isDisposed;

    /// <summary>会話を再生中かどうか。ゲーム側の入力抑止などに使う。</summary>
    public ReadOnlyReactiveProperty<bool> IsPlaying => isPlaying;

    /// <summary>シナリオが PauseScenario で中断しているか。</summary>
    public bool IsPausing => engine != null && engine.IsPausingScenario;

    /// <summary>中断中のシナリオを再開する。中断していなければ false。</summary>
    public bool Resume() => engine != null && engine.ResumeScenario();

    /// <summary>
    /// シナリオデータの読み込み完了を待つ。HasLabel を使う前に呼ぶこと。
    /// </summary>
    public async UniTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        if (engine == null) return;
        if (!engine.gameObject.activeSelf) engine.gameObject.SetActive(true);
        await UniTask.WaitWhile(() => engine != null && engine.IsWaitBootLoading, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 指定ラベルがシナリオに存在するか。
    /// 会話を持たないイベントを呼んでもエラーにしないための事前チェックに使う。
    /// </summary>
    public bool HasLabel(string label)
    {
        if (string.IsNullOrEmpty(label) || engine == null) return false;
        if (label[0] == '*') label = label.Substring(1);
        var dataManager = engine.DataManager;
        if (dataManager == null) return false;
        return dataManager.FindScenarioLabelData(label) != null;
    }

    /// <summary>
    /// 指定ラベルの会話を再生し、終了するまで待つ。
    /// ラベルは先頭の * を付けても付けなくてもよい。
    /// </summary>
    public async UniTask PlayAsync(string label, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(label))
        {
            Debug.LogError("[ScenarioPlayer] ラベルが空です。");
            return;
        }
        if (engine == null)
        {
            Debug.LogError("[ScenarioPlayer] AdvEngine が未アサインです。ScenarioSystem プレハブを確認してください。");
            return;
        }
        if (isPlaying.Value)
        {
            Debug.LogWarning($"[ScenarioPlayer] 別の会話を再生中のため '{label}' をスキップしました。");
            return;
        }

        if (isDisposed) return;
        isPlaying.Value = true;
        try
        {
            // 会話UIの開閉は宴側（AdvUguiManager）が行うため、エンジン自体は一度有効にしたら落とさない
            if (!engine.gameObject.activeSelf) engine.gameObject.SetActive(true);

            // シナリオデータとリソースのロード完了を待つ
            await UniTask.WaitWhile(() => engine != null && engine.IsWaitBootLoading, cancellationToken: cancellationToken);
            if (engine == null) return;

            engine.StartGame(label);

            // 実際に再生が始まるまで待つ（開始しない場合はタイムアウトで打ち切る）
            if (!await WaitUntilWithTimeout(() => engine == null || IsScenarioThreadRunning(), startTimeoutSeconds, cancellationToken))
            {
                Debug.LogError($"[ScenarioPlayer] ラベル '{label}' の再生が開始しませんでした。シナリオにラベルが存在するか確認してください。");
                return;
            }

            // 終了まで待つ
            // エンジンが破棄されていたら（Play終了・シーン破棄）その時点で打ち切る
            if (!await WaitUntilWithTimeout(() => engine == null || engine.IsEndScenario, playTimeoutSeconds, cancellationToken))
            {
                Debug.LogError($"[ScenarioPlayer] ラベル '{label}' の会話が終了しませんでした。EndScenario の記述漏れの可能性があります。");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            // 破棄後（Play終了時）に書き込むと ObjectDisposedException になる
            if (!isDisposed) isPlaying.Value = false;
        }
    }

    /// <summary>シナリオスレッドが実際に走り出したか。</summary>
    private bool IsScenarioThreadRunning()
    {
        var player = engine != null ? engine.ScenarioPlayer : null;
        var thread = player != null ? player.MainThread : null;
        return thread != null && thread.IsPlaying;
    }

    private static async UniTask<bool> WaitUntilWithTimeout(Func<bool> predicate, float timeoutSeconds, CancellationToken cancellationToken)
    {
        float deadline = Time.unscaledTime + timeoutSeconds;
        while (!predicate())
        {
            if (Time.unscaledTime > deadline) return false;
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
        return true;
    }

    protected override void Awake()
    {
        base.Awake();
        if (engine == null) engine = GetComponentInChildren<AdvEngine>(true);
    }

    private void OnDestroy()
    {
        isDisposed = true;
        isPlaying.Dispose();
    }
}
