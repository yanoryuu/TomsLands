using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

/// <summary>
/// ラン間で持ち越すメタ進行（スロット=プロフィール単位、slot_N/metaData.json）。
/// - 村資金 VillageFunds: ラン終了時に「儲け」から変換されて貯まる唯一のメタ通貨（ConvertRunToVillageFunds）。
///   村の施設投資・次の出店への持ち込み（上限=銀行レベル）・スタートダッシュ購入すべてに使う
/// - 周回統計（総ラン数・クリア数・ベスト記録・直近のラン履歴）
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

    /// <summary>直近のラン履歴の保存件数。</summary>
    public const int MaxRunRecords = 20;

    /// <summary>難易度ごとの最高の儲け（index = GameModeId。クリアしたランのみ。未記録は int.MinValue）。</summary>
    private readonly List<int> bestProfits = new();

    /// <summary>直近のラン履歴（古い順。最大 MaxRunRecords 件）。</summary>
    private readonly List<RunRecordPlain> runRecords = new();
    public IReadOnlyList<RunRecordPlain> RunRecords => runRecords;

    /// <summary>村資金(G)。ラン終了時に純資産から変換されて貯まり、村の施設投資に使う。</summary>
    public int VillageFunds { get; private set; }

    /// <summary>村施設のレベル（facilityIdキー。JsonUtility対応のためList保持）。</summary>
    private readonly List<FacilityLevelPlain> facilityLevels = new();

    /// <summary>会話チュートリアルの既読フラグ（tutorialIdキー。JsonUtility対応のためList保持）。</summary>
    private readonly List<TutorialFlagPlain> tutorialFlags = new();

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
    public void UpgradeCreditLine() => CreditLineLevel++;

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
    /// ラン終了時、そのランの儲けを村資金へ入れる（村と店の経営を繋ぐ唯一の橋・一方向）。
    /// 儲け = 純資産 − 初期資金 − 持ち込み − 立て替え（RunProfit.Compute）。
    /// クリア時: 持ち込み（儲けが赤字ならその分を引く）+ 完走ボーナス（<see cref="ClearBonusBase"/>。領主館 Lv4 で +15%）+ max(0, 儲け) × 変換率 × 難易度倍率。
    /// 破産時: 持ち込みは戻らない。完走ボーナスは無い。
    /// 「最高の儲け × 変換率 × 難易度倍率 × bankruptcyConversionRate」と救済「bankruptcyBaseFunds + 納付した額 × bankruptcyPaidDebtRate」の多い方。
    /// dueDebtCycles: クリア時はそのランで期日が来た納付の回数（DebtCalculator.CountDueCyclesOfClearedRun）、破産時は納付を済ませた回数。
    /// 変換額を返す（呼び出し側が VillageArrivalReport に載せて村の収支ポップに使う）。
    /// </summary>
    public int ConvertRunToVillageFunds(bool cleared, int profit, int carried, GameModeId mode, int dueDebtCycles)
    {
        int converted = CalculateVillageFunds(cleared, profit, carried, mode, dueDebtCycles);
        if (converted > 0) AddVillageFunds(converted);
        SaveData();
        Debug.Log($"[Meta] 村資金へ: +{converted}G (cleared={cleared}, 儲け={profit}G, 持ち込み={carried}G, {mode}, 納付{dueDebtCycles}回, 合計 {VillageFunds}G)");
        return converted;
    }

    /// <summary>
    /// 変換額の計算だけ（表示・自動プレイの見積もり用）。
    /// effects は村の施設の効果（領主館の上乗せ）。null なら VillageEffects.Current（テストでは固定のレベルを渡す）。
    /// </summary>
    public static int CalculateVillageFunds(bool cleared, int profit, int carried, GameModeId mode, int dueDebtCycles,
        VillageEffects effects = null)
    {
        var settings = GameConst.Village;
        float rate = Mathf.Max(0f, settings.conversionRate) * DifficultyMultiplier(settings, mode);
        carried = Mathf.Max(0, carried);
        if (!cleared)
        {
            float bankruptRate = Mathf.Max(0f, settings.bankruptcyConversionRate);
            int fromProfit = Mathf.FloorToInt(Mathf.Max(0, profit) * rate * bankruptRate);
            // 救済: 序盤で一度も儲からずに破産しても、土台と納めた分は村に残す（予定額で数える。完走ボーナスと同じ数え方）
            long relief = Mathf.Max(0, settings.bankruptcyBaseFunds)
                          + (long)(DebtCalculator.GetScheduledTotal(Mathf.Max(0, dueDebtCycles)) * Mathf.Max(0f, settings.bankruptcyPaidDebtRate));
            return (int)System.Math.Min(System.Math.Max(fromProfit, relief), int.MaxValue);
        }

        long total = Mathf.Max(0, carried + Mathf.Min(0, profit))
                     + (long)(effects ?? VillageEffects.Current).ApplyClearBonus(ClearBonusBase(dueDebtCycles))
                     + (long)Mathf.FloorToInt(Mathf.Max(0, profit) * rate);
        return (int)System.Math.Min(total, int.MaxValue);
    }

    /// <summary>
    /// 完走ボーナスの土台（領主館の上乗せ前）= clearBaseFlat + clearDebtMultiplier × 1〜dueDebtCycles 回目の納付の予定額の合計。
    /// 実際に払った額（減免状・初回の割引・借入・立て替え・レリック補正）ではなく、GameConst.GetDebtAmount の予定額で数える。
    /// 長く走って多く納めたランほど多く入り、納付が1回も無い短いランでも clearBaseFlat（Lv1 を1つ建てられる額）は入る。
    /// </summary>
    public static int ClearBonusBase(int dueDebtCycles)
    {
        var settings = GameConst.Village;
        long scheduled = DebtCalculator.GetScheduledTotal(dueDebtCycles);
        long bonus = Mathf.Max(0, settings.clearBaseFlat)
                     + (long)System.Math.Round(scheduled * (double)Mathf.Max(0f, settings.clearDebtMultiplier));
        return (int)System.Math.Min(bonus, int.MaxValue);
    }

    private static float DifficultyMultiplier(VillageSettingsData settings, GameModeId mode)
    {
        var table = settings.difficultyConversionMultipliers;
        int i = (int)mode;
        return table != null && i >= 0 && i < table.Length ? Mathf.Max(0f, table[i]) : 1f;
    }

    /// <summary>この難易度で記録した最高の儲け（クリアしたランのみ）。未記録なら false。</summary>
    public bool TryGetBestProfit(GameModeId mode, out int best)
    {
        int i = (int)mode;
        best = 0;
        if (i < 0 || i >= bestProfits.Count || bestProfits[i] == int.MinValue) return false;
        best = bestProfits[i];
        return true;
    }

    /// <summary>
    /// ラン終了時の記録（総ラン数・クリア数・ベスト・ラン履歴）。
    /// 旧仕組みのメタ通貨「信用」も従来どおり加算する（現在は未使用）。
    /// 自己ベスト（その難易度の最高の儲け）を更新したら true を返す（その難易度の初クリアは false）。
    /// </summary>
    public bool RecordRunEnd(RunRecordPlain record)
    {
        if (record == null) return false;
        var settings = GameConst.Preparation;

        TotalRuns++;
        if (record.cleared) ClearedRuns++;
        if (record.netWorth > BestNetWorth) BestNetWorth = record.netWorth;
        if (record.cleared && !string.IsNullOrEmpty(record.rank) && IsBetterRank(record.rank, BestRank))
            BestRank = record.rank;

        bool newBest = false;
        if (record.cleared)
        {
            int i = Mathf.Clamp(record.mode, 0, 2);
            while (bestProfits.Count <= i) bestProfits.Add(int.MinValue);
            if (bestProfits[i] == int.MinValue || record.profit > bestProfits[i])
            {
                newBest = bestProfits[i] != int.MinValue;
                bestProfits[i] = record.profit;
            }
        }

        record.runNo = TotalRuns;
        runRecords.Add(record);
        while (runRecords.Count > MaxRunRecords) runRecords.RemoveAt(0);

        int earned = Mathf.Max(0, record.turns) * settings.metaCurrencyPerTurn;
        if (record.cleared)
        {
            earned += Mathf.Max(0, record.netWorth) / Mathf.Max(1, settings.metaCurrencyDivisor);
            earned += RankBonus(record.rank, settings);
        }
        AddCurrency(earned);

        SaveData();
        Debug.Log($"[Meta] ラン終了を記録: #{record.runNo} cleared={record.cleared}, 儲け={record.profit}G, ランク={record.rank}, 自己ベスト更新={newBest}");
        return newBest;
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
            facilities = new List<FacilityLevelPlain>(facilityLevels),
            bestProfits = new List<int>(bestProfits),
            runRecords = new List<RunRecordPlain>(runRecords),
            tutorials = new List<TutorialFlagPlain>(tutorialFlags),
            firstBattleTurn = FirstBattleTurn,
            saveVersion = SaveVersion.Current,
        };
        // 何周ぶんもの積み上げなので、一時ファイル→置き換え→.bak で書く（途中で落ちても前の版が残る）
        SaveFile.WriteAtomic(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
    }

    public void LoadData()
    {
        string path = SaveSlotManager.GetPath(FileName);
        // 本体が壊れていれば .bak から復旧する。どちらも読めなければ「メタ進行なし」と同じ既定値にする
        // （壊れた本体は SaveFile が .corrupt に写して残す）
        if (!SaveFile.TryReadJson<MetaProgressData>(path, out var data))
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
            bestProfits.Clear();
            runRecords.Clear();
            FirstBattleTurn = 0;
            return;
        }

        SaveVersion.Migrate(data.saveVersion, FileName);
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

        bestProfits.Clear();
        if (data.bestProfits != null) bestProfits.AddRange(data.bestProfits);
        runRecords.Clear();
        if (data.runRecords != null)
        {
            foreach (var r in data.runRecords)
                if (r != null) runRecords.Add(r);
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

        // 村の施設を6つに統合した（2026-10）。削除した施設の分は統合先へ読み替え、読み替えられない分は返金する
        if (VillageFacilityMigration.Apply(facilityLevels, out int refund, out string log))
        {
            if (refund > 0) VillageFunds += refund;
            Debug.Log($"[Meta] 村の施設の統合: {log}（返金 {refund:N0}G、村資金 {VillageFunds:N0}G）");
            SaveData();
        }
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
    public List<FacilityLevelPlain> facilities = new();

    // --- 会話チュートリアル。旧セーブは欠損→空/0で正規化 ---
    public List<TutorialFlagPlain> tutorials = new();
    public int firstBattleTurn;

    // --- 記録（2026-10 追加）。旧セーブは欠損→空 ---
    /// <summary>難易度ごとの最高の儲け（index = GameModeId。int.MinValue = 未記録）。</summary>
    public List<int> bestProfits = new();
    /// <summary>直近のラン履歴（古い順）。</summary>
    public List<RunRecordPlain> runRecords = new();

    // --- セーブの版（SaveVersion.Current）。旧セーブは欠損→0 ---
    public int saveVersion;
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

/// <summary>ラン1回ぶんの記録（metaData.json の runRecords。JsonUtility 対応）。</summary>
[Serializable]
public class RunRecordPlain
{
    /// <summary>何周目か（RecordRunEnd が振る）。</summary>
    public int runNo;
    /// <summary>GameModeId を int で。</summary>
    public int mode;
    public bool cleared;
    /// <summary>儲け（純資産 − 初期資金 − 持ち込み − 立て替え）。破産時はそのランの最高の儲け。</summary>
    public int profit;
    public int netWorth;
    public string rank = "";
    public int turns;
    public int streamWins;
    public int streamLosses;
    /// <summary>配信の収支の合計（G）。</summary>
    public int streamEarnings;
    /// <summary>村資金へ入った額。</summary>
    public int villageFunds;
    /// <summary>終了日時（ローカル、yyyy/MM/dd HH:mm）。</summary>
    public string endedAt = "";
}
