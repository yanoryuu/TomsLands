using UnityEngine;

/// <summary>
/// 準備シーンの選択状態（永続化しない。出店時に RunSetupData へ書き出す）。
/// 初期資金は「借入」ではなく、前のランで持ち帰って銀行に預けたGを持ち込む方式。
/// 持ち込み上限は村の銀行レベル（bankCarryLimits）で決まる。
/// </summary>
public class PreparationModel
{
    private const int CarryStep = 10000;

    /// <summary>銀行の施設ID（村施設マスタ VillageFacilityData.facilityId）。</summary>
    public const string BankFacilityId = "bank";

    /// <summary>持ち込み額（0〜min(村資金, 持ち込み上限)）。</summary>
    public int CarryAmount { get; private set; }

    /// <summary>選択中の難易度（出店時に StartModeData へ書き出す）。</summary>
    public GameModeId Difficulty { get; private set; } = GameModeId.Medium;

    /// <summary>選択中のスターターレリック（空=なし）。</summary>
    public string StarterRelicId { get; private set; } = "";

    public bool UseFlyer { get; private set; }
    public bool UseAppraisal { get; private set; }
    public bool UseGrace { get; private set; }

    /// <summary>村の銀行レベル（0=未建設）。</summary>
    public int GetBankLevel(MetaProgressModel meta) => meta.GetFacilityLevel(BankFacilityId);

    /// <summary>銀行レベルに応じた持ち込み上限（G）。</summary>
    public int GetCarryLimit(MetaProgressModel meta)
    {
        var limits = GameConst.Preparation.bankCarryLimits;
        if (limits == null || limits.Length == 0) return 0;
        int index = Mathf.Clamp(GetBankLevel(meta), 0, limits.Length - 1);
        return limits[index];
    }

    /// <summary>実際に持ち込める最大額 = min(村資金, 上限)。</summary>
    public int GetCarryMax(MetaProgressModel meta) =>
        Mathf.Min(meta.VillageFunds, GetCarryLimit(meta));

    public void AddCarry(int carryMax) =>
        CarryAmount = Mathf.Min(CarryAmount + CarryStep, carryMax);

    public void SubtractCarry() =>
        CarryAmount = Mathf.Max(0, CarryAmount - CarryStep);

    /// <summary>
    /// 持ち込み額を直接指定する（スライダー用）。1,000G 刻みに丸め、0〜carryMax にクランプする。
    /// 上限が 1,000 の倍数でなくても、上限以上を指定したら上限ちょうどにする。
    /// </summary>
    public void SetCarry(int amount, int carryMax)
    {
        carryMax = Mathf.Max(0, carryMax);
        if (amount >= carryMax) { CarryAmount = carryMax; return; }
        int stepped = Mathf.RoundToInt(amount / (float)CarryStep) * CarryStep;
        CarryAmount = Mathf.Clamp(stepped, 0, carryMax);
    }

    /// <summary>スライダーの刻み幅（G）。</summary>
    public static int CarryStepAmount => CarryStep;

    public void ClampCarry(int carryMax) =>
        CarryAmount = Mathf.Clamp(CarryAmount, 0, carryMax);

    public void SelectDifficulty(GameModeId difficulty) => Difficulty = difficulty;

    public void SelectStarterRelic(string relicId)
    {
        // 同じものをもう一度選ぶと解除
        StarterRelicId = StarterRelicId == relicId ? "" : relicId;
    }

    public void ToggleFlyer() => UseFlyer = !UseFlyer;
    public void ToggleAppraisal() => UseAppraisal = !UseAppraisal;
    public void ToggleGrace() => UseGrace = !UseGrace;

    /// <summary>
    /// 難易度の中身（配信の回数とおおよその日数）。GameConst のモード定義から数える
    /// （1回の配信の前に店の日が min〜max 日、配信の日が1日。イベントは日に数えない）。
    /// </summary>
    public static (int streams, int minDays, int maxDays) DescribeMode(GameModeId mode)
    {
        var config = GameConst.GetGameMode(mode);
        if (config == null) return (0, 0, 0);
        int streams = Mathf.Max(0, config.dungeonCount);
        int minShop = Mathf.Max(0, config.minShopTurnsBetweenBattles);
        int maxShop = Mathf.Max(minShop, config.maxShopTurnsBetweenBattles);
        return (streams, streams * (minShop + 1), streams * (maxShop + 1));
    }

    /// <summary>「配信2回・6〜8日」の形。</summary>
    public static string DescribeModeShort(GameModeId mode)
    {
        var (streams, minDays, maxDays) = DescribeMode(mode);
        if (streams <= 0) return "";
        string days = minDays == maxDays ? $"{minDays}日" : $"{minDays}〜{maxDays}日";
        return $"配信{streams}回・{days}";
    }

    /// <summary>選択中のスタートダッシュの合計コスト（村資金から支払う）。</summary>
    public int StartDashTotalCost
    {
        get
        {
            var settings = GameConst.Preparation;
            int total = 0;
            if (UseFlyer) total += settings.flyerCost;
            if (UseAppraisal) total += settings.appraisalCost;
            if (UseGrace) total += settings.graceCost;
            return total;
        }
    }
}
