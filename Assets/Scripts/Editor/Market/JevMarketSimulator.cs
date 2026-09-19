using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// =====================================================================
// 価格変動モデルの比較シミュレータ。
//
// 3つのエンジンを同一条件（同じシード・同じ需要推移）で回し、
// 価格系列と統計量を並べて比較する。
//
//   Legacy   : 現行の ItemModel.ApplyShopTurnEconomy 相当（需要帯ごとの一様乱数）
//   LocalAbm : ローカル・エージェントベースモデル（外部API不要・出荷対象）
//   Jev      : TypeSafe Jev に仮想トレーダーを演じさせる（開発時のみ・お手本）
//
// 需要（Demand / Trend）の更新はどのエンジンでも完全に同一にしてあるので、
// 差分は純粋に「価格の決まり方」だけに由来する。
// =====================================================================

public sealed class JevMarketSimulator : EditorWindow
{
    // ------------------------------------------------------------
    // 現行 ShopEconomySettings の既定値のミラー。
    // Legacy エンジンを現行実装と一致させるための定数。
    // ------------------------------------------------------------
    private const float HighDemandThreshold = 0.7f;
    private const float LowDemandThreshold = 0.3f;
    private const float HighRateMin = 1.01f, HighRateMax = 1.03f;
    private const float LowRateMin = 0.97f, LowRateMax = 0.99f;
    private const float NormalRateMin = 0.99f, NormalRateMax = 1.01f;
    private const float PriceFloorRate = 0.3f, PriceCeilingRate = 3.0f;

    private const float TrendAmplitude = 0.30f;
    private const float TrendConvergenceRate = 0.15f;
    private const float TrendDriftMax = 0.12f;
    private const float TrendDecayRate = 0.10f;
    private const float DemandFloor = 0.05f, DemandCeiling = 1.0f;
    private const float DisplayDemandUp = 0.02f, NotDisplayDemandDown = 0.01f;

    private const string ItemDataFolder = "Assets/Resources_moved/ItemData";

    // ------------------------------------------------------------
    // 設定
    // ------------------------------------------------------------
    [SerializeField] private int _turns = 60;
    [SerializeField] private int _itemCount = 8;
    [SerializeField] private int _seed = 20260919;
    [SerializeField] private bool _runLegacy = true;
    [SerializeField] private bool _runLocalAbm = true;
    [SerializeField] private bool _runJev;
    [SerializeField] private int _jevTraderCount = 30;
    [SerializeField] private LocalAbmSettings _abm = new LocalAbmSettings();
    [SerializeField] private MarketModelPreset _preset;
    [SerializeField] private int _calibrationIterations = 400;

    private Vector2 _scroll;
    private int _chartItemIndex;
    private bool _running;
    private string _status = "未実行";
    private CancellationTokenSource _cts;
    private readonly List<RunResult> _results = new List<RunResult>();
    private string[] _itemIds = Array.Empty<string>();

    /// <summary>1エンジン分の実行結果。</summary>
    private sealed class RunResult
    {
        public string Label;
        public Color Color;
        public Dictionary<string, List<int>> Series = new Dictionary<string, List<int>>();
        public MarketStats Stats;
        public long InputTokens;
        public int Requests;
    }

    /// <summary>
    /// シミュレーション用の銘柄1件。ローカルABM へ渡すため <see cref="IMarketView"/> を実装する。
    /// </summary>
    private sealed class SimItem : IMarketView
    {
        public string Id, Name, Category, Element;
        public int Base;
        public int Price;
        public float DemandValue, PrevDemandValue, Trend;
        public int StockValue;
        public bool Displayed;
        public readonly List<int> History = new List<int>();
        public float LastNet;

        public int CurrentPrice => Price;
        public int BasePrice => Base;
        public float Demand => DemandValue;
        public float PreviousDemand => PrevDemandValue;
        public int Stock => StockValue;
        public IReadOnlyList<int> PriceHistory => History;
        public float LastNetOrder => LastNet;
    }

    [MenuItem("Tools/Market/Jev Market Simulator")]
    private static void Open()
    {
        GetWindow<JevMarketSimulator>("Market Sim").minSize = new Vector2(620, 560);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawSettings();
        EditorGUILayout.Space();
        DrawRunControls();
        EditorGUILayout.Space();
        DrawResults();

        EditorGUILayout.EndScrollView();
    }

    // ============================================================
    // 設定 UI
    // ============================================================
    private void DrawSettings()
    {
        EditorGUILayout.LabelField("共通設定", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(_running))
        {
            _turns = EditorGUILayout.IntSlider("ターン数", _turns, 10, 300);
            _itemCount = EditorGUILayout.IntSlider("銘柄数", _itemCount, 1, 40);
            _seed = EditorGUILayout.IntField("シード", _seed);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("実行するエンジン", EditorStyles.boldLabel);
            _runLegacy = EditorGUILayout.Toggle("Legacy（現行の一様乱数）", _runLegacy);
            _runLocalAbm = EditorGUILayout.Toggle("Local ABM（出荷対象）", _runLocalAbm);
            _runJev = EditorGUILayout.Toggle("Jev（外部API・課金あり）", _runJev);

            if (_runLocalAbm)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Local ABM パラメータ", EditorStyles.boldLabel);
                _abm.traderCount = EditorGUILayout.IntSlider("トレーダー数", _abm.traderCount, 2, 500);
                _abm.capitalParetoAlpha = EditorGUILayout.Slider("資金パレート指数 α", _abm.capitalParetoAlpha, 0.6f, 3f);
                _abm.momentumGain = EditorGUILayout.Slider("順張りゲイン", _abm.momentumGain, 0f, 30f);
                _abm.valueGain = EditorGUILayout.Slider("逆張りゲイン", _abm.valueGain, 0f, 10f);
                _abm.demandGain = EditorGUILayout.Slider("需要ゲイン", _abm.demandGain, 0f, 30f);
                _abm.marketMakerGain = EditorGUILayout.Slider("MMゲイン", _abm.marketMakerGain, 0f, 20f);
                _abm.herdingGain = EditorGUILayout.Slider("群衆行動", _abm.herdingGain, 0f, 1f);
                _abm.inactionBandMax = EditorGUILayout.Slider("不感帯（尖度を上げる）", _abm.inactionBandMax, 0f, 1f);
                _abm.lambda = EditorGUILayout.Slider("価格インパクト λ", _abm.lambda, 0.005f, 0.5f);
                _abm.baseDepth = EditorGUILayout.Slider("板の厚み", _abm.baseDepth, 10f, 2000f);
            }

            if (_runJev)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Jev 設定", EditorStyles.boldLabel);
                _jevTraderCount = EditorGUILayout.IntSlider("トレーダー数", _jevTraderCount, 2, 80);

                int questions = _jevTraderCount * 2;
                EditorGUILayout.HelpBox(
                    $"1ターン = 1リクエスト / {questions}問（全質問が並列評価される）\n" +
                    $"{_turns}ターンで約 {_turns} リクエスト。概算コストは実行後に表示されます。\n" +
                    "※ 独立した判断を平均するため、人数を増やすほど値動きは正規分布へ近づき、\n" +
                    "　 ファットテールは失われます。20〜40人が推奨レンジです。",
                    MessageType.Info);

                if (!JevApi.HasApiKey)
                {
                    EditorGUILayout.HelpBox(
                        "環境変数 TYPESAFE_API_KEY が未設定です。\n" +
                        "設定後、Unity を再起動すると読み込まれます。",
                        MessageType.Warning);
                }
            }
        }
    }

    // ============================================================
    // 実行 UI
    // ============================================================
    private void DrawRunControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_running || (!_runLegacy && !_runLocalAbm && !_runJev)))
            {
                if (GUILayout.Button("実行", GUILayout.Height(28))) RunAsync();
            }
            using (new EditorGUI.DisabledScope(!_running))
            {
                if (GUILayout.Button("中止", GUILayout.Height(28), GUILayout.Width(80))) _cts?.Cancel();
            }
            using (new EditorGUI.DisabledScope(_running || _results.Count == 0))
            {
                if (GUILayout.Button("CSV出力", GUILayout.Height(28), GUILayout.Width(100))) ExportCsv();
            }
        }

        using (new EditorGUI.DisabledScope(_running))
        {
            if (GUILayout.Button("人数スイープ（Local ABM・10/30/100/300人で尖度を比較）"))
            {
                RunTraderCountSweep();
            }
        }

        EditorGUILayout.LabelField("状態", _status);
        EditorGUILayout.Space();
        DrawCalibration();
    }

    // ============================================================
    // キャリブレーション（Jev のお手本統計量へ ABM を合わせ込む）
    // ============================================================
    private void DrawCalibration()
    {
        EditorGUILayout.LabelField("キャリブレーション", EditorStyles.boldLabel);
        _preset = (MarketModelPreset)EditorGUILayout.ObjectField(
            "プリセット", _preset, typeof(MarketModelPreset), false);

        using (new EditorGUI.DisabledScope(_running || _preset == null))
        {
            _calibrationIterations = EditorGUILayout.IntSlider("試行回数", _calibrationIterations, 50, 2000);
            if (GUILayout.Button("プリセットの目標値へ ABM を合わせ込む", GUILayout.Height(24)))
            {
                RunCalibration();
            }

            // 直近の Jev 実行結果を「お手本」としてプリセットの目標値へ取り込む
            var jevRun = _results.Find(r => r.Label == "Jev");
            using (new EditorGUI.DisabledScope(jevRun == null))
            {
                if (GUILayout.Button("直近の Jev 結果を目標値に設定"))
                {
                    Undo.RecordObject(_preset, "Set Calibration Target");
                    _preset.targetKurtosis = jevRun.Stats.Kurtosis;
                    _preset.targetAbsAutocorr1 = jevRun.Stats.AbsReturnAutocorr1;
                    _preset.targetAutocorr1 = jevRun.Stats.ReturnAutocorr1;
                    _preset.targetStdDev = jevRun.Stats.StdDevReturn;
                    _preset.referenceNote =
                        $"Jev 参照ラン: {_turns}ターン×{_itemCount}銘柄 / トレーダー{_jevTraderCount}人 / " +
                        $"lambda={_abm.lambda} / seed={_seed}";
                    EditorUtility.SetDirty(_preset);
                    AssetDatabase.SaveAssets();
                    _status = "目標値を Jev の実測値で更新しました: " + jevRun.Stats;
                }
            }
        }

        if (_preset == null)
        {
            EditorGUILayout.HelpBox(
                "MarketModelPreset アセットを作成して割り当ててください。\n" +
                "Project ビューで右クリック → Create → ScriptableObjects → Market → MarketModelPreset",
                MessageType.Info);
        }
    }

    private void RunCalibration()
    {
        _running = true;
        try
        {
            var target = new AbmCalibrationTarget
            {
                Kurtosis = _preset.targetKurtosis,
                AbsReturnAutocorr1 = _preset.targetAbsAutocorr1,
                ReturnAutocorr1 = _preset.targetAutocorr1,
                StdDevReturn = _preset.targetStdDev,
            };

            var result = AbmCalibrator.Fit(
                target, _preset.abm, _calibrationIterations,
                turns: Mathf.Max(150, _turns), itemCount: _itemCount, seed: _seed,
                onProgress: (i, total, loss) =>
                {
                    if (i % 25 != 0 && i != total) return;
                    EditorUtility.DisplayProgressBar(
                        "ABM キャリブレーション",
                        $"{i}/{total}  最良Loss={loss:F3}", i / (float)total);
                });

            Undo.RecordObject(_preset, "Calibrate Market Model");
            _preset.abm = result.Best;
            _preset.RecordResult(result.Stats, result.Loss,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            EditorUtility.SetDirty(_preset);
            AssetDatabase.SaveAssets();

            _abm = result.Best.Clone();
            _status = $"キャリブレーション完了: Loss {result.InitialLoss:F3} → {result.Loss:F3} / {result.Stats}";
            Debug.Log($"[Calibration] 開始時: {result.InitialStats}\n[Calibration] 完了時: {result.Stats}");
        }
        catch (Exception ex)
        {
            _status = "エラー: " + ex.Message;
            Debug.LogException(ex);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            _running = false;
            Repaint();
        }
    }

    // ============================================================
    // 結果 UI
    // ============================================================
    private void DrawResults()
    {
        if (_results.Count == 0) return;

        EditorGUILayout.LabelField("統計量の比較", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "kurt（尖度）: 正規分布=3、実市場=5〜10。大きいほど「たまに大暴落」が起きる。\n" +
            "|r|acf1: ボラティリティ・クラスタリング。実市場=0.1〜0.3。荒れる時期と凪が交互に来る度合い。\n" +
            "racf1: リターン自体の自己相関。実市場≒0。大きいと値動きが読めてしまう。",
            MessageType.None);

        foreach (var result in _results)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var prev = GUI.color;
                GUI.color = result.Color;
                EditorGUILayout.LabelField("■", GUILayout.Width(16));
                GUI.color = prev;

                EditorGUILayout.LabelField(result.Label, EditorStyles.boldLabel, GUILayout.Width(90));
                EditorGUILayout.LabelField(result.Stats.ToString());
            }

            if (result.InputTokens > 0)
            {
                EditorGUILayout.LabelField(
                    $"　 {result.Requests} リクエスト / 入力 {result.InputTokens:N0} tokens / " +
                    $"概算 ${JevApi.EstimateCostUsd(result.InputTokens):F5}",
                    EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.Space();
        if (_itemIds.Length > 0)
        {
            _chartItemIndex = EditorGUILayout.Popup("チャート表示する銘柄", _chartItemIndex, _itemIds);
            _chartItemIndex = Mathf.Clamp(_chartItemIndex, 0, _itemIds.Length - 1);
            DrawChart(_itemIds[_chartItemIndex]);
        }
    }

    private void DrawChart(string itemId)
    {
        var rect = GUILayoutUtility.GetRect(10, 10000, 220, 220);
        EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.15f));

        var series = _results
            .Where(r => r.Series.ContainsKey(itemId) && r.Series[itemId].Count > 1)
            .ToList();
        if (series.Count == 0) return;

        float min = float.MaxValue, max = float.MinValue;
        foreach (var r in series)
        {
            foreach (int p in r.Series[itemId])
            {
                if (p < min) min = p;
                if (p > max) max = p;
            }
        }
        if (Mathf.Approximately(min, max)) { min -= 1f; max += 1f; }

        Handles.BeginGUI();
        foreach (var r in series)
        {
            var prices = r.Series[itemId];
            var points = new Vector3[prices.Count];
            for (int i = 0; i < prices.Count; i++)
            {
                float x = rect.x + rect.width * i / (prices.Count - 1f);
                float y = rect.yMax - rect.height * (prices[i] - min) / (max - min);
                points[i] = new Vector3(x, y, 0f);
            }
            Handles.color = r.Color;
            Handles.DrawAAPolyLine(2f, points);
        }
        Handles.EndGUI();

        GUI.Label(new Rect(rect.x + 4, rect.y + 2, 120, 16),
            max.ToString("F0", CultureInfo.InvariantCulture) + "G", EditorStyles.miniLabel);
        GUI.Label(new Rect(rect.x + 4, rect.yMax - 16, 120, 16),
            min.ToString("F0", CultureInfo.InvariantCulture) + "G", EditorStyles.miniLabel);
    }

    // ============================================================
    // 銘柄の準備
    // ============================================================
    /// <summary>
    /// 実際の ItemData アセットから銘柄を作る。見つからない場合は合成データで代替する。
    /// </summary>
    private List<SimItem> BuildItems(System.Random rng)
    {
        var items = new List<SimItem>();

        // 存在しないフォルダを FindAssets へ渡すと例外になるため、先に検証する
        string[] guids = AssetDatabase.IsValidFolder(ItemDataFolder)
            ? AssetDatabase.FindAssets("t:ItemData", new[] { ItemDataFolder })
            : null;
        if (guids == null || guids.Length == 0)
        {
            guids = AssetDatabase.FindAssets("t:ItemData");
        }

        foreach (var guid in guids)
        {
            if (items.Count >= _itemCount) break;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (data == null || data.basePrice <= 0) continue;

            items.Add(new SimItem
            {
                Id = string.IsNullOrEmpty(data.itemId) ? Path.GetFileNameWithoutExtension(path) : data.itemId,
                Name = string.IsNullOrEmpty(data.itemName) ? data.name : data.itemName,
                Category = data.itemType.ToString(),
                Element = data.itemAttribute.ToString(),
                Base = data.basePrice,
                Price = data.basePrice,
                StockValue = Mathf.Max(1, data.initialStock),
                Displayed = true,
            });
        }

        // アセットが足りなければ合成銘柄で埋める
        while (items.Count < _itemCount)
        {
            int n = items.Count;
            items.Add(new SimItem
            {
                Id = "synthetic_" + n,
                Name = "Synthetic Item " + n,
                Category = "Weapon",
                Element = "Fire",
                Base = 1000,
                Price = 1000,
                StockValue = 5,
                Displayed = true,
            });
        }

        foreach (var item in items)
        {
            item.DemandValue = 0.5f;
            item.PrevDemandValue = 0.5f;
            item.Trend = (float)(rng.NextDouble() - 0.5);
            item.History.Add(item.Price);
        }
        return items;
    }

    /// <summary>
    /// 需要・流行度の更新。全エンジンで完全に同一（差分を価格モデルだけに限定するため）。
    /// 現行 ItemModel.ApplyShopTurnEconomy のロジックをそのまま移植している。
    /// </summary>
    private static void AdvanceDemand(SimItem item, System.Random rng)
    {
        item.PrevDemandValue = item.DemandValue;

        float drift = (float)(rng.NextDouble() * 2.0 - 1.0) * TrendDriftMax;
        item.Trend = Mathf.Clamp(item.Trend + drift - item.Trend * TrendDecayRate, -1f, 1f);

        float natural = Mathf.Clamp01(0.5f + item.Trend * TrendAmplitude);
        float convergence = (natural - item.DemandValue) * TrendConvergenceRate;
        float display = item.Displayed ? DisplayDemandUp : -NotDisplayDemandDown;

        item.DemandValue = Mathf.Clamp(item.DemandValue + convergence + display, DemandFloor, DemandCeiling);
    }

    private static void ApplyPrice(SimItem item, float rate)
    {
        if (float.IsNaN(rate) || float.IsInfinity(rate)) rate = 1f;

        int price = Mathf.Max(1, Mathf.RoundToInt(item.Price * rate));
        int floor = Mathf.Max(1, Mathf.RoundToInt(item.Base * PriceFloorRate));
        int ceiling = Mathf.Max(floor, Mathf.RoundToInt(item.Base * PriceCeilingRate));

        item.Price = Mathf.Clamp(price, floor, ceiling);
        item.History.Add(item.Price);
    }

    private static RunResult Finish(string label, Color color, List<SimItem> items)
    {
        var result = new RunResult { Label = label, Color = color };
        var stats = new List<MarketStats>(items.Count);
        foreach (var item in items)
        {
            result.Series[item.Id] = new List<int>(item.History);
            stats.Add(MarketStatistics.Compute(item.History));
        }
        result.Stats = MarketStatistics.Aggregate(stats);
        return result;
    }

    // ============================================================
    // エンジン: Legacy
    // ============================================================
    private RunResult RunLegacy()
    {
        var rng = new System.Random(_seed);
        var items = BuildItems(rng);

        for (int turn = 0; turn < _turns; turn++)
        {
            foreach (var item in items)
            {
                AdvanceDemand(item, rng);

                float min, max;
                if (item.DemandValue >= HighDemandThreshold) { min = HighRateMin; max = HighRateMax; }
                else if (item.DemandValue <= LowDemandThreshold) { min = LowRateMin; max = LowRateMax; }
                else { min = NormalRateMin; max = NormalRateMax; }

                float rate = min + (float)rng.NextDouble() * (max - min);
                ApplyPrice(item, rate);
            }
        }
        return Finish("Legacy", new Color(0.65f, 0.65f, 0.65f), items);
    }

    // ============================================================
    // エンジン: Local ABM
    // ============================================================
    private RunResult RunLocalAbm(LocalAbmSettings settings, string label)
    {
        var rng = new System.Random(_seed);
        var items = BuildItems(rng);
        var market = new LocalAbmMarket(settings, _seed);

        for (int turn = 0; turn < _turns; turn++)
        {
            foreach (var item in items)
            {
                AdvanceDemand(item, rng);
                float rate = market.Step(item);
                item.LastNet = market.LastNetOrder;
                ApplyPrice(item, rate);
            }
        }
        return Finish(label, new Color(0.3f, 0.8f, 1f), items);
    }

    // ============================================================
    // エンジン: Jev
    // ============================================================
    private async Task<RunResult> RunJevAsync(CancellationToken ct)
    {
        var rng = new System.Random(_seed);
        var items = BuildItems(rng);
        var personas = JevTraderRoster.CreateDefault(
            _jevTraderCount, _seed,
            _abm.capitalParetoAlpha, _abm.capitalMin, _abm.capitalMax);

        var result = new RunResult { Label = "Jev", Color = new Color(1f, 0.65f, 0.2f) };
        var itemIds = items.Select(i => i.Id).ToList();

        for (int turn = 0; turn < _turns; turn++)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var item in items) AdvanceDemand(item, rng);

            var state = BuildState(turn, items);
            var request = JevTraderRoster.BuildRequest(state, personas);

            var response = await JevApi.SendAsync(request, ct);
            result.Requests++;
            if (response?.Usage != null) result.InputTokens += response.Usage.InputTokens;

            var netOrders = JevTraderRoster.ToNetOrders(response, personas, itemIds);
            foreach (var item in items)
            {
                float net = netOrders.TryGetValue(item.Id, out var v) ? v : 0f;
                item.LastNet = net;
                float depth = OrderFlowPriceEngine.Depth(item.StockValue, item.DemandValue, _abm.baseDepth);
                ApplyPrice(item, OrderFlowPriceEngine.ToPriceRate(net, depth, _abm.lambda));
            }

            _status = $"Jev 実行中… {turn + 1}/{_turns} ターン（入力 {result.InputTokens:N0} tokens）";
            Repaint();
        }

        var stats = new List<MarketStats>(items.Count);
        foreach (var item in items)
        {
            result.Series[item.Id] = new List<int>(item.History);
            stats.Add(MarketStatistics.Compute(item.History));
        }
        result.Stats = MarketStatistics.Aggregate(stats);
        return result;
    }

    private static JevMarketState BuildState(int turn, List<SimItem> items)
    {
        var state = new JevMarketState { Turn = turn };
        foreach (var item in items)
        {
            // 直近8ターン分だけ渡す。全履歴を送るとトークンが無駄に膨らむ。
            int take = Mathf.Min(8, item.History.Count);
            state.Market.Add(new JevMarketItem
            {
                Id = item.Id,
                Name = item.Name,
                Category = item.Category,
                Element = item.Element,
                Price = item.Price,
                BasePrice = item.Base,
                RecentPrices = item.History.GetRange(item.History.Count - take, take),
                Demand = Mathf.Round(item.DemandValue * 100f) / 100f,
                DemandChange = Mathf.Round((item.DemandValue - item.PrevDemandValue) * 100f) / 100f,
                Displayed = item.Displayed,
                Stock = item.StockValue,
            });
        }
        return state;
    }

    // ============================================================
    // 実行フロー
    // ============================================================
    private async void RunAsync()
    {
        _running = true;
        _results.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            if (_runLegacy)
            {
                _status = "Legacy 実行中…";
                Repaint();
                _results.Add(RunLegacy());
            }

            if (_runLocalAbm)
            {
                _status = "Local ABM 実行中…";
                Repaint();
                _results.Add(RunLocalAbm(_abm, "Local ABM"));
            }

            if (_runJev)
            {
                if (!JevApi.HasApiKey)
                {
                    throw new InvalidOperationException(
                        "環境変数 TYPESAFE_API_KEY が未設定です。設定後に Unity を再起動してください。");
                }
                _results.Add(await RunJevAsync(_cts.Token));
            }

            _itemIds = _results.Count > 0 ? _results[0].Series.Keys.ToArray() : Array.Empty<string>();
            _chartItemIndex = 0;
            _status = $"完了（{_results.Count} エンジン / {_turns} ターン）";
        }
        catch (OperationCanceledException)
        {
            _status = "中止しました";
        }
        catch (Exception ex)
        {
            _status = "エラー: " + ex.Message;
            Debug.LogException(ex);
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
            Repaint();
        }
    }

    /// <summary>
    /// トレーダー数を変えて尖度がどう変化するかを実測する。
    /// 中心極限定理により、人数を増やすほど尖度は 3（正規分布）へ近づくはず。
    /// </summary>
    private void RunTraderCountSweep()
    {
        _running = true;
        _results.Clear();
        try
        {
            int[] counts = { 10, 30, 100, 300 };
            var colors = new[]
            {
                new Color(0.4f, 1f, 0.5f), new Color(0.3f, 0.8f, 1f),
                new Color(1f, 0.85f, 0.3f), new Color(1f, 0.45f, 0.45f),
            };

            for (int i = 0; i < counts.Length; i++)
            {
                var settings = _abm.Clone();
                settings.traderCount = counts[i];
                var run = RunLocalAbm(settings, counts[i] + "人");
                run.Color = colors[i];
                _results.Add(run);
            }

            _itemIds = _results[0].Series.Keys.ToArray();
            _chartItemIndex = 0;
            _status = "人数スイープ完了（kurt が 3 に近づくほど「つまらない相場」）";
        }
        catch (Exception ex)
        {
            _status = "エラー: " + ex.Message;
            Debug.LogException(ex);
        }
        finally
        {
            _running = false;
            Repaint();
        }
    }

    // ============================================================
    // CSV 出力
    // ============================================================
    private void ExportCsv()
    {
        // Assets の外に出す（Unity にインポートさせない／ビルドに混ぜないため）
        string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
        string dir = Path.Combine(root, "MarketSim");
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, $"marketsim_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var sb = new StringBuilder();

        sb.AppendLine("engine,item,turn,price");
        foreach (var result in _results)
        {
            foreach (var kv in result.Series)
            {
                for (int turn = 0; turn < kv.Value.Count; turn++)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3}", result.Label, kv.Key, turn, kv.Value[turn]));
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("engine,samples,stddev,kurtosis,abs_autocorr1,autocorr1,max_drawdown");
        foreach (var result in _results)
        {
            var s = result.Stats;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6}",
                result.Label, s.SampleCount, s.StdDevReturn, s.Kurtosis,
                s.AbsReturnAutocorr1, s.ReturnAutocorr1, s.MaxDrawdown));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        _status = "CSV出力: " + path;
        EditorUtility.RevealInFinder(path);
    }
}
