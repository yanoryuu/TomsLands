using UnityEngine;


public static class GameConst
{
    /// <summary>GameConstSettings の Addressable アドレス。</summary>
    public const string Address = "GameConstSettings";

    /// <summary>適用を受け付ける schemaVersion。データ構造を破壊的に変えた時のみ上げる。</summary>
    public const int ExpectedSchemaVersion = 1;

    private static GameConstData _data;

    public static GameConstData Data => _data ??= LoadDefault();

    /// <summary>
    /// サーバー等から取得した値で上書きする（将来のリモートコンフィグ用の差し込み口）。
    /// </summary>
    public static void Override(GameConstData data)
    {
        if (data != null) _data = data;
    }

    /// <summary>
    /// JSON 文字列から上書きする。SpreadSheet → サーバー経由でダウンロードした JSON をそのまま渡す想定。
    /// ベイク済みデフォルトを土台に、JSON に存在するフィールドだけを上書きする（前方互換）。
    /// JSON に無いフィールドはデフォルト値が保持されるため、クライアント先行でフィールドを
    /// 追加してもサーバー旧JSONで破綻しない。
    /// </summary>
    public static void OverrideFromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        // ベイク済みデフォルトを土台にする（欠損フィールドはこの値が残る）。
        // LoadDefault() は Clone 済みの独立インスタンスを返すためアセットは汚染されない。
        var baseData = LoadDefault();
        try
        {
            JsonUtility.FromJsonOverwrite(json, baseData);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameConst] JSON の解析に失敗しました。上書きを中止します。{e.Message}");
            return;
        }
        _data = baseData;
    }

    /// <summary>
    /// 配信エンベロープ（version/schemaVersion 付き）から上書きする。
    /// schemaVersion 不一致時は適用せずデフォルト（または前回値）を維持する。
    /// </summary>
    /// <returns>適用した version。適用しなかった場合は -1。</returns>
    public static int OverrideFromEnvelope(string json)
    {
        if (string.IsNullOrEmpty(json)) return -1;

        // data を土台（ベイク済みデフォルト）で初期化しておくことで、
        // data 内の欠損フィールドにもデフォルト保持が効く。
        var envelope = new GameConstEnvelope { data = LoadDefault() };
        try
        {
            // envelope.data は参照型のため、入れ子の data フィールドにも上書きが効く。
            JsonUtility.FromJsonOverwrite(json, envelope);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GameConst] エンベロープ解析に失敗しました。上書きを中止します。{e.Message}");
            return -1;
        }

        // schemaVersion 検証：不一致なら部分適用せずフォールバック。
        if (envelope.schemaVersion != ExpectedSchemaVersion)
        {
            Debug.LogWarning($"[GameConst] schemaVersion 不一致 (expected {ExpectedSchemaVersion}, got {envelope.schemaVersion})。" +
                             "デフォルト/前回値を維持します。");
            return -1;
        }

        if (envelope.data == null)
        {
            Debug.LogError("[GameConst] エンベロープに data がありません。上書きを中止します。");
            return -1;
        }

        _data = envelope.data;
        Debug.Log($"[GameConst] リモートコンフィグ version {envelope.version} を適用しました。");
        return envelope.version;
    }

    /// <summary>
    /// 上書きを破棄してベイク済みデフォルト（GameConstSettings）に戻す。
    /// リモート/キャッシュのいずれも不正だった場合の最終フォールバック用。
    /// </summary>
    public static void ResetToDefault() => _data = LoadDefault();

    /// <summary>現在の値を JSON 化する（サーバーへのアップロードや差分比較用）。</summary>
    public static string ToJson() => JsonUtility.ToJson(Data, true);

    private static GameConstData LoadDefault()
    {
        var settings = LoadSettings();
        if (settings != null && settings.data != null)
            return settings.data.Clone();

        Debug.LogWarning($"[GameConst] '{Address}' が読み込めませんでした。既定値を使用します。" +
                         "Tools > TomsLands > リモート設定 > GameConst Settingsアセット生成 で作成し、Addressable 登録してください。");
        return new GameConstData();
    }

    private static GameConstSettings LoadSettings()
    {
#if UNITY_EDITOR
        // 非再生時（Inspector 編集中やエディタ拡張からの参照）は AssetDatabase から直接取得する。
        if (!Application.isPlaying)
        {
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:GameConstSettings"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var s = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConstSettings>(path);
                if (s != null) return s;
            }
            return null;
        }
#endif
        return AddressableLoader.Load<GameConstSettings>(Address);
    }

    public static int MaxDungeonLevel => Data.maxDungeonLevel;
    public static int MaxBlackSmithLevel => Data.maxBlackSmithLevel;
    public static int MaxToolShopLevel => Data.maxToolShopLevel;
    public static int MaxInfoBrokerLevel => Data.maxInfoBrokerLevel;
    public static int MaxItemStock => Data.maxItemStock;
    public static int MinItemStock => Data.minItemStock;
    public static int InitMoney => Data.initMoney;

    // --- 準備シーン（メタ進行） ---
    public static PreparationSettingsData Preparation => Data.preparation;

    // --- レリック（装備アイテム） ---
    public static int RelicMaxEquipSlots => Data.relicSettings.maxEquipSlots;
    public static int RelicRewardChoiceCount => Data.relicSettings.rewardChoiceCount;
    public static float RelicCommonWeight => Data.relicSettings.commonWeight;
    public static float RelicRareWeight => Data.relicSettings.rareWeight;
    public static float RelicEpicWeight => Data.relicSettings.epicWeight;
    public static int DebtPaymentInterval => Data.debtPaymentInterval;
    public static int DebtBaseAmount => Data.debtBaseAmount;
    public static float DebtMultiplier => Data.debtMultiplier;
    public static int HeroExpPerMob => Data.heroExpPerMob;
    public static int HeroExpPerBoss => Data.heroExpPerBoss;
    public static int HeroBaseExpToNextLevel => Data.heroBaseExpToNextLevel;
    public static int[] BlackSmithLevelUpCosts => Data.blackSmithLevelUpCosts;

    // --- ゲームフロー自動生成 ---
    public static GameFlowGenerationSettings FlowGeneration => Data.flowGeneration;
    public static GameModeConfig GetGameMode(GameModeId id) => Data.flowGeneration.GetMode(id);

    /// <summary>
    /// 指定サイクルの返済額。支払うたびに倍率が掛かる等比級数。
    /// 返済額 = 基準額 × 倍率^(cycle-1)
    /// </summary>
    public static int GetDebtAmount(int cycle)
    {
        if (cycle <= 0) return 0;
        double amount = Data.debtBaseAmount * System.Math.Pow(Data.debtMultiplier, cycle - 1);
        if (amount >= int.MaxValue) return int.MaxValue;
        return Mathf.RoundToInt((float)amount);
    }

    public static int GetHeroExpToNextLevel(int currentLevel)
    {
        return Mathf.Max(Data.heroBaseExpToNextLevel, currentLevel * Data.heroBaseExpToNextLevel);
    }

    /// <summary>
    /// 鍛冶屋の現在レベルからレベルアップコストを取得。最大レベル／範囲外なら -1 を返す。
    /// </summary>
    public static int GetBlackSmithLevelUpCost(int currentLevel)
    {
        var costs = Data.blackSmithLevelUpCosts;
        if (currentLevel <= 0 || currentLevel >= Data.maxBlackSmithLevel) return -1;
        return costs != null && currentLevel < costs.Length ? costs[currentLevel] : -1;
    }
}
