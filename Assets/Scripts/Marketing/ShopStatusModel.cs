using System;
using System.IO;
using R3;
using UnityEngine;

[Serializable]
public class ShopStatusData
{
    public int trust;
    public int attention;
    public int spread;
    public int retention;
    public int followers;

    public ShopStatusData(int trust, int attention, int spread, int retention, int followers)
    {
        this.trust = trust;
        this.attention = attention;
        this.spread = spread;
        this.retention = retention;
        this.followers = followers;
    }
}

/// <summary>
/// 店のマーケティングステータスを管理するモデル。
/// 4つのステータス（信頼度・注目度・拡散力・顧客維持力）とフォロワー数を保持する。
/// ウマ娘風のステータス成長システムの中核。
///
/// 【既存システムとの連携】
/// - TomsModel と並行して使用。TomsModel は所持金・レベル管理、本クラスはマーケティング管理を担当。
/// - VContainer で Singleton 登録し、各 Presenter / System から注入して使用する。
/// </summary>
public class ShopStatusModel
{
    private static readonly string SavePath = Application.persistentDataPath + "/shopStatusData.json";

    // =====================================================
    // リアクティブプロパティ（UIバインディング用）
    // =====================================================

    /// <summary>信頼度（0〜100）：炎上を防ぎ、ポジティブバズになりやすくする</summary>
    public ReactiveProperty<int> Trust { get; private set; }

    /// <summary>注目度（0〜100）：バズの発生確率に影響</summary>
    public ReactiveProperty<int> Attention { get; private set; }

    /// <summary>拡散力（0〜100）：バズ発生時の効果の大きさに影響</summary>
    public ReactiveProperty<int> Spread { get; private set; }

    /// <summary>顧客維持力（0〜100）：バズ効果の持続ターン数に影響</summary>
    public ReactiveProperty<int> Retention { get; private set; }

    /// <summary>フォロワー数（0以上、上限なし）</summary>
    public ReactiveProperty<int> Followers { get; private set; }

    /// <summary>バランスデータへの参照（クランプ処理に使用）</summary>
    private readonly GameBalanceData _balanceData;

    /// <summary>
    /// コンストラクタ。GameBalanceData から初期値を読み込み、セーブデータがあれば上書きする。
    /// VContainer から GameBalanceData を注入して使用する。
    /// </summary>
    public ShopStatusModel(GameBalanceData balanceData)
    {
        _balanceData = balanceData;

        if (_balanceData == null)
        {
            Debug.LogError("[ShopStatusModel] GameBalanceData が null です。デフォルト値で初期化します。");
            Trust = new ReactiveProperty<int>(50);
            Attention = new ReactiveProperty<int>(0);
            Spread = new ReactiveProperty<int>(0);
            Retention = new ReactiveProperty<int>(0);
            Followers = new ReactiveProperty<int>(0);
        }
        else
        {
            Trust = new ReactiveProperty<int>(_balanceData.initialTrust);
            Attention = new ReactiveProperty<int>(_balanceData.initialAttention);
            Spread = new ReactiveProperty<int>(_balanceData.initialSpread);
            Retention = new ReactiveProperty<int>(_balanceData.initialRetention);
            Followers = new ReactiveProperty<int>(_balanceData.initialFollowers);
        }

        LoadData();
    }

    // =====================================================
    // セーブ / ロード
    // =====================================================

    public void SaveData()
    {
        var data = new ShopStatusData(Trust.Value, Attention.Value, Spread.Value, Retention.Value, Followers.Value);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(SavePath, json);
    }

    public void LoadData()
    {
        if (!File.Exists(SavePath)) return;

        string json = File.ReadAllText(SavePath);
        var data = JsonUtility.FromJson<ShopStatusData>(json);
        if (data == null) return;

        Trust.Value = data.trust;
        Attention.Value = data.attention;
        Spread.Value = data.spread;
        Retention.Value = data.retention;
        Followers.Value = data.followers;

        Debug.Log($"[ShopStatusModel] セーブデータを読み込みました: 信頼={data.trust} 注目={data.attention} 拡散={data.spread} 維持={data.retention} フォロワー={data.followers}");
    }

    /// <summary>
    /// ステータスを初期値にリセットする。
    /// ReactiveProperty のインスタンスは維持し、既存の Subscribe を壊さない。
    /// </summary>
    public void Reset()
    {
        if (_balanceData != null)
        {
            Trust.Value = _balanceData.initialTrust;
            Attention.Value = _balanceData.initialAttention;
            Spread.Value = _balanceData.initialSpread;
            Retention.Value = _balanceData.initialRetention;
            Followers.Value = _balanceData.initialFollowers;
        }
        else
        {
            Trust.Value = 50;
            Attention.Value = 0;
            Spread.Value = 0;
            Retention.Value = 0;
            Followers.Value = 0;
        }

        SaveData();
    }

    // =====================================================
    // ステータス変更メソッド（クランプ処理付き）
    // =====================================================

    /// <summary>
    /// 信頼度を変更する（加算）。上下限でクランプされる。
    /// </summary>
    public void ChangeTrust(int amount)
    {
        Trust.Value = ClampStat(Trust.Value + amount);
        Debug.Log($"[ShopStatus] 信頼度変化: {amount:+#;-#;0} → {Trust.Value}");
        SaveData();
    }

    /// <summary>
    /// 注目度を変更する（加算）。上下限でクランプされる。
    /// </summary>
    public void ChangeAttention(int amount)
    {
        Attention.Value = ClampStat(Attention.Value + amount);
        Debug.Log($"[ShopStatus] 注目度変化: {amount:+#;-#;0} → {Attention.Value}");
        SaveData();
    }

    /// <summary>
    /// 拡散力を変更する（加算）。上下限でクランプされる。
    /// </summary>
    public void ChangeSpread(int amount)
    {
        Spread.Value = ClampStat(Spread.Value + amount);
        Debug.Log($"[ShopStatus] 拡散力変化: {amount:+#;-#;0} → {Spread.Value}");
        SaveData();
    }

    /// <summary>
    /// 顧客維持力を変更する（加算）。上下限でクランプされる。
    /// </summary>
    public void ChangeRetention(int amount)
    {
        Retention.Value = ClampStat(Retention.Value + amount);
        Debug.Log($"[ShopStatus] 顧客維持力変化: {amount:+#;-#;0} → {Retention.Value}");
        SaveData();
    }

    /// <summary>
    /// 全ステータスを一括で変更する（加算）。
    /// バズの持続効果などで使用。
    /// </summary>
    public void ChangeAllStats(int amount)
    {
        Trust.Value = ClampStat(Trust.Value + amount);
        Attention.Value = ClampStat(Attention.Value + amount);
        Spread.Value = ClampStat(Spread.Value + amount);
        Retention.Value = ClampStat(Retention.Value + amount);
        Debug.Log($"[ShopStatus] 全ステータス変化: {amount:+#;-#;0}");
        SaveData();
    }

    /// <summary>
    /// フォロワー数を変更する（加算）。最小値は0。
    /// </summary>
    public void ChangeFollowers(int amount)
    {
        int min = _balanceData != null ? _balanceData.followerMin : 0;
        Followers.Value = Mathf.Max(min, Followers.Value + amount);
        Debug.Log($"[ShopStatus] フォロワー変化: {amount:+#;-#;0} → {Followers.Value}");
        SaveData();
    }

    /// <summary>
    /// ステータス値を上下限内にクランプする。
    /// </summary>
    private int ClampStat(int value)
    {
        int min = _balanceData != null ? _balanceData.statMin : 0;
        int max = _balanceData != null ? _balanceData.statMax : 100;
        return Mathf.Clamp(value, min, max);
    }
}
