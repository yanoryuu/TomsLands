using System;
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

    // --- 進行中のバズ（BuzzSystem）。効果データ(SO)は保存できないので種類から引き直す。旧セーブは欠損→バズなし ---
    public bool hasBuzz;
    public int buzzType;              // BuzzType を int で保存
    public int buzzRemainingTurns;
    public float buzzRevenueMultiplier;

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
    // 選択中スロット配下を参照する（static readonly だと型初期化時のスロットに固定されるためプロパティにする）。
    private static string SavePath => SaveSlotManager.GetPath("shopStatusData.json");

    // =====================================================
    // リアクティブプロパティ（UIバインディング用）
    // =====================================================

    /// <summary>信頼度（0〜100）：炎上を防ぎ、ポジティブバズになりやすくする</summary>
    public ReactiveProperty<int> Trust { get; }

    /// <summary>注目度（0〜100）：バズの発生確率に影響</summary>
    public ReactiveProperty<int> Attention { get; }

    /// <summary>拡散力（0〜100）：バズ発生時の効果の大きさに影響</summary>
    public ReactiveProperty<int> Spread { get; }

    /// <summary>顧客維持力（0〜100）：バズ効果の持続ターン数に影響</summary>
    public ReactiveProperty<int> Retention { get; }

    /// <summary>フォロワー数（0以上、上限なし）</summary>
    public ReactiveProperty<int> Followers { get; }

    /// <summary>ステータスの最大値（正規化に使用）</summary>
    public int StatMax => _balanceData != null ? _balanceData.statMax : 100;

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
        var data = new ShopStatusData(Trust.Value, Attention.Value, Spread.Value, Retention.Value, Followers.Value)
        {
            hasBuzz = SavedBuzz.HasValue,
            buzzType = SavedBuzz.HasValue ? (int)SavedBuzz.Value.Type : 0,
            buzzRemainingTurns = SavedBuzz?.RemainingTurns ?? 0,
            buzzRevenueMultiplier = SavedBuzz?.RevenueMultiplier ?? 1f,
        };
        string json = JsonUtility.ToJson(data, true);
        SaveFile.WriteAtomic(SavePath, json);
    }

    public void LoadData()
    {
        // 壊れていれば .bak から復旧。どちらも読めなければ今の値（初期値）のまま続行（SaveFile がログを出す）
        if (!SaveFile.TryReadJson<ShopStatusData>(SavePath, out var data)) return;

        Trust.Value = data.trust;
        Attention.Value = data.attention;
        Spread.Value = data.spread;
        Retention.Value = data.retention;
        Followers.Value = data.followers;
        SavedBuzz = data.hasBuzz && data.buzzRemainingTurns > 0
            ? new BuzzSnapshot((BuzzType)data.buzzType, data.buzzRemainingTurns, data.buzzRevenueMultiplier)
            : (BuzzSnapshot?)null;

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

        // 新しいランでは前のランのバズを持ち越さない（BuzzSystem は OnReset を受けて進行中のバズを捨てる）
        SavedBuzz = null;
        SaveData();
        _onReset.OnNext(Unit.Default);
    }

    // =====================================================
    // 進行中のバズの保存（BuzzSystem が書き、起動時に読み戻す）
    // =====================================================

    /// <summary>保存用のバズの状態（種類・残りターン・売上倍率）。効果データ(SO)は種類から引き直す。</summary>
    public readonly struct BuzzSnapshot
    {
        public readonly BuzzType Type;
        public readonly int RemainingTurns;
        public readonly float RevenueMultiplier;

        public BuzzSnapshot(BuzzType type, int remainingTurns, float revenueMultiplier)
        {
            Type = type;
            RemainingTurns = remainingTurns;
            RevenueMultiplier = revenueMultiplier;
        }
    }

    /// <summary>保存されている進行中のバズ（無ければ null）。</summary>
    public BuzzSnapshot? SavedBuzz { get; private set; }

    private readonly Subject<Unit> _onReset = new();

    /// <summary>Reset（新しいラン）の通知。</summary>
    public Observable<Unit> OnReset => _onReset;

    /// <summary>進行中のバズを保存する（null でバズなし）。BuzzSystem がバズの開始・残りターンの変化・終了のたびに呼ぶ。</summary>
    public void SaveBuzz(BuzzSnapshot? buzz)
    {
        SavedBuzz = buzz;
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
    /// <summary>
    /// 毎朝の減衰（H-3）。注目・拡散は早く、維持はゆっくり冷める。広告を「一度買えば終わり」から「張りどきを選ぶ」ものにする。
    /// 減衰率は GameBalanceData（配信で上書き可）。1 未満の端数は切り捨て（0 に向かって減る）。
    /// </summary>
    public void ApplyDailyDecay()
    {
        if (_balanceData == null) return;
        int Decay(int v, float rate) => rate <= 0f || v <= 0 ? v : ClampStat(Mathf.FloorToInt(v * (1f - Mathf.Clamp01(rate))));
        int t = Decay(Trust.Value, _balanceData.trustDecayRate);
        int a = Decay(Attention.Value, _balanceData.attentionDecayRate);
        int s = Decay(Spread.Value, _balanceData.spreadDecayRate);
        int r = Decay(Retention.Value, _balanceData.retentionDecayRate);
        if (t == Trust.Value && a == Attention.Value && s == Spread.Value && r == Retention.Value) return;
        Trust.Value = t;
        Attention.Value = a;
        Spread.Value = s;
        Retention.Value = r;
        Debug.Log($"[ShopStatus] 毎朝の減衰 → 信頼={t} 注目={a} 拡散={s} 維持={r}");
        SaveData();
    }

    private int ClampStat(int value)
    {
        int min = _balanceData != null ? _balanceData.statMin : 0;
        int max = _balanceData != null ? _balanceData.statMax : 100;
        return Mathf.Clamp(value, min, max);
    }
}
