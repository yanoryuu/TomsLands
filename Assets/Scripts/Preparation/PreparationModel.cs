using UnityEngine;

/// <summary>
/// 準備シーンの選択状態（永続化しない。出店時に RunSetupData へ書き出す）。
/// 初期資金は「借入」ではなく、前のランで持ち帰って銀行に預けたGを持ち込む方式。
/// 持ち込み上限は村の銀行レベル（bankCarryLimits）で決まる。
/// </summary>
public class PreparationModel
{
    private const int CarryStep = 1000;

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
