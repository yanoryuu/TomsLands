using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/TomsLands/Jev AutoPlay — Jev（とベースラインボット）にランを最後まで自動で遊ばせ、レポートを出す。
/// 設計: Docs/Jev_AutoPlay_Design.md
/// </summary>
public sealed class AutoPlayWindow : EditorWindow
{
    private AutoPlayBatchConfig _config = new AutoPlayBatchConfig();
    private bool[] _personaOn;
    private Vector2 _scroll;

    [MenuItem("Tools/TomsLands/Jev AutoPlay", priority = 0)]
    public static void Open()
    {
        var w = GetWindow<AutoPlayWindow>("Jev AutoPlay");
        w.minSize = new Vector2(460, 520);
    }

    private void OnEnable()
    {
        _personaOn ??= new bool[AutoPlayPersona.All.Length];
        AutoPlayBatch.Changed += Repaint;
    }

    private void OnDisable()
    {
        AutoPlayBatch.Changed -= Repaint;
    }

    private void Update()
    {
        // 実行中は進捗表示を更新する
        if (AutoPlayBatch.IsRunning) Repaint();
    }

    private void OnGUI()
    {
        using (new EditorGUI.DisabledScope(AutoPlayBatch.IsRunning))
        {
            EditorGUILayout.LabelField("ラン設定", EditorStyles.boldLabel);
            _config.Mode = (GameModeId)EditorGUILayout.EnumPopup("ゲームモード", _config.Mode);
            _config.Seeds = Mathf.Clamp(EditorGUILayout.IntField("シード数", _config.Seeds), 1, 200);
            _config.BaseSeed = EditorGUILayout.IntField("開始シード", _config.BaseSeed);
            _config.InitMoneyOverride = Mathf.Max(0, EditorGUILayout.IntField(
                new GUIContent("初期資金の上書き", "0 = GameConst.InitMoney をそのまま使う"), _config.InitMoneyOverride));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ボット", EditorStyles.boldLabel);
            _config.UseGreedyBot = EditorGUILayout.ToggleLeft("貪欲（おすすめ通り＝おまかせ仕入れ・API不要）", _config.UseGreedyBot);
            _config.UseRandomBot = EditorGUILayout.ToggleLeft("ランダム（バグ探索・API不要）", _config.UseRandomBot);

            bool hasKey = JevApi.HasApiKey;
            using (new EditorGUI.DisabledScope(!hasKey))
            {
                for (int i = 0; i < AutoPlayPersona.All.Length; i++)
                {
                    var p = AutoPlayPersona.All[i];
                    _personaOn[i] = EditorGUILayout.ToggleLeft($"Jev: {p.LabelJa}（{p.Id}）", _personaOn[i] && hasKey);
                }
                _config.JevDecisionMode = (AutoPlayJevDecisionMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Jev の1択の決め方", "Sample=確率分布から抽選（シード固定） / Argmax=最尤"), _config.JevDecisionMode);
            }
            EditorGUILayout.HelpBox(hasKey
                ? "TYPESAFE_API_KEY: 設定あり（値は表示しません）"
                : "TYPESAFE_API_KEY が未設定のため Jev ボットは使えません（設定後は Unity Hub ごと再起動）。",
                hasKey ? MessageType.None : MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("配信サロゲート・実行", EditorStyles.boldLabel);
            _config.StreamSalesScale = EditorGUILayout.Slider(
                new GUIContent("配信販売の回数倍率", "戦闘ターン数 × この倍率 回だけ販売判定する（実機との較正用）"), _config.StreamSalesScale, 0f, 5f);
            _config.StreamMaxKinds = Mathf.Clamp(EditorGUILayout.IntField("配信に持ち込む銘柄数の上限", _config.StreamMaxKinds), 1, 20);
            _config.ProbabilisticBattle = EditorGUILayout.ToggleLeft("勝敗をクリア確率で抽選する（既定はドライランで決定論）", _config.ProbabilisticBattle);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("配信中の介入（スパチャ）", EditorStyles.boldLabel);
            _config.EnableInterventions = EditorGUILayout.ToggleLeft("ボットに介入を使わせる", _config.EnableInterventions);
            using (new EditorGUI.DisabledScope(!_config.EnableInterventions))
            {
                _config.ControlRunsWithoutInterventions = EditorGUILayout.ToggleLeft("同じシードで介入なしの対照群も回す（貪欲・ランダム）", _config.ControlRunsWithoutInterventions);
                _config.ControlRunsIncludeJev = EditorGUILayout.ToggleLeft("対照群に Jev も含める（費用2倍）", _config.ControlRunsIncludeJev);
                _config.ViewerSuperChat = (AutoPlayViewerSuperChatMode)EditorGUILayout.EnumPopup(
                    new GUIContent("視聴者の赤スパ（必殺技）", "FollowSettings = StreamingInteractionSettings の ON/OFF に従う"), _config.ViewerSuperChat);
                _config.ViewerRedChancePerTurn = EditorGUILayout.Slider(new GUIContent("視聴者赤スパ/戦闘ターン", "較正値"), _config.ViewerRedChancePerTurn, 0f, 0.3f);
                _config.SecondsPerBattleTurn = EditorGUILayout.Slider(new GUIContent("1戦闘ターンの秒数", "クールダウン（秒）をターンに直す換算。較正値"), _config.SecondsPerBattleTurn, 0.5f, 10f);
            }
            _config.MaxConcurrent = Mathf.Clamp(EditorGUILayout.IntField(
                new GUIContent("同時進行ラン数", "Jev の応答待ちを重ねるため。モデル操作はメインスレッドで交互に行う"), _config.MaxConcurrent), 1, 64);
            _config.VerboseLogs = EditorGUILayout.ToggleLeft("ゲーム本体の通常ログも出す（遅くなる）", _config.VerboseLogs);
        }

        _config.JevPersonas = AutoPlayPersona.All.Where((p, i) => _personaOn[i]).Select(p => p.Id).ToList();
        int bots = (_config.UseGreedyBot ? 1 : 0) + (_config.UseRandomBot ? 1 : 0) + _config.JevPersonas.Count;
        if (_config.EnableInterventions && _config.ControlRunsWithoutInterventions)
            bots += (_config.UseGreedyBot ? 1 : 0) + (_config.UseRandomBot ? 1 : 0) + (_config.ControlRunsIncludeJev ? _config.JevPersonas.Count : 0);
        double usd = AutoPlayBatch.EstimateJevCostUsd(_config);
        EditorGUILayout.HelpBox(
            $"{bots} ボット × {_config.Seeds} シード = {bots * _config.Seeds} ラン" +
            (_config.JevPersonas.Count > 0 ? $"\nJev 概算: ${usd:F4}（≒{usd * AutoPlayReport.JpyPerUsd:F1}円）" : ""),
            MessageType.Info);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(AutoPlayBatch.IsRunning || bots == 0))
            {
                if (GUILayout.Button("実行", GUILayout.Height(28))) AutoPlayBatch.Start(_config);
            }
            using (new EditorGUI.DisabledScope(!AutoPlayBatch.IsRunning))
            {
                if (GUILayout.Button("中止", GUILayout.Height(28), GUILayout.Width(80))) AutoPlayBatch.Cancel();
            }
            if (GUILayout.Button("レポート", GUILayout.Height(28), GUILayout.Width(80)))
            {
                Directory.CreateDirectory(AutoPlayBatch.ReportsRoot);
                EditorUtility.RevealInFinder(AutoPlayBatch.LastReportDir ?? AutoPlayBatch.ReportsRoot);
            }
        }

        if (!string.IsNullOrEmpty(AutoPlayBatch.LastMessage))
            EditorGUILayout.HelpBox(AutoPlayBatch.LastMessage, MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("進行状況", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var r in AutoPlayBatch.Runners)
        {
            var res = r.Result;
            EditorGUILayout.LabelField(res.RunId,
                $"{r.Status}  |  所持金 {res.FinalMoney:N0}  勇者 {res.HeroWins}勝{res.HeroLosses}敗  異常 {res.Anomalies.Count}" +
                (res.JevRequests > 0 ? $"  Jev {res.JevRequests}req" : ""));
        }
        EditorGUILayout.EndScrollView();
    }
}
