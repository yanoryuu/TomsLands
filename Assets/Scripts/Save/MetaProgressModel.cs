using System;
using System.IO;
using R3;
using UnityEngine;

/// <summary>
/// ラン間で持ち越すメタ進行（スロット=プロフィール単位、slot_N/metaData.json）。
/// - 村資金 VillageFunds: ランクリア時に持ち帰った手元Gがそのまま貯まる唯一のメタ通貨。
///   村の施設投資・次の出店への持ち込み（上限=銀行レベル）・スタートダッシュ購入すべてに使う
/// - 周回統計（総ラン数・クリア数・ベスト記録）
/// - MetaCurrency/CreditLineLevel/bankedGold は旧仕組みの残置。現在は未使用
/// ※ RunSaveCleaner の削除対象に含めないこと（ラン内データではない）。
/// スロット削除（プロフィール削除）でのみ消える。
/// </summary>
public class MetaProgressModel
{
    private const string FileName = "metaData.json";

    /// <summary>【旧・未使用】メタ通貨「信用」。</summary>
    public ReactiveProperty<int> MetaCurrency { get; } = new(0);
    /// <summary>【旧・未使用】借入枠レベル。</summary>
    public int CreditLineLevel { get; private set; }
    public int TotalRuns { get; private set; }
    public int ClearedRuns { get; private set; }
    public int BestNetWorth { get; private set; }
    public string BestRank { get; private set; } = "";

    /// <summary>村資金(G)。ラン終了時に純資産から変換されて貯まり、村の施設投資に使う。</summary>
    public int VillageFunds { get; private set; }

    /// <summary>村施設のレベル（facilityIdキー。JsonUtility対応のためList保持）。</summary>
    private readonly System.Collections.Generic.List<FacilityLevelPlain> facilityLevels = new();

    /// <summary>会話チュートリアルの既読フラグ（tutorialIdキー。JsonUtility対応のためList保持）。</summary>
    private readonly System.Collections.Generic.List<TutorialFlagPlain> tutorialFlags = new();

    /// <summary>初めて配信（Battle）から帰還したターン番号。未経験なら0。</summary>
    public int FirstBattleTurn { get; private set; }

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

    /// <summary>【旧・未使用】借入枠レベルを1上げる。</summary>
    public void UpgradeCreditLine()
    {
        CreditLineLevel++;
    }

    // ========================================
    // 村（メタ層）
    // ========================================

    public void AddVillageFunds(int amount)
    {
        if (amount <= 0) return;
        VillageFunds += amount;
    }

    public bool TrySpendVillageFunds(int amount)
    {
        if (amount < 0 || VillageFunds < amount) return false;
        VillageFunds -= amount;
        return true;
    }

    public int GetFacilityLevel(string facilityId)
    {
        if (string.IsNullOrEmpty(facilityId)) return 0;
        foreach (var entry in facilityLevels)
        {
            if (entry.facilityId == facilityId) return entry.level;
        }
        return 0;
    }

    public void SetFacilityLevel(string facilityId, int level)
    {
        if (string.IsNullOrEmpty(facilityId)) return;
        foreach (var entry in facilityLevels)
        {
            if (entry.facilityId == facilityId)
            {
                entry.level = level;
                return;
            }
        }
        facilityLevels.Add(new FacilityLevelPlain { facilityId = facilityId, level = level });
    }

    // ========================================
    // 会話チュートリアル（ラン跨ぎで一度だけ再生する）
    // ========================================

    /// <summary>指定IDのチュートリアル会話を再生済みか。</summary>
    public bool HasSeenTutorial(string tutorialId)
    {
        if (string.IsNullOrEmpty(tutorialId)) return true;
        foreach (var entry in tutorialFlags)
        {
            if (entry.tutorialId == tutorialId) return entry.seen;
        }
        return false;
    }

    /// <summary>指定IDのチュートリアル会話を再生済みにして保存する。</summary>
    public void MarkTutorialSeen(string tutorialId)
    {
        if (string.IsNullOrEmpty(tutorialId)) return;
        foreach (var entry in tutorialFlags)
        {
            if (entry.tutorialId == tutorialId)
            {
                if (entry.seen) return;
                entry.seen = true;
                SaveData();
                return;
            }
        }
        tutorialFlags.Add(new TutorialFlagPlain { tutorialId = tutorialId, seen = true });
        SaveData();
    }

    /// <summary>初回の配信帰還ターンを記録する（既に記録済みなら何もしない）。</summary>
    public void RecordFirstBattleTurn(int turn)
    {
        if (FirstBattleTurn > 0 || turn <= 0) return;
        FirstBattleTurn = turn;
        SaveData();
    }

    /// <summary>
    /// ラン終了時、持ち帰ったお金を村資金へ入れる（村と店の経営を繋ぐ唯一の橋・一方向）。
    /// クリア時: 手元現金がそのまま全額 / 破産時: 手元現金 × bankruptcyConversionRate。
    /// 変換額を返す（呼び出し側が VillageArrivalReport に載せて村の収支ポップに使う）。
    /// </summary>
    public int ConvertRunToVillageFunds(bool cleared, int netWorth, int finalCash)
    {
        var settings = GameConst.Village;
        int converted = cleared
            ? Mathf.Max(0, finalCash)
            : Mathf.FloorToInt(Mathf.Max(0, finalCash) * settings.bankruptcyConversionRate);

        if (converted > 0) AddVillageFunds(converted);
        SaveData();
        Debug.Log($"[Meta] 村資金へ: +{converted}G (cleared={cleared}, 合計 {VillageFunds}G)");
        return converted;
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
            // bankedGold は村資金へ統合済み（旧セーブ読み込み時に移行）。常に0で保存する
            bankedGold = 0,
            metaCurrency = MetaCurrency.Value,
            creditLineLevel = CreditLineLevel,
            totalRuns = TotalRuns,
            clearedRuns = ClearedRuns,
            bestNetWorth = BestNetWorth,
            bestRank = BestRank,
            villageFunds = VillageFunds,
            facilities = new System.Collections.Generic.List<FacilityLevelPlain>(facilityLevels),
            tutorials = new System.Collections.Generic.List<TutorialFlagPlain>(tutorialFlags),
            firstBattleTurn = FirstBattleTurn,
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
            VillageFunds = 0;
            facilityLevels.Clear();
            tutorialFlags.Clear();
            FirstBattleTurn = 0;
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

        // 旧セーブ（フィールド欠損）は0/空で正規化
        VillageFunds = Mathf.Max(0, data.villageFunds);

        // 旧仕組みの銀行預金は村資金へ統合（移行。次のSaveDataでbankedGold=0が保存される）
        if (data.bankedGold > 0)
        {
            VillageFunds += data.bankedGold;
            Debug.Log($"[Meta] 旧・銀行預金 {data.bankedGold}G を村資金へ統合しました（村資金 {VillageFunds}G）");
        }
        facilityLevels.Clear();
        if (data.facilities != null)
        {
            foreach (var entry in data.facilities)
            {
                if (entry == null || string.IsNullOrEmpty(entry.facilityId) || entry.level <= 0) continue;
                facilityLevels.Add(entry);
            }
        }

        tutorialFlags.Clear();
        if (data.tutorials != null)
        {
            foreach (var entry in data.tutorials)
            {
                if (entry == null || string.IsNullOrEmpty(entry.tutorialId) || !entry.seen) continue;
                tutorialFlags.Add(entry);
            }
        }
        FirstBattleTurn = Mathf.Max(0, data.firstBattleTurn);
    }
}

[Serializable]
public class MetaProgressData
{
    /// <summary>【旧】銀行預金(G)。村資金へ統合済み（読み込み時に移行し、常に0で保存）。</summary>
    public int bankedGold;
    public int metaCurrency;
    public int creditLineLevel;
    public int totalRuns;
    public int clearedRuns;
    public int bestNetWorth;
    public string bestRank;

    // --- 村（メタ層）。旧セーブは欠損→0/空で正規化 ---
    public int villageFunds;
    public System.Collections.Generic.List<FacilityLevelPlain> facilities = new();

    // --- 会話チュートリアル。旧セーブは欠損→空/0で正規化 ---
    public System.Collections.Generic.List<TutorialFlagPlain> tutorials = new();
    public int firstBattleTurn;
}

/// <summary>チュートリアル会話1本ぶんの既読フラグ（JsonUtilityはDictionary不可のためList要素）。</summary>
[Serializable]
public class TutorialFlagPlain
{
    public string tutorialId;
    public bool seen;
}

/// <summary>村施設1つぶんのレベル保存（JsonUtilityはDictionary不可のためList要素）。</summary>
[Serializable]
public class FacilityLevelPlain
{
    public string facilityId;
    public int level;
}
