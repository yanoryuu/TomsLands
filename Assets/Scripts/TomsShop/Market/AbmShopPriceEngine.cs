using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// エージェントベースモデル（ABM）による価格変動エンジン。
///
/// キャリブレーション済みの <see cref="MarketModelPreset"/> を読み、
/// 30体の仮想トレーダーの注文フローから価格変動率を決める。外部APIは使わない。
///
/// 広告ステータス補正は Legacy と同じ意味で後掛けする:
///   Attention … 上振れ側だけを増幅（下落方向は増幅しない）
///   Retention … 変動率を 1.0 へ引き寄せて安定化
/// </summary>
public sealed class AbmShopPriceEngine : IShopPriceEngine
{
    private readonly LocalAbmMarket _market;
    private readonly Dictionary<string, float> _lastNetOrders = new Dictionary<string, float>();
    private readonly View _view = new View();

    /// <summary>直近の Step で算出した純注文（デバッグ表示用）。</summary>
    public float LastNetOrder => _market.LastNetOrder;

    public AbmShopPriceEngine(LocalAbmSettings settings, int seed)
    {
        _market = new LocalAbmMarket(settings ?? new LocalAbmSettings(), seed);
    }

    public void BeginTurn() { }

    public float GetPriceRate(in ShopPriceContext context)
    {
        var item = context.Item;
        var master = context.Master;
        if (item == null || master == null) return 1f;

        _lastNetOrders.TryGetValue(item.ItemId, out float lastNet);
        _view.Bind(item, master, lastNet);

        float rate = _market.Step(_view);
        _lastNetOrders[item.ItemId] = _market.LastNetOrder;

        // 案A2 Attention: 上振れ側だけを増幅する。
        // Legacy は抽選レンジの上端を伸ばしていたが、ABM には抽選レンジが無いので
        // 「1.0 からの上方向の乖離」を同じ係数で伸ばすことで意味を揃える。
        if (rate > 1f && context.AttentionFactor > 1f)
        {
            rate = 1f + (rate - 1f) * context.AttentionFactor;
        }

        // 案A4 Retention: 常連が多いほど価格を安定させる（Legacy と同一式）
        rate = Mathf.Lerp(rate, 1f, context.RetentionStability);

        return (float.IsNaN(rate) || float.IsInfinity(rate)) ? 1f : rate;
    }

    /// <summary>
    /// <see cref="RuntimeItemData"/> を ABM が読める形へ変換する薄いアダプタ。
    /// 銘柄ごとに作り直さず、Bind で中身を差し替えて使い回す（毎ターン×銘柄数の GC を避ける）。
    /// </summary>
    private sealed class View : IMarketView
    {
        private RuntimeItemData _item;
        private ItemData _master;
        private float _lastNet;

        public void Bind(RuntimeItemData item, ItemData master, float lastNetOrder)
        {
            _item = item;
            _master = master;
            _lastNet = lastNetOrder;
        }

        public int CurrentPrice => _item.CurrentPrice.Value;
        public int BasePrice => _master.basePrice;
        public float Demand => _item.Demand.Value;
        public float PreviousDemand => _item.PreviousDemand;
        public int Stock => _item.Stock.Value;

        /// <summary>
        /// ショップ価格履歴。末尾が最新。ABM は直近数ターンしか見ないので、
        /// リングバッファ運用（上限 <see cref="RuntimeItemData.ShopHistoryCapacity"/>）で十分。
        /// </summary>
        public IReadOnlyList<int> PriceHistory => _item.ShopPriceHistory;

        public float LastNetOrder => _lastNet;
    }
}
