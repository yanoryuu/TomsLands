using UnityEngine;

/// <summary>
/// 村の施設レベルから決まる「次のランに効く効果」（Docs/Village_Meta_Design.md §11）。
/// 施設は6つ: 領主館（他施設の Lv2・Lv3 の解禁）／冒険者ギルド／酒場（情報局）／銀行／職人組合／広場。
/// 村と店の経営（ラン）は別フローで、ラン中に施設レベルは変わらない。
/// 店シーンの読み込み時に <see cref="Refresh"/> で選択中スロットの metaData.json から作り直し、
/// 店側の各所（新聞・取引所・鍛冶屋・マシン・配信の精算・レリック抽選）は <see cref="Current"/> を読む。
/// 数値は GameConst.Village（配信値で調整可）。施設レベル0なら全て「効果なし」で従来と同じ挙動。
/// 施設の最大は Lv5。数値の効果は Lv4 で頭打ちにし、Lv5（<see cref="IsMaxLevel"/>）は施設ごとの「特殊な効果」だけが付く。
/// </summary>
public sealed class VillageEffects
{
    public const string HallId = "hall";
    public const string GuildId = "guild";
    public const string TavernId = "tavern";
    public const string BankId = "bank";
    public const string ArtisanId = "artisan";
    public const string PlazaId = "plaza";

    public int HallLevel { get; }
    public int GuildLevel { get; }
    public int TavernLevel { get; }
    public int BankLevel { get; }
    public int ArtisanLevel { get; }
    public int PlazaLevel { get; }

    private static VillageEffects _current;

    /// <summary>今のスロットの効果（未読み込みなら metaData.json から読む）。</summary>
    public static VillageEffects Current => _current ??= Refresh();

    /// <summary>効果なし（施設がすべてレベル0）。</summary>
    public static readonly VillageEffects None = new(0, 0, 0, 0, 0, 0);

    private VillageEffects(int hall, int guild, int tavern, int bank, int artisan, int plaza)
    {
        HallLevel = Mathf.Max(0, hall);
        GuildLevel = Mathf.Max(0, guild);
        TavernLevel = Mathf.Max(0, tavern);
        BankLevel = Mathf.Max(0, bank);
        ArtisanLevel = Mathf.Max(0, artisan);
        PlazaLevel = Mathf.Max(0, plaza);
    }

    public static VillageEffects From(MetaProgressModel meta) =>
        meta == null
            ? None
            : new VillageEffects(
                meta.GetFacilityLevel(HallId),
                meta.GetFacilityLevel(GuildId),
                meta.GetFacilityLevel(TavernId),
                meta.GetFacilityLevel(BankId),
                meta.GetFacilityLevel(ArtisanId),
                meta.GetFacilityLevel(PlazaId));

    /// <summary>選択中スロットの metaData.json から作り直して <see cref="Current"/> にする。</summary>
    public static VillageEffects Refresh() => _current = From(new MetaProgressModel());

    /// <summary>テスト・自動プレイ用に効果を差し替える（null で次回読み直し）。</summary>
    public static void Override(VillageEffects effects) => _current = effects;

    /// <summary>テスト・自動プレイ用: 施設レベルを直接指定して作る（<see cref="Override"/> と組み合わせる）。</summary>
    public static VillageEffects ForLevels(int hall, int guild, int tavern, int bank, int artisan, int plaza) =>
        new(hall, guild, tavern, bank, artisan, plaza);

    private static VillageSettingsData Settings => GameConst.Village;

    // ───────── 施設レベル・特殊な効果（Lv5） ─────────

    /// <summary>施設のレベル（未知の施設IDは0）。</summary>
    public int GetLevel(string facilityId) => facilityId switch
    {
        HallId => HallLevel,
        GuildId => GuildLevel,
        TavernId => TavernLevel,
        BankId => BankLevel,
        ArtisanId => ArtisanLevel,
        PlazaId => PlazaLevel,
        _ => 0,
    };

    /// <summary>「特殊な効果」が付く施設レベル（GameConst.Village.specialEffectLevel。施設の最大レベル = 5）。</summary>
    public static int SpecialEffectLevel => Mathf.Max(1, Settings.specialEffectLevel);

    /// <summary>
    /// 施設が最大レベル（Lv5）で、特殊な効果が有効か。Lv5 の特殊な効果はすべてここを入口にして判定する
    /// （領主館=配信の予定表／ギルド=呪いのレリックが報酬に出る／酒場=裏取り／銀行=勝負の融資／職人組合=細工／広場=勝負配信の宣言）。
    /// </summary>
    public bool IsMaxLevel(string facilityId) => GetLevel(facilityId) >= SpecialEffectLevel;

    // ───────── 領主館 ─────────

    /// <summary>クリア時の完走ボーナスへの上乗せ率（0.15 = +15%）。</summary>
    public float ClearBonusRate => Mathf.Max(0f, At(Settings.hallClearBonusRates, HallLevel));

    /// <summary>完走ボーナスに領主館の上乗せを足した額。</summary>
    public int ApplyClearBonus(int baseFunds) =>
        baseFunds <= 0 ? baseFunds : Mathf.RoundToInt(baseFunds * (1f + ClearBonusRate));

    // ───────── 冒険者ギルド ─────────

    /// <summary>報酬・スターターの抽選に入るレリックの解禁段階（RelicDefinition.unlockGuildLevel 以下が対象）。</summary>
    public int RelicUnlockTier => GuildLevel;

    public bool StarterRareUnlocked => GuildLevel >= Settings.guildStarterRareLevel;

    /// <summary>報酬の選択肢の数（基本の rewardChoiceCount + ギルドの追加）。</summary>
    public int RelicChoiceCount(int baseCount) =>
        baseCount + (GuildLevel >= Settings.guildExtraChoiceLevel ? 1 : 0);

    public int ApplyDeclineGold(int baseGold)
    {
        float mult = At(Settings.guildDeclineGoldMultipliers, GuildLevel);
        return mult > 0f ? Mathf.RoundToInt(baseGold * mult) : baseGold;
    }

    /// <summary>スターターレリックの候補に足すエピックの数。</summary>
    public int StarterEpicCount => Mathf.Max(0, At(Settings.guildStarterEpicCounts, GuildLevel));

    // ───────── 酒場（情報局） ─────────

    /// <summary>ラン開始時の情報屋のレベル（0 = 変えない）。</summary>
    public int StartInfoBrokerLevel => Mathf.Max(0, At(Settings.tavernStartInfoBrokerLevels, TavernLevel));

    public float NewspaperDiscount => Clamp01(At(Settings.tavernNewspaperDiscounts, TavernLevel));

    public int ApplyNewspaperPrice(int price) =>
        price <= 0 ? price : Mathf.RoundToInt(price * (1f - NewspaperDiscount));

    /// <summary>ファンドの売買手数料率から酒場の引き下げ分を引く（0 未満にはしない）。</summary>
    public float ApplyFundFee(float feeRate) =>
        Mathf.Max(0f, feeRate - At(Settings.tavernFundFeeReductions, TavernLevel));

    /// <summary>占い師の値段（酒場の値段が設定されていて、通常より安いときだけ下げる）。</summary>
    public int ApplyProphetPrice(int basePrice)
    {
        int price = At(Settings.tavernProphetPrices, TavernLevel);
        return price > 0 && price < basePrice ? price : basePrice;
    }

    // ───────── 職人組合 ─────────

    /// <summary>ラン開始時の鍛冶屋のレベル（0 = 変えない）。</summary>
    public int StartBlacksmithLevel => Mathf.Max(0, At(Settings.artisanStartBlacksmithLevels, ArtisanLevel));

    public float CostDiscount => Clamp01(At(Settings.artisanCostDiscounts, ArtisanLevel));

    public int ApplyCraftCost(int cost) =>
        cost <= 0 ? cost : Mathf.RoundToInt(cost * (1f - CostDiscount));

    public int MachineSlotAdd => Mathf.Max(0, At(Settings.artisanMachineSlotAdds, ArtisanLevel));

    // ───────── 広場 ─────────

    public int StartFollowers => Mathf.Max(0, At(Settings.plazaStartFollowers, PlazaLevel));
    public int StartAttention => Mathf.Max(0, At(Settings.plazaStartAttention, PlazaLevel));
    public float StreamSalesBonus => Mathf.Max(0f, At(Settings.plazaStreamSalesBonuses, PlazaLevel));

    /// <summary>配信で売れた額への上乗せ（G）。</summary>
    public int StreamSalesBonusGold(int rawSales) =>
        rawSales <= 0 ? 0 : Mathf.RoundToInt(rawSales * StreamSalesBonus);

    // ───────── Lv5: 銀行「勝負の融資」・広場「勝負配信の宣言」（どちらも1ランに1回） ─────────

    /// <summary>銀行 Lv5: 配信の前日に勝負の融資を受けられるか（使ったかどうかはランのセーブ TomsModel が持つ）。</summary>
    public bool BankLoanUnlocked => IsMaxLevel(BankId);

    /// <summary>勝負の融資で借りる額（持ち込み上限の最後の値。今は 70万G）。</summary>
    public static int BankLoanPrincipal
    {
        get
        {
            var limits = GameConst.Preparation.bankCarryLimits;
            return limits != null && limits.Length > 0 ? Mathf.Max(0, limits[limits.Length - 1]) : 0;
        }
    }

    /// <summary>勝負の融資で返す額（元本 + 利息。利息は GameConst.Village.bankLoanInterestRate）。</summary>
    public static int BankLoanRepayment(int principal) =>
        principal <= 0 ? 0 : Mathf.RoundToInt(principal * (1f + Mathf.Max(0f, Settings.bankLoanInterestRate)));

    /// <summary>広場 Lv5: 配信の前に勝つ側を宣言できるか（使ったかどうかはランのセーブ TomsModel が持つ）。</summary>
    public bool StreamDeclarationUnlocked => IsMaxLevel(PlazaId);

    /// <summary>宣言が当たったときの売上・防衛報酬の倍率（1 未満にはしない）。</summary>
    public static float DeclarationMultiplier => Mathf.Max(1f, Settings.plazaDeclarationMultiplier);

    // ───────── 内部 ─────────

    private static float Clamp01(float v) => Mathf.Clamp01(v);

    private static T At<T>(T[] table, int level)
    {
        if (table == null || table.Length == 0) return default;
        return table[Mathf.Clamp(level, 0, table.Length - 1)];
    }
}
