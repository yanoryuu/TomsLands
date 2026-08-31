using System;

[Serializable]
public class GameConstData
{
    // --- 上限値 ---
    public int maxDungeonLevel = 5;
    public int maxBlackSmithLevel = 5;
    public int maxToolShopLevel = 5;
    public int maxInfoBrokerLevel = 5;
    public int maxItemStock = 99;
    public int minItemStock = 0;

    // --- 所持金 ---
    public int initMoney = 10000;

    // --- 借金 ---
    // ※ サーバー配信(gameconst.json)で上書き可能。ベイク値は GameConstSettings.asset。
    public int debtPaymentInterval = 7;    // 何ターンごとに強制返済か
    public int debtBaseAmount = 5000;       // 1回目（cycle=1）の返済額
    public float debtMultiplier = 1.8f;     // 返済のたびに前回額へ掛ける倍率

    // --- ヒーロー経験値 ---
    public int heroExpPerMob = 10;
    public int heroExpPerBoss = 100;
    public int heroBaseExpToNextLevel = 100;

    // --- 鍛冶屋レベルアップコスト（index = 現在レベル → 次レベルへの費用） ---
    public int[] blackSmithLevelUpCosts = { 0, 3000, 6000, 12000, 20000 };

    // --- ゲームフロー自動生成（ローグライト） ---
    public GameFlowGenerationSettings flowGeneration = new GameFlowGenerationSettings();

    // --- レリック（装備アイテム） ---
    public RelicSettingsData relicSettings = new RelicSettingsData();

    // --- 準備シーン（メタ進行・借入レバレッジ・スタートダッシュ） ---
    public PreparationSettingsData preparation = new PreparationSettingsData();

    /// <summary>
    /// 配列まで含めた深いコピーを返す（ベイク済みアセットを実行時に汚染しないため）。
    /// </summary>
    public GameConstData Clone()
    {
        var clone = (GameConstData)MemberwiseClone();
        clone.blackSmithLevelUpCosts = blackSmithLevelUpCosts != null
            ? (int[])blackSmithLevelUpCosts.Clone()
            : Array.Empty<int>();
        clone.flowGeneration = flowGeneration != null
            ? flowGeneration.Clone()
            : new GameFlowGenerationSettings();
        clone.relicSettings = relicSettings != null
            ? relicSettings.Clone()
            : new RelicSettingsData();
        clone.preparation = preparation != null
            ? preparation.Clone()
            : new PreparationSettingsData();
        return clone;
    }
}

/// <summary>準備シーン（メタ進行）関連の設定。</summary>
[Serializable]
public class PreparationSettingsData
{
    /// <summary>借入の利率。借入額×(1+これ) が初回返済に上乗せされる。</summary>
    public float borrowInterestRate = 0.5f;
    /// <summary>借入枠の上限額（index = creditLineLevel）。</summary>
    public int[] creditLineAmounts = { 0, 5000, 10000, 20000 };
    /// <summary>借入枠拡張のメタ通貨コスト（index = 現在レベル → 次レベル）。</summary>
    public int[] creditLineUpgradeCosts = { 30, 80, 200 };
    /// <summary>持ち込みアイテムのスロット数（合計個数の上限）。</summary>
    public int baseCarrySlots = 2;

    // --- メタ通貨の獲得式 ---
    /// <summary>クリア時: floor(NetWorth / この値) を獲得。</summary>
    public int metaCurrencyDivisor = 5000;
    /// <summary>ランクボーナス（S/A/B/C/D）。</summary>
    public int[] rankBonuses = { 200, 120, 70, 40, 20 };
    /// <summary>到達ターン×この値を獲得（破産時もこれだけは入る）。</summary>
    public int metaCurrencyPerTurn = 2;

    // --- スタートダッシュ（メタ通貨コストと効果量） ---
    public int flyerCost = 20;
    public int flyerAttention = 20;
    public int flyerFollowers = 100;
    public int appraisalCost = 25;
    public float appraisalDemandBoost = 0.15f;
    public int graceCost = 30;
    /// <summary>返済猶予証: 初回返済額の割引率。</summary>
    public float graceDiscountRate = 0.3f;

    public PreparationSettingsData Clone()
    {
        var clone = (PreparationSettingsData)MemberwiseClone();
        clone.creditLineAmounts = creditLineAmounts != null ? (int[])creditLineAmounts.Clone() : Array.Empty<int>();
        clone.creditLineUpgradeCosts = creditLineUpgradeCosts != null ? (int[])creditLineUpgradeCosts.Clone() : Array.Empty<int>();
        clone.rankBonuses = rankBonuses != null ? (int[])rankBonuses.Clone() : Array.Empty<int>();
        return clone;
    }
}

/// <summary>レリック（装備アイテム）関連の設定。</summary>
[Serializable]
public class RelicSettingsData
{
    /// <summary>装備枠の上限。0 = 無制限（後からデータで絞れる）。</summary>
    public int maxEquipSlots = 0;
    /// <summary>配信勝利報酬の選択肢数。</summary>
    public int rewardChoiceCount = 3;
    /// <summary>レア度抽選の重み。</summary>
    public float commonWeight = 60f;
    public float rareWeight = 30f;
    public float epicWeight = 10f;
    /// <summary>3択を辞退したときの代わりのゴールド（選択肢の最高レア度で決まる）。</summary>
    public int declineGoldCommon = 500;
    public int declineGoldRare = 1200;
    public int declineGoldEpic = 2500;

    public RelicSettingsData Clone() => (RelicSettingsData)MemberwiseClone();
}
