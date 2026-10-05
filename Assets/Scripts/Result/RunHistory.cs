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

    /// <summary>配信1回ぶんの勝敗と、配信中に売れた商品の売上を記録する。</summary>
    public static void RecordStreaming(bool won, IEnumerable<BattleOutputSoldItem> soldItems)
    {
        Modify(d =>
        {
            if (won) d.streamWins++;
            else d.streamLosses++;

            if (soldItems == null) return;
            foreach (var s in soldItems)
            {
                if (s == null) continue;
                AddItemRevenue(d, s.ItemId, null, s.SoldQuantity * s.SoldPrice);
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
            File.WriteAllText(_cachePath, JsonUtility.ToJson(data));
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
            var data = JsonUtility.FromJson<RunHistoryData>(File.ReadAllText(path));
            if (data == null) return null;
            data.moneyTurns ??= new List<int>();
            data.moneyValues ??= new List<int>();
            data.itemIds ??= new List<string>();
            data.itemNames ??= new List<string>();
            data.itemRevenues ??= new List<int>();
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RunHistory] 読み込みに失敗: {e.Message}");
            return null;
        }
    }
}
