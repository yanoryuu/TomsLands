using System.IO;
using UnityEngine;
using R3;

public class TomsModel
{
    public ReactiveProperty<int> PlayerMoney { get; private set; }
    
    public ReactiveProperty<int> BlacksmithLevel { get; private set; }
    
    public ReactiveProperty<int> ToolShopLevel { get; private set; }
    
    public ReactiveProperty<int> InfoBrokerLevel { get; private set; }
    
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

    public TomsModel()
    {
        // ReactivePropertyの初回作成（一度だけ）
        PlayerMoney = new ReactiveProperty<int>(GameConst.InitMoney);
        BlacksmithLevel = new ReactiveProperty<int>(1);
        ToolShopLevel = new ReactiveProperty<int>(1);
        InfoBrokerLevel = new ReactiveProperty<int>(1);
        Trust = new ReactiveProperty<float>(1f);
        CurrentTurn = new ReactiveProperty<int>(1);
        DebtCycle = new ReactiveProperty<int>(0);
        GameFlowIndex = 0;

        LoadPlayerMoney();
    }

    /// <summary>
    /// 値をリセットする。ReactivePropertyのインスタンスは維持し、既存のSubscribeを壊さない。
    /// </summary>
    public void Initialize(int defaultMoney = -1, int defaultBlacksmithLevel = 1, int defaultToolLevel = 1, int defaultInfoBrokerLevel = 1, float defaultTrust = 1, int defaultTurn = 1)
    {
        // defaultMoney 未指定（負値）なら GameConst の初期所持金を使う。
        // ※ デフォルト引数はコンパイル時定数が必須で GameConst.InitMoney（実行時プロパティ）を直接使えないため、この方式にしている。
        if (defaultMoney < 0) defaultMoney = GameConst.InitMoney;

        PlayerMoney.Value = defaultMoney;
        BlacksmithLevel.Value = defaultBlacksmithLevel;
        ToolShopLevel.Value = defaultToolLevel;
        InfoBrokerLevel.Value = defaultInfoBrokerLevel;
        Trust.Value = defaultTrust;
        CurrentTurn.Value = defaultTurn;
        DebtCycle.Value = 0;
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
            FlowSeed, (int)GameMode, UseAutoFlow);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(Application.persistentDataPath + "/tomsData.json", json);
    }

    public void LoadPlayerMoney()
    {
        string path = Application.persistentDataPath + "/tomsData.json";
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

    
    public void Settlement(int price)
    {
        // 売り処理
        PlayerMoney.Value += price;
        
    }

    public void PurchaseItem(int price)
    {
        //購入処理
        PlayerMoney.Value -= price;
        Debug.Log(PlayerMoney.Value);
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
}