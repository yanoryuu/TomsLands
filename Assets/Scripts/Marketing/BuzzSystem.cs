using System;
using R3;
using UnityEngine;

/// <summary>
/// アクティブなバズの状態を保持するクラス。
/// バズ発生時に生成され、持続ターンが終了するまで保持される。
/// </summary>
[Serializable]
public class ActiveBuzzState
{
    /// <summary>バズの種類</summary>
    public BuzzType BuzzType;

    /// <summary>使用中のバズ効果データ</summary>
    public BuzzEffectData EffectData;

    /// <summary>残り持続ターン数</summary>
    public int RemainingTurns;

    /// <summary>即時効果で計算された売上倍率（持続中にも参照される）</summary>
    public float RevenueMultiplier;
}

/// <summary>
/// バズ発生システム。
/// 毎ターンのバズ発生判定、バズ種類の決定、効果の適用（即時・持続・終了後）を管理する。
/// 
/// 【既存システムとの連携】
/// - GameFlowManager.NextTurn() の前後で ProcessTurnStart() を呼び出す。
/// - SalesCalculator からバズ中の売上倍率を参照される。
/// - AdvertisementSystem にバズ中の広告費割引を設定する。
/// - ShopStatusModel のステータス値を参照・変更する。
/// - FollowerSystem からフォロワーボーナスを参照する。
/// </summary>
public class BuzzSystem
{
    private readonly ShopStatusModel _statusModel;
    private readonly FollowerSystem _followerSystem;
    private readonly AdvertisementSystem _adSystem;
    private readonly GameBalanceData _balanceData;

    /// <summary>各バズタイプに対応する効果データ（炎上・通常・大バズ）</summary>
    private readonly BuzzEffectData _flameData;
    private readonly BuzzEffectData _normalBuzzData;
    private readonly BuzzEffectData _bigBuzzData;

    /// <summary>現在アクティブなバズ（なければ null）</summary>
    private ActiveBuzzState _activeBuzz;

    /// <summary>バズがアクティブかどうか（UI表示用のリアクティブプロパティ）</summary>
    public ReactiveProperty<bool> IsBuzzActive { get; } = new(false);

    /// <summary>現在のバズタイプ（UI表示用のリアクティブプロパティ）</summary>
    public ReactiveProperty<BuzzType> CurrentBuzzType { get; } = new(BuzzType.Normal);

    /// <summary>バズの残りターン数（UI表示用のリアクティブプロパティ）</summary>
    public ReactiveProperty<int> RemainingTurns { get; } = new(0);

    /// <summary>
    /// コンストラクタ。VContainer から注入する。
    /// </summary>
    /// <param name="statusModel">ステータスモデル</param>
    /// <param name="followerSystem">フォロワーシステム</param>
    /// <param name="adSystem">広告システム</param>
    /// <param name="balanceData">バランスデータ</param>
    /// <param name="flameData">炎上バズの効果データ</param>
    /// <param name="normalBuzzData">通常バズの効果データ</param>
    /// <param name="bigBuzzData">大バズの効果データ</param>
    public BuzzSystem(
        ShopStatusModel statusModel,
        FollowerSystem followerSystem,
        AdvertisementSystem adSystem,
        GameBalanceData balanceData,
        BuzzEffectData flameData,
        BuzzEffectData normalBuzzData,
        BuzzEffectData bigBuzzData)
    {
        _statusModel = statusModel;
        _followerSystem = followerSystem;
        _adSystem = adSystem;
        _balanceData = balanceData;
        _flameData = flameData;
        _normalBuzzData = normalBuzzData;
        _bigBuzzData = bigBuzzData;

        ValidateData();
    }

    /// <summary>
    /// データの整合性を検証する。
    /// </summary>
    private void ValidateData()
    {
        if (_balanceData == null) Debug.LogError("[BuzzSystem] GameBalanceData が null です。");
        if (_flameData == null) Debug.LogError("[BuzzSystem] 炎上バズデータが null です。");
        if (_normalBuzzData == null) Debug.LogError("[BuzzSystem] 通常バズデータが null です。");
        if (_bigBuzzData == null) Debug.LogError("[BuzzSystem] 大バズデータが null です。");
    }

    // =====================================================
    // ターン処理
    // =====================================================

    /// <summary>
    /// ターン開始時に呼び出す。バズの発展判定・継続判定・持続効果適用・新規バズ判定を行う。
    ///
    /// 【処理の流れ（新方式：確率は GameBalanceData に外だし）】
    /// 1. 通常バズ継続中 → 超バズへの発展判定（buzzEvolveToBigChance）
    /// 2. バズ継続中 → 継続判定（buzzContinueChance）。失敗で即終了、成功で持続効果適用
    /// 3. バズなし → 超バズ発生判定（bigBuzzBaseChance～bigBuzzMaxChance）
    ///              → 外れたら通常バズ発生判定（buzzBaseChance～buzzMaxChance、低信頼なら炎上化）
    ///
    /// 【呼び出しタイミング】
    /// GameFlowManager の NextTurn() 内、経済更新の後に呼び出すことを想定。
    /// </summary>
    /// <returns>このターンで発生したバズの情報</returns>
    public BuzzTurnResult ProcessTurnStart()
    {
        var result = new BuzzTurnResult();

        // 1. アクティブなバズがある場合、発展→継続の順に判定
        if (_activeBuzz != null)
        {
            // 通常バズ → 超バズへの発展判定
            if (_activeBuzz.BuzzType == BuzzType.Normal && _balanceData != null)
            {
                float evolveRoll = UnityEngine.Random.Range(0f, 100f);
                if (evolveRoll < _balanceData.buzzEvolveToBigChance)
                {
                    Debug.Log($"[BuzzSystem] 超バズ発展: ロール={evolveRoll:F1} < {_balanceData.buzzEvolveToBigChance}% → 超バズへ！");
                    EndBuzz(); // 終了後効果は適用せず超バズへ引き継ぐ
                    StartBuzz(BuzzType.Big);
                    result.NewBuzzOccurred = true;
                    result.NewBuzzType = BuzzType.Big;
                    return result;
                }
            }

            // 継続判定（失敗したらこのターンで終了）
            float continueChance = _balanceData != null ? _balanceData.buzzContinueChance : 50f;
            float continueRoll = UnityEngine.Random.Range(0f, 100f);
            bool continues = continueRoll < continueChance;

            Debug.Log($"[BuzzSystem] 継続判定: {_activeBuzz.BuzzType} ロール={continueRoll:F1} / 継続率={continueChance:F0}% → {(continues ? "継続" : "終了")}");

            if (continues)
            {
                ApplySustainedEffect();
                _activeBuzz.RemainingTurns--;
                RemainingTurns.Value = _activeBuzz.RemainingTurns;

                // 最大持続ターン（BuzzEffectData 由来）に達したら終了
                if (_activeBuzz.RemainingTurns <= 0)
                {
                    result.BuzzEnded = true;
                    result.EndedBuzzType = _activeBuzz.BuzzType;
                    ApplyAfterEffect();
                    EndBuzz();
                }
            }
            else
            {
                result.BuzzEnded = true;
                result.EndedBuzzType = _activeBuzz.BuzzType;
                ApplyAfterEffect();
                EndBuzz();
            }
        }

        // 2. バズが非アクティブの場合のみ、新規バズ発生判定を行う
        //    （このターンに終了・発展した場合は判定しない）
        if (_activeBuzz == null && !result.BuzzEnded)
        {
            // 超バズ → 通常バズ の順に判定（超バズ優先）
            float bigChance = CalculateBigBuzzChance();
            float bigRoll = UnityEngine.Random.Range(0f, 100f);
            Debug.Log($"[BuzzSystem] 超バズ判定: 確率={bigChance:F1}% / ロール={bigRoll:F1}");

            if (bigRoll < bigChance)
            {
                result.NewBuzzOccurred = true;
                result.NewBuzzType = BuzzType.Big;
                StartBuzz(BuzzType.Big);
            }
            else
            {
                float buzzChance = CalculateBuzzChance();
                float roll = UnityEngine.Random.Range(0f, 100f);
                Debug.Log($"[BuzzSystem] バズ判定: 確率={buzzChance:F1}% / ロール={roll:F1}");

                if (roll < buzzChance)
                {
                    // バズ発生！低信頼なら炎上化
                    BuzzType type = DetermineBuzzType();
                    result.NewBuzzOccurred = true;
                    result.NewBuzzType = type;
                    StartBuzz(type);
                }
            }
        }

        return result;
    }

    // =====================================================
    // バズ確率計算
    // =====================================================

    /// <summary>
    /// ステータスによる強化度合いを 0～1 で返す。
    /// 強化ボーナス（注目×係数＋信頼×係数＋フォロワーボーナス）を
    /// buzzMaxBaseChance を基準に正規化する。
    /// </summary>
    private float CalculateEnhancementRatio()
    {
        if (_balanceData == null || _balanceData.buzzMaxBaseChance <= 0f) return 0f;

        float bonus = _statusModel.Attention.Value * _balanceData.buzzAttentionCoeff
                    + _statusModel.Trust.Value * _balanceData.buzzTrustCoeff
                    + (_followerSystem != null ? _followerSystem.GetBuzzChanceBonus() : 0f);

        return Mathf.Clamp01(bonus / _balanceData.buzzMaxBaseChance);
    }

    /// <summary>
    /// 通常バズの発生確率（%）を計算する。
    /// 基礎発生率から最大発生率まで、強化度合いに応じて上がる。
    /// </summary>
    public float CalculateBuzzChance()
    {
        if (_balanceData == null) return 0f;
        return Mathf.Lerp(_balanceData.buzzBaseChance, _balanceData.buzzMaxChance, CalculateEnhancementRatio());
    }

    /// <summary>
    /// 超バズの発生確率（%）を計算する。
    /// 基礎発生率から最大発生率まで、強化度合いに応じて上がる。
    /// </summary>
    public float CalculateBigBuzzChance()
    {
        if (_balanceData == null) return 0f;
        return Mathf.Lerp(_balanceData.bigBuzzBaseChance, _balanceData.bigBuzzMaxChance, CalculateEnhancementRatio());
    }

    // =====================================================
    // バズ種類判定
    // =====================================================

    /// <summary>
    /// 発生したバズの種類を決定する（超バズは事前の専用判定で決まるためここでは扱わない）。
    /// 信頼度が閾値未満なら一定確率で炎上、それ以外は通常バズ。
    /// </summary>
    private BuzzType DetermineBuzzType()
    {
        if (_balanceData == null) return BuzzType.Normal;

        int trust = _statusModel.Trust.Value;

        // 炎上判定: 信頼度が閾値未満の場合、一定確率で炎上
        if (trust < _balanceData.flameTrustThreshold)
        {
            float flameRoll = UnityEngine.Random.Range(0f, 100f);
            if (flameRoll < _balanceData.flameChance)
            {
                Debug.Log($"[BuzzSystem] 炎上判定: 信頼度={trust} < {_balanceData.flameTrustThreshold}, ロール={flameRoll:F1} < {_balanceData.flameChance}% → 炎上！");
                return BuzzType.Flame;
            }
        }

        // それ以外は通常バズ
        Debug.Log($"[BuzzSystem] 通常バズ発生: 信頼度={trust}");
        return BuzzType.Normal;
    }

    // =====================================================
    // バズ開始・終了
    // =====================================================

    /// <summary>
    /// バズを開始する。即時効果を適用し、持続状態を設定する。
    /// </summary>
    private void StartBuzz(BuzzType type)
    {
        BuzzEffectData effectData = GetEffectData(type);
        if (effectData == null)
        {
            Debug.LogError($"[BuzzSystem] バズ効果データが見つかりません: {type}");
            return;
        }

        int spread = _statusModel.Spread.Value;
        int retention = _statusModel.Retention.Value;

        // アクティブバズ状態を生成
        _activeBuzz = new ActiveBuzzState
        {
            BuzzType = type,
            EffectData = effectData,
            RemainingTurns = effectData.CalculateDuration(retention),
            RevenueMultiplier = effectData.CalculateRevenueMultiplier(spread)
        };

        // リアクティブプロパティを更新（購読側が正しいタイプで表示できるよう先にタイプを更新する）
        CurrentBuzzType.Value = type;
        IsBuzzActive.Value = true;
        RemainingTurns.Value = _activeBuzz.RemainingTurns;

        // 即時効果を適用
        ApplyImmediateEffect(effectData, spread);

        // バズ中の広告費割引を設定
        if (effectData.sustainedAdDiscountRate > 0f)
        {
            _adSystem?.SetBuzzDiscount(effectData.sustainedAdDiscountRate);
        }

        Debug.Log($"[BuzzSystem] ===== バズ開始: {type} =====" +
                  $"\n  売上倍率: {_activeBuzz.RevenueMultiplier:F2}" +
                  $"\n  持続ターン: {_activeBuzz.RemainingTurns}" +
                  $"\n  広告割引: {effectData.sustainedAdDiscountRate * 100:F0}%");
    }

    /// <summary>
    /// 即時効果を適用する。
    /// </summary>
    private void ApplyImmediateEffect(BuzzEffectData data, int spread)
    {
        // ステータス変化
        if (data.immediateTrustChange != 0)
            _statusModel.ChangeTrust(data.immediateTrustChange);
        if (data.immediateAttentionChange != 0)
            _statusModel.ChangeAttention(data.immediateAttentionChange);

        // フォロワー変化
        int followerChange = data.CalculateFollowerChange(spread);
        if (followerChange != 0)
            _statusModel.ChangeFollowers(followerChange);

        Debug.Log($"[BuzzSystem] 即時効果適用:" +
                  $" 信頼{data.immediateTrustChange:+#;-#;0}" +
                  $" 注目{data.immediateAttentionChange:+#;-#;0}" +
                  $" フォロワー{followerChange:+#;-#;0}");
    }

    /// <summary>
    /// 持続効果を適用する（毎ターン呼ばれる）。
    /// </summary>
    private void ApplySustainedEffect()
    {
        if (_activeBuzz?.EffectData == null) return;

        var data = _activeBuzz.EffectData;

        // 全ステータス上昇
        if (data.sustainedAllStatGain != 0)
            _statusModel.ChangeAllStats(data.sustainedAllStatGain);

        // フォロワー獲得
        if (data.sustainedFollowerGain != 0)
            _statusModel.ChangeFollowers(data.sustainedFollowerGain);

        // 信頼度変化（炎上時のマイナスなど）
        if (data.sustainedTrustChange != 0)
            _statusModel.ChangeTrust(data.sustainedTrustChange);

        Debug.Log($"[BuzzSystem] 持続効果適用:" +
                  $" 全ステ{data.sustainedAllStatGain:+#;-#;0}" +
                  $" フォロワー{data.sustainedFollowerGain:+#;-#;0}" +
                  $" 信頼{data.sustainedTrustChange:+#;-#;0}");
    }

    /// <summary>
    /// 終了後効果を適用する。
    /// </summary>
    private void ApplyAfterEffect()
    {
        if (_activeBuzz?.EffectData == null) return;

        var data = _activeBuzz.EffectData;

        // ステータス変化
        if (data.afterTrustChange != 0)
            _statusModel.ChangeTrust(data.afterTrustChange);
        if (data.afterAttentionChange != 0)
            _statusModel.ChangeAttention(data.afterAttentionChange);

        // 特殊効果: 無料広告付与
        if (data.afterGrantFreeMarketing && data.afterFreeMarketingData != null)
        {
            _adSystem?.ApplyEffectOnly(data.afterFreeMarketingData);
            Debug.Log($"[BuzzSystem] 特殊効果: 無料広告「{data.afterFreeMarketingData.advertisementName}」の効果を付与");
        }

        Debug.Log($"[BuzzSystem] 終了後効果適用:" +
                  $" 信頼{data.afterTrustChange:+#;-#;0}" +
                  $" 注目{data.afterAttentionChange:+#;-#;0}" +
                  $" 特殊={data.afterGrantFreeMarketing}");
    }

    /// <summary>
    /// バズを終了する。状態をクリアし、広告割引をリセットする。
    /// </summary>
    private void EndBuzz()
    {
        Debug.Log($"[BuzzSystem] ===== バズ終了: {_activeBuzz?.BuzzType} =====");

        _activeBuzz = null;
        IsBuzzActive.Value = false;
        RemainingTurns.Value = 0;
        _adSystem?.ClearBuzzDiscount();
    }

    // =====================================================
    // 外部参照用メソッド
    // =====================================================

    /// <summary>
    /// 現在のバズ中の売上倍率を取得する。
    /// バズが非アクティブの場合は 1.0（等倍）を返す。
    /// SalesCalculator から参照される。
    /// </summary>
    public float GetCurrentRevenueMultiplier()
    {
        if (_activeBuzz == null) return 1f;

        // 持続中の売上倍率が設定されている場合はそれを使用
        if (_activeBuzz.EffectData.sustainedRevenueMultiplier > 0f)
        {
            return _activeBuzz.EffectData.sustainedRevenueMultiplier;
        }

        // 即時効果の倍率を継続使用
        return _activeBuzz.RevenueMultiplier;
    }

    /// <summary>
    /// 現在アクティブなバズ状態を取得する（デバッグ・UI表示用）。
    /// </summary>
    public ActiveBuzzState GetActiveBuzz()
    {
        return _activeBuzz;
    }

    /// <summary>
    /// バズタイプに対応する効果データを取得する。
    /// </summary>
    private BuzzEffectData GetEffectData(BuzzType type)
    {
        return type switch
        {
            BuzzType.Flame => _flameData,
            BuzzType.Normal => _normalBuzzData,
            BuzzType.Big => _bigBuzzData,
            _ => null
        };
    }

    /// <summary>
    /// バズ状態を強制リセットする（デバッグ・ゲームリセット用）。
    /// </summary>
    public void ForceReset()
    {
        if (_activeBuzz != null)
        {
            EndBuzz();
        }
    }

    // =====================================================
    // デバッグ用API（DebugMenuView から使用）
    // =====================================================

    /// <summary>
    /// 【デバッグ用】指定タイプのバズを強制発生させる。
    /// 既存のバズがある場合は終了後効果を適用せず破棄してから開始する。
    /// </summary>
    public void DebugStartBuzz(BuzzType type)
    {
        if (_activeBuzz != null)
        {
            EndBuzz();
        }
        StartBuzz(type);
    }

    /// <summary>
    /// 【デバッグ用】アクティブなバズを終了後効果込みで強制終了する。
    /// </summary>
    public void DebugEndBuzz()
    {
        if (_activeBuzz == null) return;
        ApplyAfterEffect();
        EndBuzz();
    }
}

/// <summary>
/// バズのターン処理結果を格納するクラス。
/// ターン開始時の処理結果をPresenterやViewに伝えるために使用する。
/// </summary>
public class BuzzTurnResult
{
    /// <summary>新しいバズが発生したか</summary>
    public bool NewBuzzOccurred;

    /// <summary>発生した新しいバズの種類</summary>
    public BuzzType NewBuzzType;

    /// <summary>既存のバズが終了したか</summary>
    public bool BuzzEnded;

    /// <summary>終了したバズの種類</summary>
    public BuzzType EndedBuzzType;
}

