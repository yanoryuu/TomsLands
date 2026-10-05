using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 区切りの場面（配信からの帰還・ランクリア・破産）で流すトコの会話。
/// 状況からラベルを選ぶ処理と再生をまとめる。セリフ本体は Assets/TomsScenario/Scenarios/TokoTalk.csv。
/// 既読管理はせず毎回流す（チュートリアルではなく、場面の締めの一言のため）。
/// </summary>
public static class TokoTalk
{
    // ---- 配信から店へ帰ってきたとき（勝敗 × 稼ぎ）。同じ枠は2本からランダムに選ぶ ----
    private static readonly string[] StreamWinBig    = { "Toko_StreamWin_Big_1",    "Toko_StreamWin_Big_2" };
    private static readonly string[] StreamWinSmall  = { "Toko_StreamWin_Small_1",  "Toko_StreamWin_Small_2" };
    private static readonly string[] StreamLoseBig   = { "Toko_StreamLose_Big_1",   "Toko_StreamLose_Big_2" };
    private static readonly string[] StreamLoseSmall = { "Toko_StreamLose_Small_1", "Toko_StreamLose_Small_2" };
    private static readonly string[] StreamDeficit   = { "Toko_StreamDeficit_1",    "Toko_StreamDeficit_2" };

    // ---- ランクリア（ResultScene）----
    private const string RunClearHigh = "Toko_RunClear_High";
    private const string RunClearMid  = "Toko_RunClear_Mid";
    private const string RunClearLow  = "Toko_RunClear_Low";

    // ---- 破産（GameOver）----
    private const string BankruptEarly = "Toko_Bankrupt_Early";
    private const string BankruptLate  = "Toko_Bankrupt_Late";

    /// <summary>初配信の帰還では占い師との後日談（Tutorial_StreamingEnd）が流れるため、トコの一言は出さない。</summary>
    private const string FirstStreamingEndTutorial = "Tutorial_StreamingEnd";

    /// <summary>配信の稼ぎが、帰還前の所持金に対してこの割合以上なら「大儲け」扱い。</summary>
    private const float BigEarningsRatio = 0.3f;

    /// <summary>店の画面が出揃ってから話し始めるための待ち時間（秒）。</summary>
    private const float ReturnTalkDelaySeconds = 0.6f;

    private const string TomsShopSceneName = "TomsShop";

    // =====================================================
    // ラベル選択
    // =====================================================

    public static string SelectStreamingReturnLabel(BattleResult result, int totalEarnings, int moneyAfter)
    {
        if (totalEarnings < 0) return Pick(StreamDeficit);

        int moneyBefore = Mathf.Max(1, moneyAfter - totalEarnings);
        bool big = totalEarnings >= moneyBefore * BigEarningsRatio;

        return result == BattleResult.Victory
            ? Pick(big ? StreamWinBig : StreamWinSmall)
            : Pick(big ? StreamLoseBig : StreamLoseSmall);
    }

    public static string SelectRunClearLabel(ResultStatisticsData stats)
    {
        if (stats == null) return RunClearMid;
        if (stats.Rank == "D" || stats.MoneyDifference < 0) return RunClearLow;
        if (stats.Rank == "S" || stats.Rank == "A") return RunClearHigh;
        return RunClearMid;
    }

    /// <param name="paidDebtCycles">破産までに納付を済ませた回数（TomsModel.DebtCycle）</param>
    public static string SelectBankruptLabel(int paidDebtCycles)
        => paidDebtCycles <= 0 ? BankruptEarly : BankruptLate;

    /// <summary>同じ枠の差分からランダムに1本選ぶ（毎回同じセリフにならないように）。</summary>
    private static string Pick(string[] labels) => labels[UnityEngine.Random.Range(0, labels.Length)];

    // =====================================================
    // 再生
    // =====================================================

    /// <summary>ラベルの会話を流して終わるまで待つ。会話システムが無い／ラベル未定義なら何もしない。</summary>
    public static UniTask PlayAsync(string label, CancellationToken cancellationToken = default)
        => TutorialScenarioService.PlayAlwaysAsync(label, cancellationToken);

    /// <summary>
    /// 配信から店へ帰ってきたときの一言。BattleResultHandler.Start から投げっぱなしで呼ぶ。
    /// 帰還処理の直後に次の配信やリザルトへそのまま遷移することがあるため、
    /// 少し待ってから「まだ店のシーンにいる」ことを確かめて再生する。
    /// </summary>
    public static async UniTaskVoid PlayStreamingReturnAsync(
        BattleResult result, int totalEarnings, int moneyAfter,
        MetaProgressModel meta, CancellationToken cancellationToken)
    {
        try
        {
            if (meta == null || !meta.HasSeenTutorial(FirstStreamingEndTutorial)) return;

            string label = SelectStreamingReturnLabel(result, totalEarnings, moneyAfter);

            await UniTask.Delay(TimeSpan.FromSeconds(ReturnTalkDelaySeconds), DelayType.UnscaledDeltaTime,
                cancellationToken: cancellationToken);
            if (SceneManager.GetActiveScene().name != TomsShopSceneName) return;

            await PlayAsync(label, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // シーン遷移で店が破棄された（次の配信・リザルトへ直行した）場合は流さない
        }
        catch (Exception e)
        {
            Debug.LogError($"[TokoTalk] 配信帰還の会話に失敗しました。\n{e}");
        }
    }
}
