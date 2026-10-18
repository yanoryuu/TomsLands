using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.UI;
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

    // ゲームパッド対応: 会話中は背面の画面へ Cancel 等と決定・フォーカスが届かないようにする
    private UIFocusScope dialogScope;
    private IDisposable scopeInputBlock;
    private int playingCount;

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
        // 別の会話を再生中なら、終わるまで待ってから再生する。
        // 以前はスキップしていたが、呼び出し側（TutorialScenarioService.PlayOnceAsync）が
        // 再生されなかった会話を既読にしてしまうため、待ち合わせに変更した。
        while (isPlaying.Value)
        {
            if (isDisposed) return;
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        if (isDisposed) return;
        isPlaying.Value = true;
        BeginDialogInput();
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
        finally
        {
            // 破棄後（Play終了時）に書き込むと ObjectDisposedException になる
            EndDialogInput();
            if (!isDisposed) isPlaying.Value = false;
        }
    }

    /// <summary>会話中の入力まわり（背面UIの操作を止め、選択肢ボタンへフォーカスを渡す）。</summary>
    private void BeginDialogInput()
    {
        playingCount++;
        // Utage 側の InputSystemStrategy（BeforeSceneLoad）と登録順が競合して上書きされることがあるため、会話開始ごとに入れ直す
        GamepadInputStrategy.Install();
        GamepadInputStrategy.IsMessageWindowHidden = IsMessageWindowHidden;
        if (scopeInputBlock == null) scopeInputBlock = UIFocusManager.BlockScopeInput();
        if (dialogScope == null)
        {
            dialogScope = UIFocusScope.GetOrAdd(gameObject);
            dialogScope.Modal = true;
            dialogScope.Priority = 50;
            dialogScope.RememberLastSelected = false;
        }
        dialogScope.AutoSelect = false; // 選択肢が出るまでは何も選ばない（背面の選択も外す）
        dialogScope.FirstSelected = null;
        dialogScope.enabled = true;
    }

    private bool IsMessageWindowHidden()
        => engine != null && engine.UiManager != null && engine.UiManager.Status == AdvUiManager.UiStatus.HideMessageWindow;

    private void EndDialogInput()
    {
        playingCount = Mathf.Max(0, playingCount - 1);
        if (playingCount > 0) return;
        GamepadInputStrategy.SelectionActive = false;
        scopeInputBlock?.Dispose();
        scopeInputBlock = null;
        if (dialogScope != null) dialogScope.enabled = false;
    }

    private bool selectionWasActive;
    private readonly List<AdvUguiSelection> selectionBuffer = new();

    private void Update()
    {
        if (dialogScope == null || !dialogScope.enabled || engine == null) return;

        bool selecting = engine.SelectionManager != null && engine.SelectionManager.IsWaitInput;
        GamepadInputStrategy.SelectionActive = selecting;

        Button first = null;
        if (selecting)
        {
            // 毎フレーム呼ばれるので配列を作らないバッファ版で取る
            GetComponentsInChildren(false, selectionBuffer);
            foreach (var item in selectionBuffer)
            {
                if (item.TryGetComponent<Button>(out var b) && b.isActiveAndEnabled && b.IsInteractable()) { first = b; break; }
            }
            selectionBuffer.Clear();
        }

        if (first != null)
        {
            dialogScope.AutoSelect = true;
            if (!selectionWasActive || dialogScope.FirstSelected == null || !dialogScope.FirstSelected.isActiveAndEnabled)
            {
                dialogScope.FirstSelected = first;
                dialogScope.Refocus(true);
            }
            selectionWasActive = true;
        }
        else
        {
            dialogScope.AutoSelect = false;
            dialogScope.FirstSelected = null;
            selectionWasActive = false;
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
        GamepadInputStrategy.Install(); // Utage の入力にゲームパッドを追加
        if (engine == null) engine = GetComponentInChildren<AdvEngine>(true);
        // 宴のカメラを各シーンの Base カメラへ Overlay として積む（Base のままだと URP が画面を黄色でクリアする）
        if (!TryGetComponent<UtageCameraStacker>(out _)) gameObject.AddComponent<UtageCameraStacker>();
    }

    private void OnDestroy()
    {
        isDisposed = true;
        scopeInputBlock?.Dispose();
        GamepadInputStrategy.SelectionActive = false;
        GamepadInputStrategy.IsMessageWindowHidden = null;
        isPlaying.Dispose();
    }
}
