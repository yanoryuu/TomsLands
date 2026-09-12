using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 広告システム。
/// 広告の実行（コスト消費、ステータス上昇、フォロワー獲得）を管理する。
/// フォロワーマイルストーンによる広告費割引やバズ中の割引も適用する。
/// 
/// 【既存システムとの連携】
/// - TomsModel.PlayerMoney を消費して広告を実行する。
/// - ShopStatusModel にステータス上昇を反映する。
/// - FollowerSystem から広告費割引率を参照する。
/// - BuzzSystem からバズ中の広告費割引率を参照する。
/// </summary>
public class AdvertisementSystem
{
    private readonly ShopStatusModel _statusModel;
    private readonly TomsModel _tomsModel;
    private readonly FollowerSystem _followerSystem;
    private readonly List<AdvertisementData> _advertisements;

    /// <summary>バズ中の追加広告費割引率（BuzzSystem から設定される）</summary>
    private float _buzzAdDiscountRate;

    /// <summary>
    /// コンストラクタ。VContainer から注入する。
    /// </summary>
    public AdvertisementSystem(
        ShopStatusModel statusModel,
        TomsModel tomsModel,
        FollowerSystem followerSystem,
        List<AdvertisementData> advertisements)
    {
        _statusModel = statusModel;
        _tomsModel = tomsModel;
        _followerSystem = followerSystem;
        _advertisements = advertisements;

        if (_advertisements == null || _advertisements.Count == 0)
        {
            Debug.LogWarning("[AdvertisementSystem] 広告データが未設定です。");
            _advertisements = new List<AdvertisementData>();
        }
    }

    /// <summary>
    /// バズ中の広告費割引率を設定する（BuzzSystem から呼ばれる）。
    /// </summary>
    /// <param name="rate">割引率（0〜1、例: 0.3 = 30%OFF）</param>
    public void SetBuzzDiscount(float rate)
    {
        _buzzAdDiscountRate = Mathf.Clamp01(rate);
    }

    /// <summary>
    /// バズ中の広告費割引率をクリアする。
    /// </summary>
    public void ClearBuzzDiscount()
    {
        _buzzAdDiscountRate = 0f;
    }

    /// <summary>
    /// 広告の実際のコストを計算する（割引適用後）。
    /// 計算式: 元コスト × (1 - フォロワー割引率) × (1 - バズ割引率)
    /// </summary>
    public int GetDiscountedCost(AdvertisementData ad)
    {
        if (ad == null) return 0;

        float followerDiscount = _followerSystem != null ? _followerSystem.GetAdDiscountRate() : 0f;

        // 複数の割引を乗算で適用（加算だと割引率が高くなりすぎるため）
        float finalRate = (1f - followerDiscount) * (1f - _buzzAdDiscountRate);
        int discountedCost = Mathf.Max(0, Mathf.RoundToInt(ad.cost * finalRate));

        return discountedCost;
    }

    /// <summary>
    /// 広告の実効上昇量を計算する。
    /// AdvertisementData.statMax（広告ごとのマックス値）を超えないよう、各ステータスの上昇量を削る。
    /// statMax が 0 の場合は全体上限（ShopStatusModel.StatMax）のみが上限となる。
    /// マイナスの上昇量（減少）はそのまま適用する。
    /// </summary>
    public AdvertisementEffect GetEffectiveGains(AdvertisementData ad)
    {
        if (ad == null) return default;

        int globalMax = _statusModel.StatMax;
        int cap = ad.statMax > 0 ? Mathf.Min(ad.statMax, globalMax) : globalMax;

        return new AdvertisementEffect
        {
            Trust     = CapGain(ad.trustGain,     _statusModel.Trust.Value,     cap),
            Attention = CapGain(ad.attentionGain, _statusModel.Attention.Value, cap),
            Spread    = CapGain(ad.spreadGain,    _statusModel.Spread.Value,    cap),
            Retention = CapGain(ad.retentionGain, _statusModel.Retention.Value, cap),
            Followers = ad.followerGain,
        };
    }

    private static int CapGain(int gain, int current, int cap)
    {
        if (gain <= 0) return gain;
        return Mathf.Clamp(cap - current, 0, gain);
    }

    /// <summary>
    /// 広告の効果（実効上昇量）をステータスに適用する共通処理。
    /// </summary>
    private void ApplyGains(AdvertisementEffect effect)
    {
        if (effect.Trust != 0) _statusModel.ChangeTrust(effect.Trust);
        if (effect.Attention != 0) _statusModel.ChangeAttention(effect.Attention);
        if (effect.Spread != 0) _statusModel.ChangeSpread(effect.Spread);
        if (effect.Retention != 0) _statusModel.ChangeRetention(effect.Retention);
        if (effect.Followers != 0) _statusModel.ChangeFollowers(effect.Followers);
    }

    /// <summary>
    /// 指定した広告を購入可能かどうかを判定する。
    /// </summary>
    public bool CanExecute(AdvertisementData ad)
    {
        if (ad == null) return false;

        int cost = GetDiscountedCost(ad);
        return _tomsModel.PlayerMoney.Value >= cost;
    }

    /// <summary>
    /// 広告を実行する。
    /// コストを消費し、ステータスとフォロワーを上昇させる。
    /// </summary>
    /// <returns>実行に成功した場合 true</returns>
    public bool Execute(AdvertisementData ad)
    {
        if (ad == null)
        {
            Debug.LogError("[AdvertisementSystem] 広告データが null です。");
            return false;
        }

        if (!CanExecute(ad))
        {
            Debug.LogWarning($"[AdvertisementSystem] 資金不足のため広告を実行できません: {ad.advertisementName}");
            return false;
        }

        int cost = GetDiscountedCost(ad);

        // コスト消費
        _tomsModel.PlayerMoney.Value -= cost;

        // ステータス上昇・フォロワー獲得（広告ごとの statMax で上昇量を制限）
        var effect = GetEffectiveGains(ad);
        ApplyGains(effect);

        Debug.Log($"[AdvertisementSystem] 広告実行: {ad.advertisementName}" +
                  $" | コスト: {cost}G（元: {ad.cost}G）" +
                  $" | 信頼+{effect.Trust} 注目+{effect.Attention} 拡散+{effect.Spread} 維持+{effect.Retention}" +
                  $" | フォロワー+{effect.Followers}" +
                  (ad.statMax > 0 ? $" | 広告上限: {ad.statMax}" : ""));

        return true;
    }

    /// <summary>
    /// 広告の効果のみを適用する（コスト消費なし）。
    /// 大バズ終了後の無料広告効果で使用する。
    /// </summary>
    public void ApplyEffectOnly(AdvertisementData ad)
    {
        if (ad == null)
        {
            Debug.LogError("[AdvertisementSystem] 広告データが null です（無料効果適用）。");
            return;
        }

        ApplyGains(GetEffectiveGains(ad));

        Debug.Log($"[AdvertisementSystem] 無料広告効果適用: {ad.advertisementName}");
    }

    /// <summary>
    /// 利用可能な全広告データのリストを取得する（UI表示用）。
    /// </summary>
    public IReadOnlyList<AdvertisementData> GetAllAdvertisements()
    {
        return _advertisements;
    }
}

/// <summary>
/// 広告の実効上昇量（statMax 適用後）。プレビュー表示・実行結果表示・実際の適用で共通利用する。
/// </summary>
public struct AdvertisementEffect
{
    public int Trust;
    public int Attention;
    public int Spread;
    public int Retention;
    public int Followers;
}
