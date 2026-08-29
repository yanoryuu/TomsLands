using System;
using System.IO;
using R3;
using UnityEngine;

/// <summary>
/// ラン間で持ち越すメタ進行（スロット=プロフィール単位、slot_N/metaData.json）。
/// - メタ通貨「信用」: ラン終了時に成績から獲得し、準備シーンで借入枠拡張やスタートダッシュに使う
/// - creditLineLevel: 借入枠のレベル（準備シーンの借入レバレッジの上限を決める）
/// - 周回統計（総ラン数・クリア数・ベスト記録）
/// ※ RunSaveCleaner の削除対象に含めないこと（ラン内データではない）。
/// スロット削除（プロフィール削除）でのみ消える。
/// </summary>
public class MetaProgressModel
{
    private const string FileName = "metaData.json";

    public ReactiveProperty<int> MetaCurrency { get; } = new(0);
    public int CreditLineLevel { get; private set; }
    public int TotalRuns { get; private set; }
    public int ClearedRuns { get; private set; }
    public int BestNetWorth { get; private set; }
    public string BestRank { get; private set; } = "";

    public MetaProgressModel()
    {
        LoadData();
    }

    public void AddCurrency(int amount)
    {
        if (amount <= 0) return;
        MetaCurrency.Value += amount;
    }

    public bool TrySpend(int amount)
    {
        if (amount < 0 || MetaCurrency.Value < amount) return false;
        MetaCurrency.Value -= amount;
        return true;
    }

    /// <summary>借入枠レベルを1上げる（メタ通貨消費は呼び出し側で TrySpend 済みであること）。</summary>
    public void UpgradeCreditLine()
    {
        CreditLineLevel++;
    }

    /// <summary>
    /// ラン終了時の記録とメタ通貨の精算。
    /// 獲得式: floor(NetWorth/divisor) + ランクボーナス + 到達ターン×係数（破産時はターン分のみ）。
    /// </summary>
    public int RecordRunEnd(bool cleared, int netWorth, string rank, int totalTurns)
    {
        var settings = GameConst.Preparation;

        TotalRuns++;
        if (cleared) ClearedRuns++;
        if (netWorth > BestNetWorth) BestNetWorth = netWorth;
        if (cleared && !string.IsNullOrEmpty(rank) && IsBetterRank(rank, BestRank))
            BestRank = rank;

        int earned = Mathf.Max(0, totalTurns) * settings.metaCurrencyPerTurn;
        if (cleared)
        {
            earned += Mathf.Max(0, netWorth) / Mathf.Max(1, settings.metaCurrencyDivisor);
            earned += RankBonus(rank, settings);
        }

        AddCurrency(earned);
        SaveData();
        Debug.Log($"[Meta] ラン終了を記録: cleared={cleared}, 獲得メタ通貨={earned} (合計 {MetaCurrency.Value})");
        return earned;
    }

    private static bool IsBetterRank(string a, string b)
    {
        int RankOrder(string r) => r switch { "S" => 0, "A" => 1, "B" => 2, "C" => 3, "D" => 4, _ => 5 };
        if (string.IsNullOrEmpty(b)) return true;
        return RankOrder(a) < RankOrder(b);
    }

    private static int RankBonus(string rank, PreparationSettingsData settings)
    {
        var bonuses = settings.rankBonuses;
        if (bonuses == null || bonuses.Length < 5) return 0;
        return rank switch
        {
            "S" => bonuses[0],
            "A" => bonuses[1],
            "B" => bonuses[2],
            "C" => bonuses[3],
            "D" => bonuses[4],
            _ => 0,
        };
    }

    // ========================================
    // 永続化
    // ========================================

    public void SaveData()
    {
        var data = new MetaProgressData
        {
            metaCurrency = MetaCurrency.Value,
            creditLineLevel = CreditLineLevel,
            totalRuns = TotalRuns,
            clearedRuns = ClearedRuns,
            bestNetWorth = BestNetWorth,
            bestRank = BestRank,
        };
        File.WriteAllText(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
    }

    public void LoadData()
    {
        string path = SaveSlotManager.GetPath(FileName);
        if (!File.Exists(path))
        {
            MetaCurrency.Value = 0;
            CreditLineLevel = 0;
            TotalRuns = 0;
            ClearedRuns = 0;
            BestNetWorth = 0;
            BestRank = "";
            return;
        }

        var data = JsonUtility.FromJson<MetaProgressData>(File.ReadAllText(path));
        if (data == null) return;
        MetaCurrency.Value = Mathf.Max(0, data.metaCurrency);
        CreditLineLevel = Mathf.Max(0, data.creditLineLevel);
        TotalRuns = Mathf.Max(0, data.totalRuns);
        ClearedRuns = Mathf.Max(0, data.clearedRuns);
        BestNetWorth = Mathf.Max(0, data.bestNetWorth);
        BestRank = data.bestRank ?? "";
    }
}

[Serializable]
public class MetaProgressData
{
    public int metaCurrency;
    public int creditLineLevel;
    public int totalRuns;
    public int clearedRuns;
    public int bestNetWorth;
    public string bestRank;
}
