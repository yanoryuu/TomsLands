#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using UnityEngine;
using VContainer;

/// <summary>
/// デバッグメニュー「オート」タブ。プレイ中の実ゲーム画面を自動で進める（DebugAutoPlayer）。
/// 仕様: Docs/DebugMenu_Spec.md「オート」。エディタ専用のヘッドレス試走（Editor/AutoPlay）・Jev は使わない。
/// </summary>
public partial class DebugMenuView
{
    private DebugAutoPlayer _autoPlayer;

    // UI の選択状態（シーンをまたいで保持）
    private static int _apTurnChoice;
    private static readonly int[] ApTurnOptions = { 1, 5, 10, 0 };
    private static readonly string[] ApTurnLabels = { "1", "5", "10", "∞" };
    private static readonly string[] ApStrategyLabels = { "標準", "陳列のみ", "何もしない" };
    private static readonly int[] ApBudgetOptions = { 100, 75, 50, 25 };
    private static readonly string[] ApBudgetLabels = { "100%", "75%", "50%", "25%" };
    private static readonly float[] ApTimeScales = { 1f, 2f, 4f, 8f };

    /// <summary>オート進行ドライバを同じ GameObject に生やし、このシーンの依存を渡す。</summary>
    [Inject]
    public void ConstructAutoPlay(IObjectResolver resolver)
    {
        _autoPlayer = gameObject.GetComponent<DebugAutoPlayer>();
        if (_autoPlayer == null) _autoPlayer = gameObject.AddComponent<DebugAutoPlayer>();
        _autoPlayer.Initialize(resolver);
    }

    private void DrawAutoPlaySection()
    {
        GUILayout.Label("■ オートプレイ（実ゲーム画面を自動進行）", _headerStyle);

        if (_autoPlayer == null)
        {
            GUILayout.Label("（このシーンではオート進行ドライバが初期化されていません）");
            return;
        }

        // ---- 状態 ----
        string state = !DebugAutoPlayer.Running ? "停止中"
                     : DebugAutoPlayer.Paused ? "一時停止中"
                     : "実行中";
        GUILayout.Label($"状態: {state}");
        if (DebugAutoPlayer.Paused)
            GUILayout.Label($"理由: {DebugAutoPlayer.PauseReason}");
        GUILayout.Label($"いま: {DebugAutoPlayer.Status}");

        int elapsed = DebugAutoPlayer.ElapsedTurns;
        string limit = DebugAutoPlayer.TurnLimit > 0 ? $"/{DebugAutoPlayer.TurnLimit}" : "/∞";
        GUILayout.Label(elapsed >= 0
            ? $"経過: {elapsed}{limit} ターン（T{DebugAutoPlayer.StartTurn}→T{DebugAutoPlayer.LastKnownTurn}）"
            : "経過: -");

        if (DebugAutoPlayer.MoneyHistory.Count > 0)
        {
            var sb = new StringBuilder("所持金: ");
            for (int i = 0; i < DebugAutoPlayer.MoneyHistory.Count; i++)
            {
                var (turn, money) = DebugAutoPlayer.MoneyHistory[i];
                if (i > 0) sb.Append(i % 3 == 0 ? "\n  → " : " → ");
                sb.Append($"T{turn}:{money:N0}G");
            }
            GUILayout.Label(sb.ToString());
        }

        // ---- 開始/停止 ----
        GUILayout.BeginHorizontal();
        GUI.enabled = !DebugAutoPlayer.Running;
        if (GUILayout.Button("開始"))
            _autoPlayer.Begin(DebugAutoPlayScope.Continuous, ApTurnOptions[_apTurnChoice]);
        GUI.enabled = DebugAutoPlayer.Running;
        if (DebugAutoPlayer.Paused)
        {
            if (GUILayout.Button("再開")) DebugAutoPlayer.Resume();
        }
        else
        {
            if (GUILayout.Button("一時停止")) DebugAutoPlayer.PauseManual();
        }
        if (GUILayout.Button("停止")) DebugAutoPlayer.Stop("手動で停止");
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("進めるターン数", GUILayout.Width(110));
        GUI.enabled = !DebugAutoPlayer.Running;
        _apTurnChoice = GUILayout.SelectionGrid(_apTurnChoice, ApTurnLabels, ApTurnLabels.Length);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        // ---- 単発 ----
        GUILayout.Space(4);
        GUILayout.Label("■ 単発", _headerStyle);
        GUI.enabled = !DebugAutoPlayer.Running;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("このターンだけ自動"))
            _autoPlayer.Begin(DebugAutoPlayScope.Continuous, 1);
        if (GUILayout.Button("仕入れだけ自動"))
            _autoPlayer.Begin(DebugAutoPlayScope.ProcurementOnly, 0);
        if (GUILayout.Button("陳列だけ自動"))
            _autoPlayer.Begin(DebugAutoPlayScope.DisplayOnly, 0);
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        // ---- 設定 ----
        GUILayout.Space(4);
        GUILayout.Label("■ 設定", _headerStyle);

        GUILayout.Label($"戦略: {DebugAutoPlayer.StrategyLabel(DebugAutoPlayer.Strategy)}");
        DebugAutoPlayer.Strategy = (DebugAutoPlayStrategy)GUILayout.SelectionGrid(
            (int)DebugAutoPlayer.Strategy, ApStrategyLabels, ApStrategyLabels.Length);

        GUILayout.BeginHorizontal();
        GUILayout.Label("仕入れ予算", GUILayout.Width(110));
        int budgetIndex = System.Array.IndexOf(ApBudgetOptions, DebugAutoPlayer.BudgetPercent);
        if (budgetIndex < 0) budgetIndex = 0;
        budgetIndex = GUILayout.SelectionGrid(budgetIndex, ApBudgetLabels, ApBudgetLabels.Length);
        DebugAutoPlayer.BudgetPercent = ApBudgetOptions[budgetIndex];
        GUILayout.EndHorizontal();
        GUILayout.Label("  ※ 予算は「所持金 − 2ターン以内の納税額」に対する割合");

        GUILayout.BeginHorizontal();
        GUILayout.Label($"ステップ間ウェイト {DebugAutoPlayer.StepWait:0.00}秒", GUILayout.Width(170));
        DebugAutoPlayer.StepWait = GUILayout.HorizontalSlider(DebugAutoPlayer.StepWait, 0.05f, 2f);
        GUILayout.EndHorizontal();

        DebugAutoPlayer.WaitFollowsTimeScale = GUILayout.Toggle(DebugAutoPlayer.WaitFollowsTimeScale,
            " ウェイトを timeScale に連動（x2 なら半分の実時間）");

        GUILayout.BeginHorizontal();
        GUILayout.Label($"timeScale x{Time.timeScale:0.#}", GUILayout.Width(110));
        foreach (var t in ApTimeScales)
        {
            if (GUILayout.Button($"x{t:0}")) Time.timeScale = t;
        }
        GUILayout.EndHorizontal();

        DebugAutoPlayer.AutoAdvanceScenario = GUILayout.Toggle(DebugAutoPlayer.AutoAdvanceScenario,
            " 会話（Utage）を自動で送る（選択肢は先頭）");
        DebugAutoPlayer.PauseOnErrorLog = GUILayout.Toggle(DebugAutoPlayer.PauseOnErrorLog,
            " エラーログを検出したら一時停止");

        // ---- ログ ----
        GUILayout.Space(4);
        GUILayout.Label("■ ログ", _headerStyle);
        if (DebugAutoPlayer.LogLines.Count == 0)
        {
            GUILayout.Label("（まだありません）");
        }
        else
        {
            for (int i = DebugAutoPlayer.LogLines.Count - 1; i >= 0; i--)
                GUILayout.Label(DebugAutoPlayer.LogLines[i]);
        }
    }
}
#endif
