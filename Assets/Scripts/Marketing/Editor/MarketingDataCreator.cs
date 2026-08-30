using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// マーケティングシステムのScriptableObjectデフォルトデータを一括生成するエディタツール。
/// メニュー「Tools > TomsLands > データ生成 > マーケティング初期データ生成（全部入り）」から実行する。
/// </summary>
public static class MarketingDataCreator
{
    private const string BasePath = "Assets/Resources/Marketing";

    [MenuItem("Tools/TomsLands/データ生成/マーケティング初期データ生成（全部入り）")]
    private static void CreateAllDefaultData()
    {
        // フォルダ作成
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(BasePath))
            AssetDatabase.CreateFolder("Assets/Resources", "Marketing");

        CreateDefaultAdvertisements();
        CreateDefaultBuzzEffects();
        CreateDefaultMilestones();
        CreateDefaultGameBalance();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[MarketingDataCreator] 全てのデフォルトデータを作成しました: " + BasePath);
    }

    // =====================================================
    // 広告データ作成
    // =====================================================

    // 個別メニューは「全部入り」に集約（既存アセットはスキップされるため全部入りで安全に再生成できる）
    private static void CreateDefaultAdvertisements()
    {
        EnsureFolderExists();

        // 1. SNS広告
        CreateAd("Ad_SNS", "SNS広告", 1000,
            trust: 0, attention: 40, spread: 0, retention: 0, followers: 0);

        // 2. インフルエンサー起用
        CreateAd("Ad_Influencer", "インフルエンサー起用", 2000,
            trust: 0, attention: 0, spread: 40, retention: 0, followers: 0);

        // 3. 口コミキャンペーン
        CreateAd("Ad_WordOfMouth", "口コミキャンペーン", 1500,
            trust: 40, attention: 0, spread: 0, retention: 0, followers: 0);

        // 4. リピーター施策
        CreateAd("Ad_Repeater", "リピーター施策", 1800,
            trust: 0, attention: 0, spread: 0, retention: 40, followers: 0);

        // 5. 総合マーケティング
        CreateAd("Ad_Comprehensive", "総合マーケティング", 3000,
            trust: 15, attention: 15, spread: 15, retention: 15, followers: 0);

        // 6. SNSフォロワーキャンペーン
        CreateAd("Ad_FollowerCampaign", "SNSフォロワーキャンペーン", 1200,
            trust: 0, attention: 10, spread: 0, retention: 0, followers: 1000);

        Debug.Log("[MarketingDataCreator] 広告データ作成完了");
    }

    private static void CreateAd(string fileName, string adName, int cost,
        int trust, int attention, int spread, int retention, int followers)
    {
        string path = $"{BasePath}/{fileName}.asset";
        if (AssetDatabase.LoadAssetAtPath<AdvertisementData>(path) != null)
        {
            Debug.Log($"[MarketingDataCreator] 既存のアセットをスキップ: {path}");
            return;
        }

        var ad = ScriptableObject.CreateInstance<AdvertisementData>();
        ad.advertisementName = adName;
        ad.cost = cost;
        ad.trustGain = trust;
        ad.attentionGain = attention;
        ad.spreadGain = spread;
        ad.retentionGain = retention;
        ad.followerGain = followers;

        AssetDatabase.CreateAsset(ad, path);
    }

    // =====================================================
    // バズ効果データ作成
    // =====================================================

    private static void CreateDefaultBuzzEffects()
    {
        EnsureFolderExists();

        // --- 炎上 ---
        CreateBuzzEffect("BuzzEffect_Flame", BuzzType.Flame, data =>
        {
            // 即時効果
            data.immediateRevenueMultiplierBase = 0.5f;
            data.immediateRevenueSpreadCoeff = 0f; // 固定倍率
            data.immediateTrustChange = -30;
            data.immediateAttentionChange = 20;
            data.immediateFollowerBase = -500;
            data.immediateFollowerSpreadCoeff = 0f;
            data.immediateFollowerFixed = true;

            // 持続効果
            data.durationBase = 3;
            data.durationRetentionDivisor = 25f;
            data.sustainedAllStatGain = 0;
            data.sustainedFollowerGain = 0;
            data.sustainedTrustChange = -5;
            data.sustainedRevenueMultiplier = 0.5f;
            data.sustainedAdDiscountRate = 0f;

            // 終了後効果
            data.afterTrustChange = 0;
            data.afterAttentionChange = -10;
            data.afterGrantFreeMarketing = false;
        });

        // --- 通常バズ ---
        CreateBuzzEffect("BuzzEffect_Normal", BuzzType.Normal, data =>
        {
            // 即時効果: 売上倍率 = 1.5 + (拡散力 ÷ 100)
            data.immediateRevenueMultiplierBase = 1.5f;
            data.immediateRevenueSpreadCoeff = 0.01f; // 拡散力 ÷ 100
            data.immediateTrustChange = 5;
            data.immediateAttentionChange = 0;
            data.immediateFollowerBase = 0;
            data.immediateFollowerSpreadCoeff = 10f; // フォロワー = 拡散力 × 10
            data.immediateFollowerFixed = false;

            // 持続効果
            data.durationBase = 3;
            data.durationRetentionDivisor = 25f;
            data.sustainedAllStatGain = 2;
            data.sustainedFollowerGain = 100;
            data.sustainedTrustChange = 0;
            data.sustainedRevenueMultiplier = 0f; // 即時倍率を継続
            data.sustainedAdDiscountRate = 0f;

            // 終了後効果
            data.afterTrustChange = 10;
            data.afterAttentionChange = 0;
            data.afterGrantFreeMarketing = false;
        });

        // --- 大バズ ---
        // 総合マーケティングの参照を取得
        var comprehensiveAd = AssetDatabase.LoadAssetAtPath<AdvertisementData>($"{BasePath}/Ad_Comprehensive.asset");

        CreateBuzzEffect("BuzzEffect_Big", BuzzType.Big, data =>
        {
            // 即時効果: 売上倍率 = 2.0 + (拡散力 ÷ 100) + 0.5
            data.immediateRevenueMultiplierBase = 2.5f; // 2.0 + 0.5
            data.immediateRevenueSpreadCoeff = 0.01f; // 拡散力 ÷ 100
            data.immediateTrustChange = 15;
            data.immediateAttentionChange = 20;
            data.immediateFollowerBase = 0;
            data.immediateFollowerSpreadCoeff = 30f; // フォロワー = 拡散力 × 30
            data.immediateFollowerFixed = false;

            // 持続効果
            data.durationBase = 3;
            data.durationRetentionDivisor = 25f;
            data.sustainedAllStatGain = 5;
            data.sustainedFollowerGain = 300;
            data.sustainedTrustChange = 0;
            data.sustainedRevenueMultiplier = 0f; // 即時倍率を継続
            data.sustainedAdDiscountRate = 0.3f; // 30% OFF

            // 終了後効果
            data.afterTrustChange = 20;
            data.afterAttentionChange = 0;
            data.afterGrantFreeMarketing = true;
            data.afterFreeMarketingData = comprehensiveAd;
        });

        Debug.Log("[MarketingDataCreator] バズ効果データ作成完了");
    }

    private static void CreateBuzzEffect(string fileName, BuzzType type, System.Action<BuzzEffectData> setup)
    {
        string path = $"{BasePath}/{fileName}.asset";
        if (AssetDatabase.LoadAssetAtPath<BuzzEffectData>(path) != null)
        {
            Debug.Log($"[MarketingDataCreator] 既存のアセットをスキップ: {path}");
            return;
        }

        var data = ScriptableObject.CreateInstance<BuzzEffectData>();
        data.buzzType = type;
        setup(data);
        AssetDatabase.CreateAsset(data, path);
    }

    // =====================================================
    // フォロワーマイルストーンデータ作成
    // =====================================================

    private static void CreateDefaultMilestones()
    {
        EnsureFolderExists();

        // マイルストーン1: フォロワー1,000人
        CreateMilestone("Milestone_01_1000", 1000,
            salesBonus: 0.05f, buzzBonus: 0f, adDiscount: 0f);

        // マイルストーン2: フォロワー5,000人
        CreateMilestone("Milestone_02_5000", 5000,
            salesBonus: 0.15f, buzzBonus: 5f, adDiscount: 0f);

        // マイルストーン3: フォロワー10,000人
        CreateMilestone("Milestone_03_10000", 10000,
            salesBonus: 0.30f, buzzBonus: 10f, adDiscount: 0.10f);

        // マイルストーン4: フォロワー50,000人
        CreateMilestone("Milestone_04_50000", 50000,
            salesBonus: 0.50f, buzzBonus: 15f, adDiscount: 0.20f);

        Debug.Log("[MarketingDataCreator] フォロワーマイルストーンデータ作成完了");
    }

    private static void CreateMilestone(string fileName, int requiredFollowers,
        float salesBonus, float buzzBonus, float adDiscount)
    {
        string path = $"{BasePath}/{fileName}.asset";
        if (AssetDatabase.LoadAssetAtPath<FollowerMilestoneData>(path) != null)
        {
            Debug.Log($"[MarketingDataCreator] 既存のアセットをスキップ: {path}");
            return;
        }

        var data = ScriptableObject.CreateInstance<FollowerMilestoneData>();
        data.requiredFollowers = requiredFollowers;
        data.salesBonusRate = salesBonus;
        data.buzzChanceBonus = buzzBonus;
        data.adDiscountRate = adDiscount;

        AssetDatabase.CreateAsset(data, path);
    }

    // =====================================================
    // ゲームバランスデータ作成
    // =====================================================

    private static void CreateDefaultGameBalance()
    {
        EnsureFolderExists();

        string path = $"{BasePath}/GameBalanceData.asset";
        if (AssetDatabase.LoadAssetAtPath<GameBalanceData>(path) != null)
        {
            Debug.Log($"[MarketingDataCreator] 既存のアセットをスキップ: {path}");
            return;
        }

        var data = ScriptableObject.CreateInstance<GameBalanceData>();
        // デフォルト値はGameBalanceDataクラスで定義済み
        AssetDatabase.CreateAsset(data, path);

        Debug.Log("[MarketingDataCreator] ゲームバランスデータ作成完了");
    }

    // =====================================================
    // ユーティリティ
    // =====================================================

    private static void EnsureFolderExists()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(BasePath))
            AssetDatabase.CreateFolder("Assets/Resources", "Marketing");
    }
}

