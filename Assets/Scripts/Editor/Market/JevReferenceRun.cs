using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// =====================================================================
// Jev を「教師」として価格系列（お手本）を生成する再利用可能なランナー。
//
// ローカルABM のキャリブレーションは
//   1. ここで Jev のお手本系列を作る
//   2. その統計量（MarketStats）に ABM のパラメータを合わせ込む
// という手順で行う。手順1をいつでも再現できるようにこのクラスへ切り出す。
//
// 需要（Demand / Trend）の更新式は JevMarketSimulator と完全に同一。
// 差分が「価格の決まり方」だけに限定されるようにしてある。
// =====================================================================

/// <summary>Jev 参照ランの設定。</summary>
public sealed class JevReferenceConfig
{
    public int Turns = 40;
    public int ItemCount = 8;
    public int TraderCount = 30;
    public int Seed = 1001;

    /// <summary>価格インパクト係数。ボラティリティ水準を決める主要ツマミ。</summary>
    public float Lambda = 0.20f;

    public float BaseDepth = 200f;

    /// <summary>ペルソナ比率。null なら <see cref="JevTraderRoster.DefaultArchetypeWeights"/>。</summary>
    public float[] ArchetypeWeights;

    public float ParetoAlpha = 1.3f;
    public float CapitalMin = 30f;
    public float CapitalMax = 2000f;
}

/// <summary>Jev 参照ランの結果。</summary>
public sealed class JevReferenceResult
{
    /// <summary>銘柄ID → 価格系列（先頭が初期価格）。</summary>
    public Dictionary<string, List<int>> Series = new Dictionary<string, List<int>>();

    /// <summary>全銘柄を束ねた統計量。</summary>
    public MarketStats Stats;

    public long InputTokens;
    public int Requests;

    public double CostUsd => JevApi.EstimateCostUsd(InputTokens);
}

public static class JevReferenceRun
{
    // JevMarketSimulator と同じ需要モデル定数
    private const float TrendAmplitude = 0.30f;
    private const float TrendConvergenceRate = 0.15f;
    private const float TrendDriftMax = 0.12f;
    private const float TrendDecayRate = 0.10f;
    private const float DemandFloor = 0.05f, DemandCeiling = 1.0f;
    private const float DisplayDemandUp = 0.02f;
    private const float PriceFloorRate = 0.3f, PriceCeilingRate = 3.0f;

    /// <summary>
    /// 実際の ItemData アセットを使って Jev 参照ランを1回実行する。
    /// </summary>
    /// <remarks>
    /// 重要: 最初の await 以降はスレッドプール上で動く（ConfigureAwait(false)）。
    /// ItemData の読み込みは await より前に済ませてあるので AssetDatabase 競合は起きないが、
    /// <paramref name="onProgress"/> は**メインスレッド外**で呼ばれる。
    /// UI を触るならコールバック側で SynchronizationContext へ戻すこと。
    ///
    /// ConfigureAwait(false) は必須。これが無いと継続がメインスレッドへ戻ろうとするため、
    /// <see cref="Execute"/> のように結果を同期待ちした瞬間に Unity がデッドロックする。
    /// </remarks>
    /// <param name="config">ラン設定。</param>
    /// <param name="ct">キャンセル用。</param>
    /// <param name="onProgress">進捗通知（完了ターン数, 総ターン数）。メインスレッド外。null 可。</param>
    public static async Task<JevReferenceResult> ExecuteAsync(
        JevReferenceConfig config, CancellationToken ct = default, Action<int, int> onProgress = null)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        int n = Mathf.Max(1, config.ItemCount);
        var items = LoadItems(n);
        if (items.Count < n)
        {
            throw new InvalidOperationException($"ItemData が不足しています（要求 {n} / 実際 {items.Count}）。");
        }

        var rng = new System.Random(config.Seed);
        foreach (var item in items)
        {
            item.Demand = 0.5f;
            item.PrevDemand = 0.5f;
            item.Trend = (float)(rng.NextDouble() - 0.5);
            item.History.Add(item.Price);
        }

        var personas = JevTraderRoster.CreateDefault(
            config.TraderCount, config.Seed,
            config.ParetoAlpha, config.CapitalMin, config.CapitalMax,
            config.ArchetypeWeights);

        var ids = new List<string>(items.Count);
        foreach (var item in items) ids.Add(item.Id);

        var result = new JevReferenceResult();

        for (int turn = 0; turn < config.Turns; turn++)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var item in items) AdvanceDemand(item, rng);

            var request = JevTraderRoster.BuildRequest(BuildState(turn, items), personas);
            var response = await JevApi.SendAsync(request, ct).ConfigureAwait(false);

            result.Requests++;
            if (response?.Usage != null) result.InputTokens += response.Usage.InputTokens;

            var netOrders = JevTraderRoster.ToNetOrders(response, personas, ids);
            foreach (var item in items)
            {
                float net = netOrders.TryGetValue(item.Id, out var v) ? v : 0f;
                float depth = OrderFlowPriceEngine.Depth(item.Stock, item.Demand, config.BaseDepth);
                ApplyPrice(item, OrderFlowPriceEngine.ToPriceRate(net, depth, config.Lambda));
            }

            onProgress?.Invoke(turn + 1, config.Turns);
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

    /// <summary>
    /// <see cref="ExecuteAsync"/> を同期的に実行する。Editor スクリプトや実験コードから使う。
    /// ExecuteAsync が ConfigureAwait(false) で組まれているため、メインスレッドから
    /// 呼んでもデッドロックしない（ただし完了までエディタは固まる）。
    /// </summary>
    public static JevReferenceResult Execute(JevReferenceConfig config, CancellationToken ct = default)
    {
        return ExecuteAsync(config, ct).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 複数の設定を順に実行し、結果をテキストファイルへ書き出すバックグラウンドジョブを開始する。
    /// 呼び出しは即座に戻る。
    ///
    /// Unity Pipeline の eval はメインスレッドを5秒までしか占有できないため、
    /// 長時間の Jev ランはこの形で投げてファイルをポーリングする。
    /// 銘柄テンプレートは呼び出し時（メインスレッド）にキャッシュされる。
    /// </summary>
    /// <param name="configs">実行する設定の列。</param>
    /// <param name="outputPath">結果の書き出し先。実行中は "RUNNING" 行から始まる。</param>
    /// <param name="labels">各設定の見出し。null なら連番。</param>
    public static void ExecuteSweepToFile(
        IReadOnlyList<JevReferenceConfig> configs, string outputPath, IReadOnlyList<string> labels = null)
    {
        if (configs == null || configs.Count == 0) throw new ArgumentException("configs が空です。");
        if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("outputPath が空です。");

        // メインスレッドのうちに AssetDatabase から銘柄を読んでおく
        int maxItems = 1;
        foreach (var c in configs) maxItems = Mathf.Max(maxItems, c.ItemCount);
        PrewarmItems(maxItems);

        var dir = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(outputPath, "RUNNING\n");

        Task.Run(async () =>
        {
            var sb = new System.Text.StringBuilder();
            long totalTokens = 0;
            try
            {
                for (int i = 0; i < configs.Count; i++)
                {
                    var label = (labels != null && i < labels.Count) ? labels[i] : ("run" + i);
                    var result = await ExecuteAsync(configs[i]).ConfigureAwait(false);
                    totalTokens += result.InputTokens;
                    sb.AppendLine(label + " | " + result.Stats);
                }
                sb.AppendLine("TOKENS " + totalTokens);
                sb.AppendLine("COST " + JevApi.EstimateCostUsd(totalTokens).ToString("F4"));
                sb.AppendLine("DONE");
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR " + ex.GetType().Name + ": " + ex.Message);
                sb.AppendLine("DONE");
            }

            try { System.IO.File.WriteAllText(outputPath, sb.ToString()); }
            catch { /* 書き出し失敗は握りつぶす（ポーリング側がタイムアウトで気付く） */ }
        });
    }

    // ------------------------------------------------------------
    private sealed class RefItem
    {
        public string Id, Name, Category, Element;
        public int BasePrice, Price, Stock;
        public float Demand, PrevDemand, Trend;
        public readonly List<int> History = new List<int>();
    }

    // AssetDatabase はメインスレッドからしか触れないため、銘柄テンプレートを事前に読んで保持する。
    // これにより ExecuteAsync 全体をスレッドプール上で走らせられる。
    private static List<RefItem> _templateCache;
    private static int _templateCacheCount = -1;

    /// <summary>
    /// 銘柄テンプレートをメインスレッドで読み込んでキャッシュする。
    /// バックグラウンドで <see cref="ExecuteAsync"/> を回す前に、必ずメインスレッドから呼ぶこと。
    /// </summary>
    public static void PrewarmItems(int count)
    {
        count = Mathf.Max(1, count);
        if (_templateCache != null && _templateCacheCount >= count) return;

        var list = new List<RefItem>(count);
        var guids = AssetDatabase.FindAssets("t:ItemData");
        if (guids != null)
        {
            foreach (var guid in guids)
            {
                if (list.Count >= count) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (data == null || data.basePrice <= 0) continue;

                list.Add(new RefItem
                {
                    Id = string.IsNullOrEmpty(data.itemId) ? data.name : data.itemId,
                    Name = string.IsNullOrEmpty(data.itemName) ? data.name : data.itemName,
                    Category = data.itemType.ToString(),
                    Element = data.itemAttribute.ToString(),
                    BasePrice = data.basePrice,
                    Stock = Mathf.Max(1, data.initialStock),
                });
            }
        }
        _templateCache = list;
        _templateCacheCount = count;
    }

    private static List<RefItem> LoadItems(int count)
    {
        PrewarmItems(count);

        var list = new List<RefItem>(count);
        foreach (var template in _templateCache)
        {
            if (list.Count >= count) break;
            list.Add(new RefItem
            {
                Id = template.Id,
                Name = template.Name,
                Category = template.Category,
                Element = template.Element,
                BasePrice = template.BasePrice,
                Price = template.BasePrice,
                Stock = template.Stock,
            });
        }
        return list;
    }

    private static void AdvanceDemand(RefItem item, System.Random rng)
    {
        item.PrevDemand = item.Demand;

        float drift = (float)(rng.NextDouble() * 2.0 - 1.0) * TrendDriftMax;
        item.Trend = Mathf.Clamp(item.Trend + drift - item.Trend * TrendDecayRate, -1f, 1f);

        float natural = Mathf.Clamp01(0.5f + item.Trend * TrendAmplitude);
        item.Demand = Mathf.Clamp(
            item.Demand + (natural - item.Demand) * TrendConvergenceRate + DisplayDemandUp,
            DemandFloor, DemandCeiling);
    }

    private static void ApplyPrice(RefItem item, float rate)
    {
        if (float.IsNaN(rate) || float.IsInfinity(rate)) rate = 1f;

        int price = Mathf.Max(1, Mathf.RoundToInt(item.Price * rate));
        int floor = Mathf.Max(1, Mathf.RoundToInt(item.BasePrice * PriceFloorRate));
        int ceiling = Mathf.Max(floor, Mathf.RoundToInt(item.BasePrice * PriceCeilingRate));

        item.Price = Mathf.Clamp(price, floor, ceiling);
        item.History.Add(item.Price);
    }

    private static JevMarketState BuildState(int turn, List<RefItem> items)
    {
        var state = new JevMarketState { Turn = turn };
        foreach (var item in items)
        {
            int take = Mathf.Min(8, item.History.Count);
            state.Market.Add(new JevMarketItem
            {
                Id = item.Id,
                Name = item.Name,
                Category = item.Category,
                Element = item.Element,
                Price = item.Price,
                BasePrice = item.BasePrice,
                RecentPrices = item.History.GetRange(item.History.Count - take, take),
                Demand = Mathf.Round(item.Demand * 100f) / 100f,
                DemandChange = Mathf.Round((item.Demand - item.PrevDemand) * 100f) / 100f,
                Displayed = true,
                Stock = item.Stock,
            });
        }
        return state;
    }
}
