using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 1ランぶんの振り返り用の記録（リザルト画面の所持金グラフ・ハイライト用）。
/// ラン内セーブ（runHistory.json）として選択中スロットに保存され、
/// RunSaveCleaner.DeleteRunFiles でラン終了・ニューゲーム時に消える。
/// </summary>
[Serializable]
public class RunHistoryData
{
    /// <summary>所持金を記録したターン（moneyValues と同じ長さ）</summary>
    public List<int> moneyTurns = new();
    /// <summary>各ターン終了時点の所持金</summary>
    public List<int> moneyValues = new();

    public int streamWins;
    public int streamLosses;
    public int buzzCount;

    /// <summary>商品別の売上累計（itemIds / itemNames / itemRevenues は同じ長さ）</summary>
    public List<string> itemIds = new();
    public List<string> itemNames = new();
    public List<int> itemRevenues = new();

    // --- ラン開始時の元手（儲け = 純資産 − 初期資金 − 持ち込み − 立て替え の計算用）。旧セーブは欠損→ false/0 ---
    /// <summary>RecordRunStart で記録したか（false = このデータより前に始まったラン）。</summary>
    public bool startRecorded;
    /// <summary>村資金から持ち込んだ額。</summary>
    public int carriedGold;
    /// <summary>ラン開始時点で、チュートリアルの立て替え（返済不要の資金）をすでに受け取っていたか。</summary>
    public bool advanceGrantedBefore;

    // --- 配信の成績（リザルトの振り返り用）。旧セーブは欠損→0 ---
    /// <summary>配信の収支（TotalEarnings）の合計。</summary>
    public int streamEarningsTotal;
    /// <summary>一番稼いだ配信の収支と行き先。</summary>
    public int bestStreamEarnings;
    public string bestStreamDungeon = "";
    public bool hasBestStream;
    /// <summary>村の広場の効果で配信の売上に上乗せされた額の合計。</summary>
    public int streamBonusTotal;
}

/// <summary>
/// RunHistoryData の記録窓口。ゲーム側の各所から1行で呼べるように static にしている。
/// 記録の失敗でゲーム進行を止めないよう、すべて例外を握りつぶしてログだけ出す。
/// </summary>
public static class RunHistory
{
    public const string FileName = "runHistory.json";

    private static RunHistoryData _cache;
    private static string _cachePath;

    /// <summary>
    /// 現在のスロットの記録。ファイルが無ければ空から始める
    /// （DeleteRunFiles・スロット削除でファイルが消えたら、古いキャッシュは捨てる）。
    /// </summary>
    private static RunHistoryData Current
    {
        get
        {
            string path = SaveSlotManager.GetPath(FileName);
            if (!File.Exists(path))
            {
                _cache = new RunHistoryData();
                _cachePath = path;
                return _cache;
            }

            if (_cache == null || _cachePath != path)
            {
                _cache = ReadFile(path) ?? new RunHistoryData();
                _cachePath = path;
            }
            return _cache;
        }
    }

    /// <summary>リザルト表示用に読み出す（無ければ空のデータ）。</summary>
    public static RunHistoryData Load()
    {
        try { return Current; }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunHistory] 読み込みに失敗: {e.Message}");
            return new RunHistoryData();
        }
    }

    /// <summary>ターン終了時点の所持金を記録する。同じターンの再記録は上書き（イベント日はターンが進まないため）。</summary>
    public static void RecordMoney(int turn, int money)
    {
        Modify(d =>
        {
            int last = d.moneyTurns.Count - 1;
            if (last >= 0 && d.moneyTurns[last] == turn)
            {
                d.moneyValues[last] = money;
                return;
            }
            d.moneyTurns.Add(turn);
            d.moneyValues.Add(money);
        });
    }

    /// <summary>
    /// 配信1回ぶんの勝敗と、配信中に売れた商品の売上を記録する。
    /// 途中終了（Abort）は勝ちにも負けにも数えない（売上だけ記録する）。
    /// </summary>
    public static void RecordStreaming(BattleResult result, IEnumerable<BattleOutputSoldItem> soldItems)
    {
        Modify(d =>
        {
            if (result == BattleResult.Victory) d.streamWins++;
            else if (result == BattleResult.Defeat) d.streamLosses++;

            if (soldItems == null) return;
            foreach (var s in soldItems)
            {
                if (s == null) continue;
                // Revenue は実際に売れたときの価格の合計。未設定（AutoPlay の代理戦闘など）なら品出し時価格×個数
                int revenue = s.Revenue > 0 ? s.Revenue : s.SoldQuantity * s.SoldPrice;
                AddItemRevenue(d, s.ItemId, null, revenue);
            }
        });
    }

    /// <summary>売り注文の約定（店頭販売の入金）を商品別に記録する。</summary>
    public static void RecordSettlement(SellSettlementResult result)
    {
        if (result?.Settled == null || result.Settled.Count == 0) return;
        Modify(d =>
        {
            foreach (var s in result.Settled)
                AddItemRevenue(d, s.ItemId, s.ItemName, s.Income);
        });
    }

    /// <summary>バズが新しく発生した。</summary>
    public static void RecordBuzz() => Modify(d => d.buzzCount++);

    /// <summary>新規ランの開始時に元手を記録する（ニューゲームでファイルが消えた直後に呼ぶ）。</summary>
    public static void RecordRunStart(int carriedGold, bool advanceGrantedBefore)
    {
        Modify(d =>
        {
            d.startRecorded = true;
            d.carriedGold = UnityEngine.Mathf.Max(0, carriedGold);
            d.advanceGrantedBefore = advanceGrantedBefore;
        });
    }

    /// <summary>配信1回ぶんの収支を記録する（途中終了も含む）。bonus は村の広場による上乗せ分。</summary>
    public static void RecordStreamEarnings(int totalEarnings, string dungeonName, int bonus)
    {
        Modify(d =>
        {
            d.streamEarningsTotal += totalEarnings + bonus;
            d.streamBonusTotal += bonus;
            int net = totalEarnings + bonus;
            if (!d.hasBestStream || net > d.bestStreamEarnings)
            {
                d.hasBestStream = true;
                d.bestStreamEarnings = net;
                d.bestStreamDungeon = dungeonName ?? "";
            }
        });
    }

    // ------------------------------------------------------------

    private static void AddItemRevenue(RunHistoryData d, string itemId, string itemName, int revenue)
    {
        if (string.IsNullOrEmpty(itemId) || revenue == 0) return;
        int idx = d.itemIds.IndexOf(itemId);
        if (idx < 0)
        {
            d.itemIds.Add(itemId);
            d.itemNames.Add(itemName ?? "");
            d.itemRevenues.Add(revenue);
            return;
        }
        d.itemRevenues[idx] += revenue;
        if (string.IsNullOrEmpty(d.itemNames[idx]) && !string.IsNullOrEmpty(itemName))
            d.itemNames[idx] = itemName;
    }

    private static void Modify(Action<RunHistoryData> change)
    {
        try
        {
            var data = Current;
            change(data);
            SaveFile.WriteAtomic(_cachePath, JsonUtility.ToJson(data));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunHistory] 記録に失敗: {e.Message}");
        }
    }

    private static RunHistoryData ReadFile(string path)
    {
        try
        {
            if (!SaveFile.TryReadJson<RunHistoryData>(path, out var data)) return null;
            data.moneyTurns ??= new List<int>();
            data.moneyValues ??= new List<int>();
            data.itemIds ??= new List<string>();
            data.itemNames ??= new List<string>();
            data.itemRevenues ??= new List<int>();
            data.bestStreamDungeon ??= "";
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunHistory] 読み込みに失敗: {e.Message}");
            return null;
        }
    }
}
