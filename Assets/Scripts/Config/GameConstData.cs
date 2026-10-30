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
    public int initMoney = 100000;

    // --- 借金 ---
    // ※ サーバー配信(gameconst.json)で上書き可能。ベイク値は GameConstSettings.asset。
    public int debtPaymentInterval = 7;    // 何ターンごとに強制返済か
    public int debtBaseAmount = 5000;       // 1回目（cycle=1）の返済額
    public float debtMultiplier = 1.8f;     // 返済のたびに前回額へ掛ける倍率
    // 強制返済で現金と金融資産を足しても届かないとき、在庫を相場のこの割合で投げ売りして充てる（H-8。0 = 投げ売りしない）
    public float debtFireSaleRate = 0.6f;

    // --- 情報屋の地図（ダンジョンの情報料） ---
    // 情報料 = mapInfoBaseCost × mapInfoDebtMultiplier ^（期日が来た納付の回数）。納付の期日が来るたびに値上がりする
    public int mapInfoBaseCost = 10000;
    public float mapInfoDebtMultiplier = 2f;

    // --- ヒーロー経験値 ---
    public int heroExpPerMob = 10;
    public int heroExpPerBoss = 100;
    public int heroBaseExpToNextLevel = 100;
    // 配信（ダンジョン挑戦）後の最低保証レベルアップ数。経験値によるレベルアップがこれ未満なら不足分を強制的に上げる
    public int heroGuaranteedLevelUpsOnVictory = 1;
    public int heroGuaranteedLevelUpsOnDefeat = 1;
    // 敗北時の経験値上乗せ: 雑魚撃破分に倍率を掛け、さらにボス経験値の一部（割合）を加える
    public float heroDefeatMobExpMultiplier = 1.5f;
    public float heroDefeatBossExpShare = 0.5f;

    // --- 勇者の装備補正（ランク = ItemData.requiredLevel）---
    // 倍率 = 1 + Base + PerTier × (ランク-1)。未装備なら補正なし
    public float heroWeaponAttackBonusBase = 0.10f;
    public float heroWeaponAttackBonusPerTier = 0.08f;
    public float heroArmorDefenseBonusBase = 0.15f;
    public float heroArmorDefenseBonusPerTier = 0.10f;
    public float heroArmorHpBonusBase = 0.05f;
    public float heroArmorHpBonusPerTier = 0.05f;
    // 武器の属性が敵の弱点（ElementAttributeMapper.IsEffective）なら、その敵への勇者の攻撃をこの割合だけ上げる（Q7）。
    // 実戦闘だけに効き、クリア確率（ClearProbabilityCalculator）には入れない（2026-10 ユーザー決定）。
    // 目安は +30% だったが、計測（Docs/Level_Design_Log.md 2026-10-21）で +30〜70% は勝敗が1つも変わらなかったため +100%（弱点で2倍）
    public float heroAttributeAttackBonus = 1.0f;

    // --- 鍛冶屋レベルアップコスト（index = 現在レベル → 次レベルへの費用） ---
    public int[] blackSmithLevelUpCosts = { 0, 3000, 6000, 12000, 20000 };

    // --- 情報屋レベルアップコスト（index = 現在レベル → 次レベルへの費用） ---
    public int[] infoBrokerLevelUpCosts = { 0, 2500, 6000, 12000, 20000 };

    // --- ゲームフロー自動生成（ローグライト） ---
    public GameFlowGenerationSettings flowGeneration = new GameFlowGenerationSettings();

    // --- レリック（装備アイテム） ---
    public RelicSettingsData relicSettings = new RelicSettingsData();

    // --- 準備シーン（メタ進行・借入レバレッジ・スタートダッシュ） ---
    public PreparationSettingsData preparation = new PreparationSettingsData();

    // --- 村（メタ層・村投資） ---
    public VillageSettingsData village = new VillageSettingsData();

    // --- 配信（FightScene の戦闘）のテンポ・ウェーブ・販売間隔 ---
    public BattleTempoData battleTempo = new BattleTempoData();

    // --- 配信の同時接続数（コメント量の入力。表示のみでゲーム性には影響しない） ---
    public StreamingAudienceData streamingAudience = new StreamingAudienceData();

    // --- 店の設備（マシン）の修理など。配信は data シートの「machine.」付きのキー ---
    public MachineSettingsData machine = new MachineSettingsData();

    // --- クリア見込み（店の勇者パネル・マップ・配信の勝敗メーター）。2026-10-30 ユーザー決定「見込みに魔王の残り HP を入れる」 ---
    // true: 負ける見込みのときの「どこまで迫れたか」を、倒した敵の数ではなく削った敵の HP の割合で測る
    //       （ボス・魔王の残り HP がそのまま効く。配信中は場にいるボスのいまの HP を使う）。
    // false（既定・配信に無いとき）: 今までどおり倒した敵の数の割合。data シートのキー clearOutlookProgressByEnemyHp（bool）
    public bool clearOutlookProgressByEnemyHp = false;

    /// <summary>
    /// 配列まで含めた深いコピーを返す（ベイク済みアセットを実行時に汚染しないため）。
    /// </summary>
    public GameConstData Clone()
    {
        var clone = (GameConstData)MemberwiseClone();
        clone.blackSmithLevelUpCosts = blackSmithLevelUpCosts != null
            ? (int[])blackSmithLevelUpCosts.Clone()
            : Array.Empty<int>();
        clone.infoBrokerLevelUpCosts = infoBrokerLevelUpCosts != null
            ? (int[])infoBrokerLevelUpCosts.Clone()
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
        clone.village = village != null
            ? village.Clone()
            : new VillageSettingsData();
        clone.battleTempo = battleTempo != null
            ? battleTempo.Clone()
            : new BattleTempoData();
        clone.streamingAudience = streamingAudience != null
            ? streamingAudience.Clone()
            : new StreamingAudienceData();
        clone.machine = machine != null
            ? machine.Clone()
            : new MachineSettingsData();
        // >>> 配信の調整キー・ボスの反応（2026-10-30。StreamTuning.cs）
        clone.streamTuning = streamTuning != null ? streamTuning.Clone() : new StreamTuningData();
        clone.bossReaction = bossReaction != null ? bossReaction.Clone() : new BossReactionSettingsData();
        // <<<
        return clone;
    }

    // >>> 配信の調整キー・ボスの反応（2026-10-30。クラスは Assets/Scripts/Streaming/StreamTuning.cs。既定値はどれも今の動き）
    // data シートのキー: streamTuning.supportOddsEasy など / bossReaction.uiEnabled など
    public StreamTuningData streamTuning = new StreamTuningData();
    public BossReactionSettingsData bossReaction = new BossReactionSettingsData();
    // <<<
}

/// <summary>
/// 配信（FightScene の戦闘）のテンポ設定。
/// 「配信が短すぎて見るものがない」対策（2026-10）で、旧 BattleUIView のシーン値
/// （battleSpeedMultiplier=2.99 / logDisplayDuration=0.5）と StreamingSalesController の
/// シーン値（baseSalesInterval=15 / intervalRandomness=3）をここへ寄せた。
/// 秒数はすべて speedMultiplier で割られる（2 なら半分の時間）。ただし販売ループ間隔は実時間。
/// </summary>
[Serializable]
public class BattleTempoData
{
    /// <summary>全体のスピード倍率。大きいほど速い（旧シーン値 2.99）。</summary>
    public float speedMultiplier = 1f;
    /// <summary>ログ1件あたりの待機秒（ログ文字列は非表示だが「間」として機能している）。</summary>
    public float logSeconds = 0.4f;
    /// <summary>攻撃モーション（踏み込み→戻り）の秒数。0 で演出なし。</summary>
    public float attackMotionSeconds = 0.35f;
    /// <summary>攻撃モーションの踏み込み距離（ワールド単位）。</summary>
    public float attackLungeDistance = 0.6f;
    /// <summary>ターンとターンの間に挟む「間」の秒数。</summary>
    public float turnIntervalSeconds = 0.4f;
    /// <summary>戦闘開始ログの後、最初の敵が出るまでの秒数。</summary>
    public float battleStartSeconds = 1.0f;
    /// <summary>ウェーブ（フェーズ）クリア後、次のウェーブが出るまでの秒数（この間も販売ループは動く）。</summary>
    public float waveIntermissionSeconds = 2.0f;
    /// <summary>増援出現時の待機秒（旧ハードコード 500ms）。</summary>
    public float reinforceDelaySeconds = 0.5f;
    /// <summary>撃破時の点滅回数（旧ハードコード 5）。</summary>
    public int deathBlinkCount = 5;
    /// <summary>撃破時の点滅1回の秒数（旧ハードコード 0.15）。</summary>
    public float deathBlinkInterval = 0.15f;

    /// <summary>
    /// 通常ウェーブ（ボスを含まないフェーズ）を何周させるか。1 = ダンジョンSOの構成どおり。
    /// 2 以上にすると戦闘が長くなるが、難易度・撃破数（価格変動イベント数）も増える。
    /// 2026-10 ユーザー決定「配信はウェーブ数を増やして伸ばす」で 4（Lv1 実測: 配信開始〜決着 約21秒 → 約49秒）。
    /// ※ ClearProbabilityCalculator.BuildPhases の戻り値も BattleWavePlan.Expand に通すこと
    ///   （通さないとクリア確率表示が実戦より高く出る）。
    /// </summary>
    public int normalWaveRepeat = 4;

    /// <summary>
    /// 時間経過販売ループの基本間隔（実時間秒）。0 以下ならシーンの StreamingSalesController 値を使う。
    /// テンポを落とした分（約2.5〜4倍）だけ伸ばし、1配信あたりの販売回数が変わらないようにしている（旧 15秒）。
    /// </summary>
    public float salesLoopIntervalSeconds = 40f;
    /// <summary>時間経過販売ループ間隔の揺らぎ（±秒。旧 3秒）。</summary>
    public float salesLoopRandomness = 6f;

    public BattleTempoData Clone() => (BattleTempoData)MemberwiseClone();
}

/// <summary>
/// 配信の同時接続数（同接）の設定。同接 = (基礎 + フォロワー×係数) × バズ倍率 × 配信熱倍率。
/// いまは表示とコメント量だけに使う（売上・精算には影響しない）。
/// </summary>
[Serializable]
public class StreamingAudienceData
{
    /// <summary>フォロワー0でも来る基礎人数。</summary>
    public int baseViewers = 60;
    /// <summary>フォロワー1人あたりの同接。</summary>
    public float viewersPerFollower = 0.5f;
    /// <summary>バズ倍率（通常バズ / 超バズ / 炎上）。</summary>
    public float buzzMultiplier = 1.6f;
    public float superBuzzMultiplier = 3f;
    public float flameMultiplier = 1.4f;
    /// <summary>配信熱 0 / 100 のときの倍率（間は線形）。</summary>
    public float heatMultiplierAt0 = 0.6f;
    public float heatMultiplierAt100 = 1.8f;
    /// <summary>目標値へ近づく速さ（1秒あたりの割合）。</summary>
    public float approachPerSecond = 0.35f;
    /// <summary>毎秒のゆらぎ（±割合）。</summary>
    public float jitter = 0.03f;

    public StreamingAudienceData Clone() => (StreamingAudienceData)MemberwiseClone();
}

/// <summary>
/// 店の設備（マシン）の設定。レベルごとの費用・効果・壊れやすさは balance.json の machineLevels（シート machineLevels）。
/// </summary>
[Serializable]
public class MachineSettingsData
{
    /// <summary>修理費の率。修理費 = この率 × 今のレベルの行の費用（Lv1 = 購入費）。村の職人組合の割引も掛かる。</summary>
    public float repairCostRate = 0.5f;

    public MachineSettingsData Clone() => (MachineSettingsData)MemberwiseClone();
}

/// <summary>準備シーン（メタ進行）関連の設定。</summary>
[Serializable]
public class PreparationSettingsData
{
    // --- 村資金の持ち込み（ラン終了時に手元Gが村資金になり、次の出店に持ち込める） ---
    /// <summary>持ち込みGの上限（index = 村の銀行レベル。0 = 未建設）。</summary>
    public int[] bankCarryLimits = { 50000, 100000, 200000, 400000, 700000 };

    /// <summary>持ち込みアイテムのスロット数（合計個数の上限）。</summary>
    public int baseCarrySlots = 2;

    // --- 旧仕組みの残置フィールド（借入レバレッジ・メタ通貨「信用」。現在は未使用） ---
    /// <summary>【旧・未使用】借入の利率。</summary>
    public float borrowInterestRate = 0.5f;
    /// <summary>【旧・未使用】借入枠の上限額。</summary>
    public int[] creditLineAmounts = { 0, 5000, 10000, 20000 };
    /// <summary>【旧・未使用】借入枠拡張のメタ通貨コスト。</summary>
    public int[] creditLineUpgradeCosts = { 30, 80, 200 };
    /// <summary>【旧・未使用】クリア時: floor(NetWorth / この値) を獲得。</summary>
    public int metaCurrencyDivisor = 5000;
    /// <summary>【旧・未使用】ランクボーナス（S/A/B/C/D）。</summary>
    public int[] rankBonuses = { 200, 120, 70, 40, 20 };
    /// <summary>【旧・未使用】到達ターン×この値を獲得。</summary>
    public int metaCurrencyPerTurn = 2;

    // --- スタートダッシュ（村資金で購入するコストと効果量） ---
    public int flyerCost = 100000;
    public int flyerAttention = 20;
    public int flyerFollowers = 100;
    public int appraisalCost = 150000;
    public float appraisalDemandBoost = 0.15f;
    public int graceCost = 200000;
    /// <summary>初回減免状: 初回返済額の割引率。</summary>
    public float graceDiscountRate = 0.3f;

    public PreparationSettingsData Clone()
    {
        var clone = (PreparationSettingsData)MemberwiseClone();
        clone.bankCarryLimits = bankCarryLimits != null ? (int[])bankCarryLimits.Clone() : Array.Empty<int>();
        clone.creditLineAmounts = creditLineAmounts != null ? (int[])creditLineAmounts.Clone() : Array.Empty<int>();
        clone.creditLineUpgradeCosts = creditLineUpgradeCosts != null ? (int[])creditLineUpgradeCosts.Clone() : Array.Empty<int>();
        clone.rankBonuses = rankBonuses != null ? (int[])rankBonuses.Clone() : Array.Empty<int>();
        return clone;
    }
}

/// <summary>村（メタ層・村投資）関連の設定。村と店の経営（ラン）は別フローで、橋はラン終了時の変換のみ。</summary>
[Serializable]
public class VillageSettingsData
{
    // --- ラン終了 → 村資金の変換（Docs/Village_Meta_Design.md §4） ---
    // 儲け = 純資産 − 初期資金 − 持ち込み − 立て替え。クリア時は「持ち込み + 儲け × 率 × 難易度倍率」が村資金に入る
    /// <summary>ランクリア時: 儲け → 村資金への変換率。</summary>
    public float conversionRate = 1f;
    /// <summary>破産時: そのランの最高の儲け（所持金の最高値から求める）× 変換率 × この値。持ち込みは戻らない。</summary>
    public float bankruptcyConversionRate = 0.2f;
    /// <summary>破産時の救済: 最低でも「この額 + 納付した額 × bankruptcyPaidDebtRate」は村資金に入る（最高の儲けから出す額の方が多ければそちら）。</summary>
    public int bankruptcyBaseFunds = 50000;
    /// <summary>破産時の救済: それまでに納付した額（予定額で数える）に掛ける率。</summary>
    public float bankruptcyPaidDebtRate = 1f;
    /// <summary>村の総合Lvに応じた借金増加率（「発展した村は狙われる」。0=無効・将来用）。</summary>
    public float debtScalePerVillageLevel = 0f;
    /// <summary>変換率に掛ける難易度ごとの倍率（index = GameModeId: Short / Medium / Long）。</summary>
    public float[] difficultyConversionMultipliers = { 1f, 1.25f, 1.5f };
    /// <summary>
    /// 【旧・未使用（2026-10-28 まで）】難易度ごとの固定の完走ボーナス（index = GameModeId）。
    /// 今は clearBaseFlat + clearDebtMultiplier × 予定の納付金の合計（MetaProgressModel.ClearBonusBase）。
    /// 古いリモート JSON との互換のためにデータだけ残している。
    /// </summary>
    public int[] clearBaseFunds = { 300000, 450000, 600000 };
    /// <summary>
    /// 完走ボーナスの土台（クリアしたら儲けに関係なく入る）。赤字のクリアでも Lv1 を1つ建てられる額。
    /// 完走ボーナス = clearBaseFlat + clearDebtMultiplier × そのランで期日が来た納付金の「予定額」の合計（減免・借入・立て替えは含めない）。
    /// </summary>
    public int clearBaseFlat = 300000;
    /// <summary>完走ボーナス: そのランで期日が来た納付金の予定額の合計に掛ける倍率（長く走って多く納めたランほど多く入る）。</summary>
    public float clearDebtMultiplier = 2f;

    // --- ランク（儲け ÷ 難易度ごとの基準。A = 基準以上） ---
    /// <summary>ランク A の基準となる儲け（index = GameModeId）。S = 基準×rankSRatio、B = ×rankBRatio、C = 0 以上、D = 赤字。</summary>
    public int[] rankBaseProfits = { 60000, 130000, 220000 };
    public float rankSRatio = 2f;
    public float rankBRatio = 0.4f;
    // >>> ランクの基準を村の総合Lv で伸ばす（2026-10-30 審議 L2-P2a。StreamTuning.RankBase。既定 0 = 今の動き）
    /// <summary>ランク A の基準に足す額（村の総合Lv 1 あたり。全難易度共通）。例: 40000 で Lv30 なら +120万。</summary>
    public int rankBasePerVillageLevel = 0;
    /// <summary>シートからランクの基準を置き換える（0 = rankBaseProfits の値。配列はシートに書けないため）。</summary>
    public int rankBaseProfitShort = 0;
    public int rankBaseProfitMedium = 0;
    public int rankBaseProfitLong = 0;
    // <<<

    // --- 施設の効果（index = 施設レベル。0 = 未建設。施設の最大は Lv5） ---
    // Lv5 は特殊な効果だけで、数値は Lv4 と同じ（配列の Lv5 の値は Lv4 と同じにしておく。足りなければ最後の値を使う）
    /// <summary>この施設レベルで「特殊な効果」が付く（VillageEffects.IsMaxLevel。施設の最大レベル）。</summary>
    public int specialEffectLevel = 5;
    /// <summary>領主館: クリア時の完走ボーナス（clearBaseFlat + 納付金の予定額 × clearDebtMultiplier）への上乗せ率。</summary>
    public float[] hallClearBonusRates = { 0f, 0f, 0f, 0f, 0.15f, 0.15f };
    /// <summary>冒険者ギルド: 報酬の3択を辞退したときの買取額の倍率。</summary>
    public float[] guildDeclineGoldMultipliers = { 1f, 1f, 1.5f, 1.5f, 2f, 2f };
    /// <summary>冒険者ギルド: このレベル以上で報酬の選択肢が +1。</summary>
    public int guildExtraChoiceLevel = 3;
    /// <summary>冒険者ギルド: このレベル以上でスターターレリックの候補にレアが並ぶ。</summary>
    public int guildStarterRareLevel = 1;
    /// <summary>冒険者ギルド: スターターレリックの候補に足すエピックの数（ランごとに選び直す）。</summary>
    public int[] guildStarterEpicCounts = { 0, 0, 0, 0, 2, 2 };
    /// <summary>酒場: ラン開始時の情報屋のレベル（0 = そのまま）。</summary>
    public int[] tavernStartInfoBrokerLevels = { 0, 2, 2, 2, 3, 3 };
    /// <summary>酒場: 新聞の購読料の割引率。</summary>
    public float[] tavernNewspaperDiscounts = { 0f, 0f, 0.25f, 0.5f, 0.65f, 0.65f };
    /// <summary>酒場: 取引所（ファンド）の売買手数料の引き下げ幅（率のポイント）。</summary>
    public float[] tavernFundFeeReductions = { 0f, 0f, 0f, 0.01f, 0.02f, 0.02f };
    /// <summary>酒場: 占い師の値段（0 = 通常の値段 ProphetFortune.Cost のまま）。</summary>
    public int[] tavernProphetPrices = { 0, 0, 0, 0, 10000, 10000 };
    /// <summary>職人組合: ラン開始時の鍛冶屋のレベル（0 = そのまま）。</summary>
    public int[] artisanStartBlacksmithLevels = { 0, 2, 2, 2, 3, 3 };
    /// <summary>職人組合: 鍛冶屋のレベルアップ費・マシン購入費の割引率。</summary>
    public float[] artisanCostDiscounts = { 0f, 0f, 0.15f, 0.15f, 0.25f, 0.25f };
    /// <summary>職人組合: マシン設置枠の追加数。</summary>
    public int[] artisanMachineSlotAdds = { 0, 0, 0, 1, 2, 2 };
    /// <summary>広場: 開始時のフォロワー・注目度の加算。</summary>
    public int[] plazaStartFollowers = { 0, 500, 500, 1000, 2000, 2000 };
    public int[] plazaStartAttention = { 0, 10, 10, 20, 40, 40 };
    /// <summary>広場: 配信で売れた額への上乗せ率。</summary>
    public float[] plazaStreamSalesBonuses = { 0f, 0f, 0.05f, 0.1f, 0.15f, 0.15f };

    // --- Lv5 の特殊な効果: 銀行「勝負の融資」（借りる額は持ち込み上限 bankCarryLimits の最後の値） ---
    /// <summary>銀行 Lv5: 勝負の融資の利息（0.1 = 1割。配信の次の日に元本 + 利息を返す）。</summary>
    public float bankLoanInterestRate = 0.1f;

    // --- Lv5 の特殊な効果: 広場「勝負配信の宣言」 ---
    /// <summary>広場 Lv5: 宣言が当たったときの配信の売上・防衛報酬の倍率。</summary>
    public float plazaDeclarationMultiplier = 1.5f;
    /// <summary>広場 Lv5: 宣言が当たったときのフォロワー・注目の加算。</summary>
    public int plazaDeclarationHitFollowers = 6000;
    public int plazaDeclarationHitAttention = 100;
    /// <summary>広場 Lv5: 宣言が外れたときのフォロワー・注目の減少（正の値で書く）。</summary>
    public int plazaDeclarationMissFollowers = 2000;
    public int plazaDeclarationMissAttention = 60;

    // --- 村の格（index = 格。0 = 開拓地。Docs/Village_Meta_Design.md §0-2。セーブせず施設レベルから毎回求める） ---
    /// <summary>村の格の名前（仮）。開拓地 → 村 → 町 → 王都。最後の格が「王都」（ご褒美: 会話・称号・自己ベストの掲示板）。</summary>
    public string[] rankNames = { "開拓地", "村", "町", "王都" };
    /// <summary>その格になるのに要る総合Lv（6施設のレベルの合計。最大30）。</summary>
    public int[] rankRequiredTotalLevels = { 0, 2, 10, 30 };
    /// <summary>その格になるのに要る領主館のレベル（0 = 条件なし）。</summary>
    public int[] rankRequiredHallLevels = { 0, 0, 1, 0 };
    /// <summary>ランの長さを選べるようになる村の格（index = GameModeId: かんたん / ふつう / むずかしい）。</summary>
    public int[] modeRequiredRanks = { 0, 1, 2 };

    public VillageSettingsData Clone()
    {
        var clone = (VillageSettingsData)MemberwiseClone();
        clone.difficultyConversionMultipliers = CopyOf(difficultyConversionMultipliers);
        clone.clearBaseFunds = CopyOf(clearBaseFunds);
        clone.rankBaseProfits = CopyOf(rankBaseProfits);
        clone.hallClearBonusRates = CopyOf(hallClearBonusRates);
        clone.guildDeclineGoldMultipliers = CopyOf(guildDeclineGoldMultipliers);
        clone.guildStarterEpicCounts = CopyOf(guildStarterEpicCounts);
        clone.tavernStartInfoBrokerLevels = CopyOf(tavernStartInfoBrokerLevels);
        clone.tavernNewspaperDiscounts = CopyOf(tavernNewspaperDiscounts);
        clone.tavernProphetPrices = CopyOf(tavernProphetPrices);
        clone.artisanStartBlacksmithLevels = CopyOf(artisanStartBlacksmithLevels);
        clone.tavernFundFeeReductions = CopyOf(tavernFundFeeReductions);
        clone.artisanCostDiscounts = CopyOf(artisanCostDiscounts);
        clone.artisanMachineSlotAdds = CopyOf(artisanMachineSlotAdds);
        clone.plazaStartFollowers = CopyOf(plazaStartFollowers);
        clone.plazaStartAttention = CopyOf(plazaStartAttention);
        clone.plazaStreamSalesBonuses = CopyOf(plazaStreamSalesBonuses);
        clone.rankNames = CopyOf(rankNames);
        clone.rankRequiredTotalLevels = CopyOf(rankRequiredTotalLevels);
        clone.rankRequiredHallLevels = CopyOf(rankRequiredHallLevels);
        clone.modeRequiredRanks = CopyOf(modeRequiredRanks);
        return clone;
    }

    private static T[] CopyOf<T>(T[] source) => source != null ? (T[])source.Clone() : Array.Empty<T>();
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
