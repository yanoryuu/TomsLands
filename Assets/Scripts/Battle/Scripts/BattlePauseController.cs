using Cysharp.Threading.Tasks;
using System.Threading;

/// <summary>
/// Time.timeScale を使わないバトル一時停止管理。
/// 各ループが自分でポーズ状態を確認して待機する方式。
/// </summary>
public class BattlePauseController
{
    public bool IsPaused { get; private set; }

    public void Pause()  => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void Toggle() { if (IsPaused) Resume(); else Pause(); }

    /// <summary>
    /// ポーズ中は毎フレーム待機し続ける。解除されると即座に返る。
    /// CancellationToken がキャンセルされた場合も正常に抜ける。
    /// </summary>
    public async UniTask WaitIfPausedAsync(CancellationToken token)
    {
        while (IsPaused && !token.IsCancellationRequested)
            await UniTask.Yield(PlayerLoopTiming.Update, token);
    }
}
