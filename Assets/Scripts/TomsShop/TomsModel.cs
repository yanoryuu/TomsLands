using System.IO;
using UnityEngine;
using R3;

public class TomsModel
{
    public ReactiveProperty<int> PlayerMoney { get; private set; }
    
    public ReactiveProperty<int> BlacksmithLevel { get; private set; }
    
    public ReactiveProperty<int> ToolShopLevel { get; private set; }
    
    public ReactiveProperty<int> InfoBrokerLevel { get; private set; }

    /// <summary>
    /// 店レベル。同時陳列銘柄数・1銘柄あたり陳列個数・マシン設置枠を規定する
    /// （テーブルは ShopLevelSettings）。ラン内完結（ニューゲームで1に戻る）。
    /// </summary>
    public ReactiveProperty<int> ShopLevel { get; private set; }
    
    public ReactiveProperty<float> Trust { get; private set; }
    
    public ReactiveProperty<int> CurrentTurn { get; private set; }

    public ReactiveProperty<int> DebtCycle { get; private set; }

    /// <summary>
    /// GameFlowManagerの現在インデックス（セーブ/ロード用）
    /// </summary>
    public int GameFlowIndex { get; set; }

    /// <summary>
    /// 自動生成フローのシード（「続きから」で同一フローを再生成するため保存する）
    /// </summary>
    public int FlowSeed { get; set; }

    /// <summary>選択中のゲームモード（自動生成時）</summary>
    public GameModeId GameMode { get; set; } = GameModeId.Short;

    /// <summary>true=自動生成フロー / false=手動(SO)フロー</summary>
    public bool UseAutoFlow { get; set; }

    /// <summary>
    /// 準備シーンで借りた初期資金の元本。初回返済に利息付きで上乗せされる（DebtCalculator）。
    /// </summary>
    public int BorrowedPrincipal { get; set; }

    /// <summary>スタートダッシュ「返済猶予証」による初回返済額の割引率（0=なし）。</summary>
    public float FirstDebtDiscountRate { get; set; }

    public TomsModel()
    {
        // ReactivePropertyの初回作成（一度だけ）
        PlayerMoney = new ReactiveProperty<int>(GameConst.InitMoney);
        BlacksmithLevel = new ReactiveProperty<int>(1);
        ToolShopLevel = new ReactiveProperty<int>(1);
        InfoBrokerLevel = new ReactiveProperty<int>(1);
        ShopLevel = new ReactiveProperty<int>(1);
        Trust = new ReactiveProperty<float>(1f);
        CurrentTurn = new ReactiveProperty<int>(1);
        DebtCycle = new ReactiveProperty<int>(0);
        GameFlowIndex = 0;

        LoadPlayerMoney();
    }

    /// <summary>
    /// 値をリセットする。ReactivePropertyのインスタンスは維持し、既存のSubscribeを壊さない。
    /// </summary>
    public void Initialize(int defaultMoney = -1, int defaultBlacksmithLevel = 1, int defaultToolLevel = 1, int defaultInfoBrokerLevel = 1, float defaultTrust = 1, int defaultTurn = 1, int defaultShopLevel = 1)
    {
        // defaultMoney 未指定（負値）なら GameConst の初期所持金を使う。
        // ※ デフォルト引数はコンパイル時定数が必須で GameConst.InitMoney（実行時プロパティ）を直接使えないため、この方式にしている。
        if (defaultMoney < 0) defaultMoney = GameConst.InitMoney;

        PlayerMoney.Value = defaultMoney;
        BlacksmithLevel.Value = defaultBlacksmithLevel;
        ToolShopLevel.Value = defaultToolLevel;
        InfoBrokerLevel.Value = defaultInfoBrokerLevel;
        // defaultShopLevel 引数は将来のメタ進行（初期店レベルの底上げ）の差し込み口
        ShopLevel.Value = Mathf.Max(1, defaultShopLevel);
        Trust.Value = defaultTrust;
        CurrentTurn.Value = defaultTurn;
        DebtCycle.Value = 0;
        BorrowedPrincipal = 0;
        FirstDebtDiscountRate = 0f;
    }

    /// <summary>
    /// 次回返済額を返す（DebtCycle + 1 のサイクル分）。
    /// </summary>
    public int GetNextDebtAmount()
    {
        return GameConst.GetDebtAmount(DebtCycle.Value + 1);
    }

    public void SavePlayerMoney()
    {
        var data = new TomsData(PlayerMoney.Value, BlacksmithLevel.Value, CurrentTurn.Value, GameFlowIndex, InfoBrokerLevel.Value, DebtCycle.Value,
            FlowSeed, (int)GameMode, UseAutoFlow, ToolShopLevel.Value, Trust.Value, ShopLevel.Value,
            BorrowedPrincipal, FirstDebtDiscountRate);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(SaveSlotManager.GetPath("tomsData.json"), json);
    }

    public void LoadPlayerMoney()
    {
        string path = SaveSlotManager.GetPath("tomsData.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<TomsData>(json);
            PlayerMoney.Value = data.shopMoney;
            BlacksmithLevel.Value = data.blacksmithLevel;
            InfoBrokerLevel.Value = data.infoBrokerLevel;
            CurrentTurn.Value = data.currentTurn;
            GameFlowIndex = data.gameFlowIndex;
            DebtCycle.Value = data.debtCycle;
            FlowSeed = data.flowSeed;
            GameMode = (GameModeId)data.gameMode;
            UseAutoFlow = data.useAutoFlow;
            // 旧セーブには無いフィールド（欠損時 0）→ 初期値 1 に正規化
            ToolShopLevel.Value = Mathf.Max(1, data.toolShopLevel);
            Trust.Value = data.trust > 0f ? data.trust : 1f;
            ShopLevel.Value = Mathf.Max(1, data.shopLevel);
            BorrowedPrincipal = Mathf.Max(0, data.borrowedPrincipal);
            FirstDebtDiscountRate = Mathf.Clamp01(data.firstDebtDiscountRate);
        }
        else
        {
            PlayerMoney.Value = GameConst.InitMoney;
            BlacksmithLevel.Value = 1;
            ToolShopLevel.Value = 1;
            InfoBrokerLevel.Value = 1;
            GameFlowIndex = 0;
            DebtCycle.Value = 0;
            FlowSeed = 0;
            GameMode = GameModeId.Short;
            UseAutoFlow = false;
        }
    }

    /// <summary>
    /// 収入を所持金に加算する（旧名: Settlement。売り注文の「約定」と紛らわしいためリネーム）。
    /// </summary>
    public void AddRevenue(int price)
    {
        PlayerMoney.Value += price;
    }

    public void PurchaseItem(int price)
    {
        //購入処理
        PlayerMoney.Value -= price;
        Debug.Log(PlayerMoney.Value);
    }

    // ========================================
    // 当日の仕入れ支出トラッカー（ターン評価の「資金効率」用）
    // 永続化はしない（日をまたぐと GameFlowManager.NextTurn がリセットする）
    // ========================================

    /// <summary>この日（ターン）に仕入れへ投じた金額の累計。</summary>
    public int TurnProcurementSpend { get; private set; }

    /// <summary>仕入れ支出を記録する。仕入れ経路（鍛冶屋購入・自動仕入れ）から呼ぶ。</summary>
    public void RecordProcurementSpend(int amount)
    {
        if (amount > 0) TurnProcurementSpend += amount;
    }

    /// <summary>日送り時に GameFlowManager から呼ばれるリセット。</summary>
    public void ResetTurnProcurementSpend()
    {
        TurnProcurementSpend = 0;
    }

    /// <summary>
    /// 鍛冶屋レベルを1上げる。成功なら true を返す。
    /// </summary>
    public bool UpgradeBlacksmith()
    {
        if (BlacksmithLevel.Value >= GameConst.MaxBlackSmithLevel)
            return false;

        int cost = GameConst.GetBlackSmithLevelUpCost(BlacksmithLevel.Value);
        if (cost < 0 || PlayerMoney.Value < cost)
            return false;

        PlayerMoney.Value -= cost;
        BlacksmithLevel.Value++;
        SavePlayerMoney();

        Debug.Log($"[BlackSmith] 鍛冶屋レベルアップ: Lv.{BlacksmithLevel.Value - 1} → Lv.{BlacksmithLevel.Value} (費用: {cost}G)");
        return true;
    }

    /// <summary>
    /// 情報屋レベルを1上げる。成功なら true を返す。
    /// UpgradeBlacksmith と同じ流儀（MAX判定 → コスト → 所持金 → 減算 → 即セーブ）。
    /// </summary>
    public bool UpgradeInfoBroker()
    {
        if (InfoBrokerLevel.Value >= GameConst.MaxInfoBrokerLevel)
            return false;

        int cost = GameConst.GetInfoBrokerLevelUpCost(InfoBrokerLevel.Value);
        if (cost < 0 || PlayerMoney.Value < cost)
            return false;

        PlayerMoney.Value -= cost;
        InfoBrokerLevel.Value++;
        SavePlayerMoney();

        Debug.Log($"[InfoBroker] 情報屋レベルアップ: Lv.{InfoBrokerLevel.Value - 1} → Lv.{InfoBrokerLevel.Value} (費用: {cost}G)");
        return true;
    }

    /// <summary>
    /// 店レベルを1上げる（ゴールド購入）。成功なら true を返す。
    /// UpgradeBlacksmith と同じ流儀（MAX判定 → コスト → 所持金 → 減算 → 即セーブ）。
    /// </summary>
    public bool UpgradeShop(ShopLevelSettings settings)
    {
        if (settings == null) return false;
        if (ShopLevel.Value >= settings.MaxLevel)
            return false;

        int cost = settings.GetLevelUpCost(ShopLevel.Value);
        if (cost < 0 || PlayerMoney.Value < cost)
            return false;

        PlayerMoney.Value -= cost;
        ShopLevel.Value++;
        SavePlayerMoney();

        Debug.Log($"[ShopUpgrade] 店レベルアップ: Lv.{ShopLevel.Value - 1} → Lv.{ShopLevel.Value} (費用: {cost}G)");
        return true;
    }
}